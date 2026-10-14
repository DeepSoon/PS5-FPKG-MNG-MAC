using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FpkgExfat
{
    internal sealed class MacBuildRequest
    {
        internal string Source;
        internal string Output;
        internal string Temporary;
        internal string Passcode;
        internal string Sdk = "9.00";
        internal string Mode = "Application";
        internal bool IsImage;

        internal void Validate(bool isImage)
        {
            IsImage = isImage;
            if (string.IsNullOrWhiteSpace(Source)) throw new ArgumentException(isImage ? "Choose an .exfat image." : "Choose a prepared game folder.");
            if (string.IsNullOrWhiteSpace(Output)) throw new ArgumentException("Choose an output .pkg file.");
            if (string.IsNullOrWhiteSpace(Temporary)) throw new ArgumentException("Choose a temporary folder.");
            Source = Path.GetFullPath(Source.Trim());
            Output = Path.GetFullPath(Output.Trim());
            Temporary = Path.GetFullPath(Temporary.Trim());
            Passcode = string.IsNullOrEmpty(Passcode) ? new string('0', 32) : Passcode.Trim();
            if (Passcode.Length != 32) throw new ArgumentException("Passcode must contain exactly 32 characters.");
            if (!Path.GetExtension(Output).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output filename must have a .pkg extension.");
            if (File.Exists(Output) || Directory.Exists(Output))
                throw new IOException("The output already exists. Choose another name.");
            if (File.Exists(Temporary)) throw new IOException("The temporary path must be a folder, not a file.");
            if (isImage)
            {
                if (!File.Exists(Source)) throw new FileNotFoundException("Source image not found.", Source);
                ImageFormat detected = ImageFormats.Detect(Source);
                if (detected != ImageFormat.Exfat)
                    throw new NotSupportedException("On macOS only exFAT images are supported here (" + ImageFormats.Name(detected) +
                        " needs the Windows-only PFS/UFS2 readers).");
            }
            else
            {
                if (!Directory.Exists(Source)) throw new DirectoryNotFoundException("Source folder not found: " + Source);
                if (!File.Exists(Path.Combine(Source, "sce_sys", "param.json")))
                    throw new InvalidDataException("sce_sys/param.json was not found in the selected source folder.");
            }
            if (string.IsNullOrWhiteSpace(Sdk)) Sdk = "9.00";
            if (string.IsNullOrWhiteSpace(Mode)) Mode = "Application";
        }
    }

    internal static class MacBuilder
    {
        internal static string Build(MacBuildRequest request, CancellationToken token,
            Action<ConversionProgress> report, Action<string> log)
        {
            string work = null, outputWork = null;
            try
            {
                token.ThrowIfCancellationRequested();
                string outputParent = Path.GetDirectoryName(request.Output);
                if (string.IsNullOrEmpty(outputParent)) throw new ArgumentException("Choose a valid output .pkg path.");
                Directory.CreateDirectory(outputParent);
                Directory.CreateDirectory(request.Temporary);
                work = FileWorkspace.CreateOwned(request.Temporary);
                outputWork = FileWorkspace.CreateOwned(outputParent);
                string scratch = Path.Combine(work, "builder-temp");
                Directory.CreateDirectory(scratch);

                string gameRoot;
                if (request.IsImage)
                {
                    // Stock macOS cannot mount raw exFAT dumps (no partition map),
                    // so the image is staged through the bundled pure-Python reader
                    // (classic dump-copy flow) instead of the Windows zero-copy mount.
                    report(new ConversionProgress("Reading exFAT", "Extracting the image to a temporary folder", 0, 0));
                    log("macOS reads raw exFAT dumps through a staged copy; the image itself stays untouched.");
                    gameRoot = MacExfatExtract.Extract(request.Source, work, token, report, log);
                    log("Game root: " + gameRoot);
                }
                else
                {
                    gameRoot = Path.GetFullPath(request.Source);
                    log("Source folder: " + gameRoot);
                }
                token.ThrowIfCancellationRequested();

                report(new ConversionProgress("Reading metadata", "Preparing PKG options", 0, 0));
                object options = PrepareOptions(gameRoot, outputWork, scratch, request, token, log);
                token.ThrowIfCancellationRequested();

                // The library build only emits text logs, so a monitor thread
                // samples the work folders to stream live byte progress.
                long estimate = Math.Max(1, FolderBytes(gameRoot, token));
                log("Source size: " + FileWorkspace.Size(estimate) + " (progress reference)");
                report(new ConversionProgress("Building PKG", "Processing with LibProsperoPkg", 0, estimate));
                string ow = outputWork, sc = scratch;
                var monCts = new CancellationTokenSource();
                // Low-CPU sampling: 750 ms cadence + skip the tree walk when
                // neither work folder changed since the previous sample.
                long lastSeen = -1;
                DateTime lastOwWrite = DateTime.MinValue, lastScWrite = DateTime.MinValue;
                var monTask = Task.Run(delegate
                {
                    while (!monCts.IsCancellationRequested)
                    {
                        try
                        {
                            DateTime owW = SafeWriteTime(ow), scW = SafeWriteTime(sc);
                            if (owW != lastOwWrite || scW != lastScWrite || lastSeen < 0)
                            {
                                lastOwWrite = owW; lastScWrite = scW;
                                lastSeen = DirBytes(ow) + DirBytes(sc);
                                report(new ConversionProgress("Building PKG", "Processing with LibProsperoPkg", Math.Max(0, lastSeen), estimate));
                            }
                        }
                        catch { }
                        try { monCts.Token.WaitHandle.WaitOne(750); }
                        catch { break; }
                    }
                });
                string package;
                try { package = Path.GetFullPath(BuildPackage(options, log)); }
                finally { try { monCts.Cancel(); monTask.Wait(3000); } catch { } }
                report(new ConversionProgress("Building PKG", "Processing with LibProsperoPkg", estimate, estimate));
                if (!FileWorkspace.Inside(package, outputWork) || !File.Exists(package) ||
                    !Path.GetExtension(package).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The builder did not return a valid PKG in the expected folder.");
                FileWorkspace.NoReparse(package);
                token.ThrowIfCancellationRequested();

                report(new ConversionProgress("Verifying PKG", "Checking the container and SHA-256", 0, 0));
                MacVerify(package, token, log);
                token.ThrowIfCancellationRequested();

                FileWorkspace.Commit(package, request.Output);
                log("PKG created: " + request.Output);
                return request.Output;
            }
            finally
            {
                report(new ConversionProgress("Cleaning temporary files", "Removing working files", 0, 0));
                FileWorkspace.DeleteOwned(work, log);
                FileWorkspace.DeleteOwned(outputWork, log);
            }
        }

        private static object PrepareOptions(string gameRoot, string outputWork, string scratch,
            MacBuildRequest request, CancellationToken token, Action<string> log)
        {
            byte[] rawParam = File.ReadAllBytes(Path.Combine(gameRoot, "sce_sys", "param.json"));
            PackageParam param = new PackageParam(rawParam);
            string contentId = param.Text("contentId");
            string titleId = param.Text("titleId");
            if (string.IsNullOrEmpty(titleId)) titleId = TitleFromContent(contentId);
            string title = param.Text("titleName");
            if (string.IsNullOrEmpty(title)) title = param.Text("name");
            if (string.IsNullOrEmpty(title)) title = titleId;
            string version = param.Text("contentVersion");
            if (string.IsNullOrEmpty(version)) version = "01.000.000";

            Type optType = BuilderBridge.Library.GetType("LibProsperoPkg.ProsperoBuildOptions", true);
            object options = Activator.CreateInstance(optType);
            Set(options, "SourceFolder", gameRoot);
            Set(options, "OutputFolder", outputWork);
            Set(options, "TemporaryDirectory", scratch);
            Set(options, "CancellationToken", token);
            Set(options, "Mode", Enum.Parse(BuilderBridge.Library.GetType("LibProsperoPkg.ProsperoPackageMode", true), request.Mode, true));
            Set(options, "PublisherImageMode", Enum.Parse(BuilderBridge.Library.GetType("LibProsperoPkg.ProsperoPublisherImageMode", true), "PlaintextNoAuth"));
            Set(options, "Passcode", request.Passcode);
            Set(options, "ContentId", contentId);
            Set(options, "PrimaryId", contentId);
            Set(options, "TitleId", titleId);
            Set(options, "Title", title);
            Set(options, "Version", version);
            Set(options, "OutputFormat", Enum.Parse(BuilderBridge.Library.GetType("LibProsperoPkg.ProsperoOutputFormat", true), "DebugImage"));
            Set(options, "UsePublisherPprNaps", true);
            Set(options, "GenerateParamJsonIfMissing", true);
            // On macOS the native Oodle backend (libScePubTools, 64-bit Windows only)
            // is unavailable: Automatic falls back to spans our own reader cannot
            // decode. BuiltIn is pure managed and round-trips correctly.
            Type krakenBackendType = null;
            foreach (Type t in BuilderBridge.Library.GetTypes()) if (t.Name == "ProsperoKrakenBackend") krakenBackendType = t;
            Set(options, "KrakenBackend", Enum.Parse(krakenBackendType, "BuiltIn"));
            log("Kraken backend: BuiltIn (managed, macOS-compatible)");
            ulong sdk = SdkVersion.ForBuild(request.Sdk);
            Set(options, "SdkVersionOverride", sdk);
            log("Content ID: " + contentId);
            log("Title: " + title + " [" + titleId + "] v" + version);
            log("Executable SDK: " + SdkVersion.Hex(sdk) + "; package declaration: " + SdkVersion.Hex(SdkVersion.PackageVersion(sdk)));
            log("PKG mode: " + request.Mode + "; image: PlaintextNoAuth (debug FIH)");
            return options;
        }

        private static string BuildPackage(object options, Action<string> log)
        {
            Type type = BuilderBridge.Library.GetType("LibProsperoPkg.ProsperoPackageBuilder", true);
            MethodInfo build = type.GetMethod("Build", BuilderBridge.Members, null,
                new Type[] { options.GetType(), typeof(Action<string>) }, null);
            if (build == null) throw new MissingMethodException(type.FullName, "Build");
            object result = BuilderBridge.Invoke(build, null, options, log);
            System.Collections.IEnumerable warnings = BuilderBridge.Get(result, "Warnings") as System.Collections.IEnumerable;
            if (warnings != null) foreach (object w in warnings) log("Builder: " + w);
            return (string)BuilderBridge.Get(result, "OutputPath");
        }

        internal static void MacVerify(string package, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            FileInfo fi = new FileInfo(package);
            if (!fi.Exists || fi.Length < 65536) throw new InvalidDataException("The built PKG is missing or truncated.");
            using (FileStream s = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] magic = new byte[8];
                if (s.Read(magic, 0, magic.Length) != magic.Length) throw new EndOfStreamException();
                if (!(magic[0] == 0x7F && magic[1] == (byte)'F' && magic[2] == (byte)'I' && magic[3] == (byte)'H'))
                    throw new InvalidDataException("The output is not a debug FIH package.");
            }
            try
            {
                object detected = PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPkgReader", "DetectType",
                    new Type[] { typeof(string) }, package);
                log("Detected container: " + Convert.ToString(detected, CultureInfo.InvariantCulture));
            }
            catch (Exception e) { log("Type detection warning: " + e.Message); }
            token.ThrowIfCancellationRequested();
            using (SHA256 sha = SHA256.Create())
            using (FileStream s = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
            {
                byte[] hash = sha.ComputeHash(s);
                log("Container bytes: " + fi.Length);
                log("SHA-256 PKG: " + BitConverter.ToString(hash).Replace("-", ""));
            }
        }

        private static DateTime SafeWriteTime(string root)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return DateTime.MinValue;
                return Directory.GetLastWriteTimeUtc(root);
            }
            catch { return DateTime.MinValue; }
        }

        private static long FolderBytes(string root, CancellationToken token)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    try { total = checked(total + new FileInfo(f).Length); }
                    catch { }
                }
            }
            catch { }
            return Math.Max(0, total);
        }

        private static long DirBytes(string root)
        {
            long total = 0;
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { total = checked(total + new FileInfo(f).Length); }
                    catch { }
                }
            }
            catch { }
            return Math.Max(0, total);
        }

        private static void Set(object target, string name, object value)        {
            PropertyInfo p = target.GetType().GetProperty(name, BuilderBridge.Members);
            if (p == null) throw new MissingMemberException(target.GetType().FullName, name);
            p.SetValue(target, value, null);
        }

        private static string TitleFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            int dash = content.IndexOf('-');
            if (dash < 0 || dash + 10 >= content.Length) return "";
            string candidate = content.Substring(dash + 1);
            int us = candidate.IndexOf('_');
            if (us > 0) candidate = candidate.Substring(0, us);
            return candidate.Length >= 9 ? candidate.Substring(0, 9) : candidate;
        }
    }

    // Staged exFAT extraction via the bundled pure-Python reader (stdlib only).
    // Stock macOS cannot mount raw exFAT dumps, so this replaces the Windows
    // OSFMount zero-copy flow. The image is only ever read.
    internal static class MacExfatExtract
    {
        internal static string Extract(string image, string workParent, CancellationToken token,
            Action<ConversionProgress> report, Action<string> log)
        {
            string script = FindScript();
            string python = FindPython();
            string dest = Path.Combine(workParent, "exfat-source");
            if (Directory.Exists(dest) || File.Exists(dest)) throw new IOException("Temporary extraction folder already exists.");
            string found = null;
            ToolResult r = ProcessRunner.Run(python,
                new string[] { script, "extract", image, dest, "--verify" },
                0, token, delegate(string line)
                {
                    if (line != null && line.StartsWith("FPKG_PROGRESS\t", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(new char[] { '\t' }, 5);
                        long done, total;
                        if (parts.Length == 5 && long.TryParse(parts[2], out done) && long.TryParse(parts[3], out total))
                            report(new ConversionProgress(parts[1], parts[4], done, total));
                    }
                    else if (line != null && line.StartsWith("OK\t", StringComparison.Ordinal))
                    {
                        found = line.Substring(3).Trim();
                    }
                    else if (log != null) log(line);
                }, true);
            if (r.ExitCode != 0) throw new InvalidDataException("exFAT extraction failed.\r\n" + r.Output);
            if (string.IsNullOrEmpty(found)) throw new InvalidDataException("The exFAT reader did not return a game folder.");
            string root = Path.GetFullPath(found);
            if (!FileWorkspace.Inside(root, dest) || !Directory.Exists(root))
                throw new InvalidDataException("The exFAT reader returned an unexpected folder.");
            FileWorkspace.NoReparse(root);
            if (!File.Exists(Path.Combine(root, "sce_sys", "param.json")))
                throw new InvalidDataException("sce_sys/param.json is missing from the extracted exFAT image.");
            return root;
        }

        private static string FindPython()
        {
            foreach (string c in new string[] { "/usr/bin/python3", "/opt/homebrew/bin/python3",
                "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3", "python3" })
            {
                try
                {
                    if (!c.Contains("/") || File.Exists(c)) return c;
                }
                catch { }
            }
            return "python3";
        }

        private static string FindScript()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string c in new string[]
            {
                Path.Combine(baseDir, "exfat", "macexfat.py"),
                Path.Combine(baseDir, "..", "Resources", "exfat", "macexfat.py"),
                Path.Combine(baseDir, "..", "..", "Resources", "exfat", "macexfat.py"),
            })
            {
                try { if (File.Exists(c)) return Path.GetFullPath(c); }
                catch { }
            }
            throw new FileNotFoundException("The bundled exFAT reader (exfat/macexfat.py) is missing.");
        }
    }
}
