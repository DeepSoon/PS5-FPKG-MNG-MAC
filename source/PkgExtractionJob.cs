using System;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class PkgExtractionRequest
    {
        internal string Package;
        internal string Destination;
        internal string Temporary;
        internal string Passcode;
        internal bool Verify;

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Package)) throw new ArgumentException("Select a .pkg source file.");
            if (string.IsNullOrWhiteSpace(Destination)) throw new ArgumentException("Choose a destination folder.");
            if (string.IsNullOrWhiteSpace(Temporary)) throw new ArgumentException("Choose a temporary folder.");
            Package = Path.GetFullPath(Package.Trim());
            Destination = Path.GetFullPath(Destination.Trim());
            Temporary = Path.GetFullPath(Temporary.Trim());
            Passcode = string.IsNullOrEmpty(Passcode) ? new string('0', 32) : Passcode;

            if (!File.Exists(Package)) throw new FileNotFoundException("Source PKG not found.", Package);
            if (!Path.GetExtension(Package).Equals(".pkg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Select a .pkg source file.");
            if (Passcode.Length != 32) throw new ArgumentException("PKG passcode must contain exactly 32 characters.");
            if (Directory.Exists(Destination) || File.Exists(Destination)) throw new IOException("The destination already exists. Choose a new folder name or remove the existing folder.");
            if (File.Exists(Temporary)) throw new IOException("The temporary path must be a folder, not a file.");

            string packageDirectory = Path.GetDirectoryName(Package);
            if (Destination.Equals(packageDirectory, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The destination must be a new folder, not the folder containing the PKG.");
        }
    }

    internal static class PkgExtractionJob
    {
        internal static string Run(PkgExtractionRequest request, CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            string extractionWork = null;
            string outputWork = null;
            try
            {
                request.Validate();
                token.ThrowIfCancellationRequested();

                string destinationParent = Path.GetDirectoryName(request.Destination);
                if (string.IsNullOrEmpty(destinationParent)) throw new ArgumentException("Choose a valid destination folder.");
                Directory.CreateDirectory(destinationParent);
                Directory.CreateDirectory(request.Temporary);

                extractionWork = FileWorkspace.CreateOwned(request.Temporary);
                outputWork = FileWorkspace.CreateOwned(destinationParent);
                string dump = Path.Combine(extractionWork, "pkg-source");
                string staged = Path.Combine(outputWork, "extracted");

                report(new ConversionProgress("Reading PKG", "Opening package container", 0, 0));
                log("Source PKG: " + request.Package);
                log("Destination: " + request.Destination);
                log("The original PKG is opened read-only and is never modified.");

                report(new ConversionProgress("Extracting PKG", "Reading inner files and CNT metadata", 0, new FileInfo(request.Package).Length));
                string source = PkgRebuildJob.ExtractSource(request.Package, dump, request.Passcode, false, token, log);
                token.ThrowIfCancellationRequested();

                report(new ConversionProgress("Scanning extracted files", "Preparing final folder", 0, 0));
                CopyPlan plan = FileWorkspace.Scan(source, token);
                log("Extracted source contains " + plan.Files + " files / " + FileWorkspace.Size(plan.Bytes) + ".");

                long free = FileWorkspace.FreeBytes(destinationParent);
                if (free < plan.Bytes) throw new IOException("Not enough free space in the destination drive. Required: " + FileWorkspace.Size(plan.Bytes) + ".");

                FileWorkspace.Copy(plan, staged, request.Verify, token, report);
                token.ThrowIfCancellationRequested();

                report(new ConversionProgress("Finalizing", "Publishing extracted folder", plan.Bytes, plan.Bytes));
                if (Directory.Exists(request.Destination) || File.Exists(request.Destination))
                    throw new IOException("The destination was created by another process while extraction was running.");
                Directory.Move(staged, request.Destination);
                log("PKG extracted successfully: " + request.Destination);
                return request.Destination;
            }
            finally
            {
                if (extractionWork != null) FileWorkspace.DeleteOwned(extractionWork, log);
                if (outputWork != null) FileWorkspace.DeleteOwned(outputWork, log);
            }
        }
    }
}
