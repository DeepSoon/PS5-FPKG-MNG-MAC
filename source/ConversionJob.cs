using System;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class ConversionRequest
    {
        internal string Image;
        internal string Output;
        internal string Temporary;
        internal string Tool;
        internal bool VerifyCopy;
        internal ulong TargetSdk;
        internal ImageFormat Format;
        internal ImageFormat DetectedFormat;

        internal void Validate()
        {
            Image = Path.GetFullPath(Image.Trim());
            Output = Path.GetFullPath(Output.Trim());
            Temporary = Path.GetFullPath(Temporary.Trim());
            if (!File.Exists(Image)) throw new FileNotFoundException("Source image not found.", Image);
            DetectedFormat = ImageFormats.Detect(Image);
            if (Format != ImageFormat.Auto && Format != DetectedFormat)
                throw new InvalidDataException("The image header is " + ImageFormats.Name(DetectedFormat) + ". Select that format or Auto-detect.");
            if (DetectedFormat == ImageFormat.Exfat)
            {
                if (string.IsNullOrWhiteSpace(Tool)) throw new FileNotFoundException("Select OSFMount.com for exFAT conversion.");
                Tool = Path.GetFullPath(Tool.Trim());
                if (!File.Exists(Tool) || !Path.GetFileName(Tool).Equals("OSFMount.com", StringComparison.OrdinalIgnoreCase))
                    throw new FileNotFoundException("Select OSFMount.com from the OSFMount installation folder.");
                ExfatMount.ValidateImage(Image);
            }
            else if (DetectedFormat == ImageFormat.Pfs) PfsExtraction.ValidateBackend();
            else if (DetectedFormat == ImageFormat.Ufs2) UfsExtraction.ValidateBackend();
            if (!Path.GetExtension(Output).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output filename must have a .pkg extension.");
            if (Output.Equals(Image, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and output are the same file.");
            if (File.Exists(Output) || Directory.Exists(Output)) throw new IOException("The output already exists. Choose another name.");
            if (File.Exists(Temporary)) throw new IOException("Select a temporary folder.");
        }
    }

    internal sealed class ConversionResult
    {
        internal string Output;
        internal string Warning;
    }

    internal static class ConversionJob
    {
        internal static ConversionResult Run(ConversionRequest request, CancellationToken token,
            Func<string, string, string, CancellationToken, object> prepareOptions,
            Action<ConversionProgress> report, Action<string> log)
        {
            string work = null, outputWork = null;
            ExfatMount mount = null;
            try
            {
                request.Validate(); token.ThrowIfCancellationRequested();
                string outputParent = Path.GetDirectoryName(request.Output);
                work = FileWorkspace.CreateOwned(request.Temporary);
                outputWork = FileWorkspace.CreateOwned(outputParent);
                string dump = Path.Combine(work, "dump"), scratch = Path.Combine(work, "builder-temp");
                Directory.CreateDirectory(scratch);
                if (request.DetectedFormat == ImageFormat.Pfs)
                {
                    dump = PfsExtraction.Extract(request.Image, work, request.VerifyCopy, token, report, log);
                    log("FFPFS / FFPFSC / PFS is read directly; no Windows drive was mounted, so no eject is required.");
                }
                else if (request.DetectedFormat == ImageFormat.Ufs2)
                {
                    dump = UfsExtraction.Extract(request.Image, work, token, report, log);
                    log("FFPKG / UFS2 is extracted directly; no Windows drive was mounted, so no eject is required.");
                }
                else
                {
                    report(new ConversionProgress("Mounting exFAT", "Opening the image read-only", 0, 0));
                    mount = new ExfatMount(request.Tool, request.Image, log);
                    mount.Attach(token);
                    string source = FileWorkspace.GameRoot(mount.Root);
                    if (FileWorkspace.Inside(work, source) || FileWorkspace.Inside(outputWork, source))
                        throw new IOException("Working and output folders must be outside the mounted source.");

                    // exFAT is already exposed by OSFMount as a normal read-only filesystem.
                    // Do not clone the dump: LibProsperoPkg reads directly from the mounted source
                    // while all generated/intermediate data goes to outputWork/scratch.
                    dump = source;
                    report(new ConversionProgress("Mounted exFAT", "Using the mounted game folder directly", 0, 0));
                    log("Direct exFAT source: " + dump);
                    log("Dump copy skipped. The exFAT mount stays attached until PKG build and verification finish.");
                }

                // Extracted PFS/UFS2 sources are writable, so keep the legacy pre-retarget there.
                // The exFAT source is intentionally mounted read-only; SdkVersionOverride below
                // updates executable SDK metadata and generated sce_sys/param.json in the output
                // without modifying the mounted image.
                if (request.DetectedFormat != ImageFormat.Exfat)
                {
                    string paramPath = Path.Combine(dump, "sce_sys", "param.json");
                    PackageParam param = new PackageParam(File.ReadAllBytes(paramPath));
                    param.Retarget(SdkVersion.PackageVersion(request.TargetSdk));
                    File.WriteAllBytes(paramPath, param.Bytes());
                }
                report(new ConversionProgress("Reading metadata", "Preparing PKG options", 0, 0));
                object options = prepareOptions(dump, outputWork, scratch, token);
                BuilderBridge.Set(options, "SdkVersionOverride", request.TargetSdk);
                log("Executable SDK: " + SdkVersion.Hex(request.TargetSdk) + "; package declaration: " + SdkVersion.Hex(SdkVersion.PackageVersion(request.TargetSdk)));
                token.ThrowIfCancellationRequested();
                log("Content ID: " + BuilderBridge.Get(options, "ContentId"));
                log("PKG mode: " + BuilderBridge.Get(options, "Mode") + "; image: " + BuilderBridge.Get(options, "PublisherImageMode"));
                report(new ConversionProgress("Building PKG", "Processing with LibProsperoPkg", 0, 0));
                string package = Path.GetFullPath(BuilderBridge.Build(options, log));
                if (!FileWorkspace.Inside(package, outputWork) || !File.Exists(package) ||
                    !Path.GetExtension(package).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The builder did not return a valid PKG in the expected folder.");
                FileWorkspace.NoReparse(package);
                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Verifying PKG", "Checking the container and SHA-256", 0, 0));
                BuilderBridge.Verify(package, options, token, log);
                token.ThrowIfCancellationRequested();
                FileWorkspace.Commit(package, request.Output);
                log("PKG created: " + request.Output);
                // If the first exFAT detach pass was temporarily blocked by Windows,
                // retry once more before reporting the final result to the UI.
                if (mount != null) mount.Dispose();
                return new ConversionResult { Output = request.Output, Warning = mount == null ? null : mount.CleanupWarning };
            }
            finally
            {
                if (mount != null) mount.Dispose();
                report(new ConversionProgress("Cleaning temporary files", "Removing working files", 0, 0));
                FileWorkspace.DeleteOwned(work, log);
                FileWorkspace.DeleteOwned(outputWork, log);
            }
        }
    }
}
