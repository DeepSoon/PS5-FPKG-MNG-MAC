using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class PkgRebuildRequest
    {
        internal string Package;
        internal string Output;
        internal string Temporary;
        internal string Passcode;
        internal bool Verify;
        internal ulong TargetSdk;

        internal void Validate()
        {
            Package = Path.GetFullPath((Package ?? "").Trim());
            Output = Path.GetFullPath((Output ?? "").Trim());
            Temporary = Path.GetFullPath((Temporary ?? "").Trim());
            Passcode = string.IsNullOrEmpty(Passcode) ? new string('0', 32) : Passcode;
            if (!File.Exists(Package)) throw new FileNotFoundException("Source PKG not found.", Package);
            if (!Path.GetExtension(Package).Equals(".pkg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Select a .pkg source file.");
            if (Passcode.Length != 32) throw new ArgumentException("PKG passcode must contain exactly 32 characters.");
            if (!Path.GetExtension(Output).Equals(".pkg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The output filename must have a .pkg extension.");
            if (Package.Equals(Output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and output must be different files.");
            if (File.Exists(Output) || Directory.Exists(Output)) throw new IOException("The output already exists. Choose another output folder or remove the old rebuilt PKG.");
            if (File.Exists(Temporary)) throw new IOException("Select a temporary folder, not a file.");
        }
    }

    internal sealed class PkgRebuildResult
    {
        internal string Output;
        internal string Warning;
    }

    internal static class PkgRebuildJob
    {
        internal static PkgRebuildResult Run(PkgRebuildRequest request, CancellationToken token,
            Func<string, string, string, CancellationToken, object> prepareOptions,
            Action<ConversionProgress> report, Action<string> log)
        {
            string work = null, outputWork = null;
            try
            {
                request.Validate();
                token.ThrowIfCancellationRequested();
                string outputParent = Path.GetDirectoryName(request.Output);
                work = FileWorkspace.CreateOwned(request.Temporary);
                outputWork = FileWorkspace.CreateOwned(outputParent);
                string dump = Path.Combine(work, "dump");
                string scratch = Path.Combine(work, "builder-temp");
                Directory.CreateDirectory(scratch);

                report(new ConversionProgress("Reading source PKG", "Auto-detecting package type and metadata", 0, 0));
                string packageProfile = DetectPackageProfile(request.Package, log);

                report(new ConversionProgress("Extracting PKG", "Reading available inner PFS and CNT metadata", 0, new FileInfo(request.Package).Length));
                string source;
                try
                {
                    source = ExtractSource(request.Package, dump, request.Passcode, request.Verify, token, log);
                }
                catch (Exception extractError)
                {
                    string lower = (packageProfile ?? "").ToLowerInvariant();
                    if (lower.Contains("official") || lower.Contains("retail") || lower.Contains("encrypted") || lower.Contains("protected"))
                        throw new InvalidDataException("The PKG was detected as " + packageProfile + ", but its source content is not readable with the supplied package credentials. The tool accepts any PKG as input, but a rebuild requires readable/extractable package content.", extractError);
                    throw new InvalidDataException("The PKG was detected as " + packageProfile + ", but the source structure could not be extracted. Verify the package/passcode and try again.", extractError);
                }
                token.ThrowIfCancellationRequested();

                string paramPath = Path.Combine(source, "sce_sys", "param.json");
                if (!File.Exists(paramPath)) throw new InvalidDataException("PKG extraction completed, but sce_sys/param.json was not reconstructed.");
                PackageParam param = new PackageParam(File.ReadAllBytes(paramPath));
                param.Retarget(SdkVersion.PackageVersion(request.TargetSdk));
                File.WriteAllBytes(paramPath, param.Bytes());

                report(new ConversionProgress("Reading metadata", "Preparing a new package from the extracted source", 0, 0));
                object options = prepareOptions(source, outputWork, scratch, token);
                BuilderBridge.Set(options, "SdkVersionOverride", request.TargetSdk);
                log("PKG → PKG rebuild. Source: " + request.Package);
                log("Executable SDK: " + SdkVersion.Hex(request.TargetSdk) + "; package declaration: " + SdkVersion.Hex(SdkVersion.PackageVersion(request.TargetSdk)));
                log("Original PKG remains unchanged; output is written to a new file.");

                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Building PKG", "Repacking extracted source with LibProsperoPkg", 0, 0));
                string package = Path.GetFullPath(BuilderBridge.Build(options, log));
                if (!FileWorkspace.Inside(package, outputWork) || !File.Exists(package) || !Path.GetExtension(package).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The builder did not return a valid PKG in the expected folder.");
                FileWorkspace.NoReparse(package);

                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Verifying PKG", "Checking rebuilt container and SHA-256", 0, 0));
                BuilderBridge.Verify(package, options, token, log);
                token.ThrowIfCancellationRequested();
                FileWorkspace.Commit(package, request.Output);
                log("Rebuilt PKG created: " + request.Output);
                return new PkgRebuildResult { Output = request.Output, Warning = null };
            }
            finally
            {
                report(new ConversionProgress("Cleaning temporary files", "Removing extracted PKG workspace", 0, 0));
                FileWorkspace.DeleteOwned(work, log);
                FileWorkspace.DeleteOwned(outputWork, log);
            }
        }

        private static string DetectPackageProfile(string package, Action<string> log)
        {
            string label = "PKG";
            try
            {
                Type reader = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoPkgReader", true);
                MethodInfo detect = reader.GetMethod("DetectType", BuilderBridge.Members, null, new Type[] { typeof(string) }, null);
                if (detect != null)
                {
                    object type = BuilderBridge.Invoke(detect, null, package);
                    string detected = Convert.ToString(type, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(detected)) label = detected.Trim();
                }
            }
            catch (Exception error)
            {
                if (log != null) log("PKG type detection warning: " + error.Message);
            }
            if (log != null) log("Detected PKG profile: " + label);
            return label;
        }

        internal static string ExtractSource(string package, string dump, string passcode, bool verify, CancellationToken token, Action<string> log)
        {
            Directory.CreateDirectory(dump);
            string cnt = Path.Combine(Path.GetDirectoryName(dump), "cnt");
            Directory.CreateDirectory(cnt);

            InvokeArchive("ExtractInnerFiles", package, dump, passcode, verify, log);
            token.ThrowIfCancellationRequested();
            try { InvokeArchive("ExtractCntEntries", package, cnt, passcode, true, log); }
            catch (Exception error) { log("CNT extraction warning: " + error.Message); }
            token.ThrowIfCancellationRequested();

            string payloadRoot = DeterminePayloadRoot(dump);
            MergeSceSys(cnt, payloadRoot, log);
            string root = ResolveGameRoot(payloadRoot);
            log("PKG source reconstructed: " + root);
            return root;
        }

        private static void InvokeArchive(string methodName, string package, string output, string passcode, bool verify, Action<string> log)
        {
            Type archive = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoPackageArchive", true);
            MethodInfo method = archive.GetMethod(methodName, BuilderBridge.Members, null,
                new Type[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
            if (method == null) throw new MissingMethodException(archive.FullName, methodName);

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string name = (parameters[i].Name ?? "").ToLowerInvariant();
                Type type = parameters[i].ParameterType;
                if (type == typeof(bool)) args[i] = name.Contains("verify") ? verify : true;
                else if (name.Contains("pass")) args[i] = passcode;
                else if (name.Contains("output") || name.Contains("destination") || name.Contains("directory")) args[i] = output;
                else if (name.Contains("package") || name.Contains("pkg") || name.Contains("path")) args[i] = package;
                else
                {
                    // Current LibProsperoPkg signatures are packagePath, outputDirectory, passcode, bool.
                    if (i == 0) args[i] = package;
                    else if (i == 1) args[i] = output;
                    else if (i == 2) args[i] = passcode;
                    else args[i] = true;
                }
            }
            log(methodName + ": " + Path.GetFileName(package));
            BuilderBridge.Invoke(method, null, args);
        }


        private static string DeterminePayloadRoot(string dump)
        {
            if (File.Exists(Path.Combine(dump, "sce_sys", "param.json"))) return dump;
            string[] files = Directory.GetFiles(dump, "*", SearchOption.TopDirectoryOnly);
            List<string> directories = new List<string>();
            foreach (string directory in Directory.GetDirectories(dump, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(directory);
                if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)) continue;
                directories.Add(directory);
            }
            if (files.Length == 0 && directories.Count == 1) return directories[0];
            return dump;
        }

        private static string ResolveGameRoot(string dump)
        {
            try { return FileWorkspace.GameRoot(dump); }
            catch
            {
                foreach (string param in Directory.EnumerateFiles(dump, "param.json", SearchOption.AllDirectories))
                {
                    DirectoryInfo sce = Directory.GetParent(param);
                    if (sce != null && sce.Name.Equals("sce_sys", StringComparison.OrdinalIgnoreCase) && sce.Parent != null)
                        return sce.Parent.FullName;
                }
                throw;
            }
        }

        private static void MergeSceSys(string extractedCnt, string dump, Action<string> log)
        {
            if (!Directory.Exists(extractedCnt)) return;
            string target = Path.Combine(dump, "sce_sys");
            Directory.CreateDirectory(target);
            int copied = 0;

            foreach (string directory in Directory.EnumerateDirectories(extractedCnt, "sce_sys", SearchOption.AllDirectories))
                copied += CopyTree(directory, target);

            if (!File.Exists(Path.Combine(target, "param.json")))
            {
                foreach (string param in Directory.EnumerateFiles(extractedCnt, "param.json", SearchOption.AllDirectories))
                {
                    File.Copy(param, Path.Combine(target, "param.json"), true);
                    copied++;
                    break;
                }
            }

            foreach (string pattern in new string[] { "icon0.*", "pic0.*", "pic1.*", "pic2.*" })
                foreach (string file in Directory.EnumerateFiles(extractedCnt, pattern, SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(target, Path.GetFileName(file));
                    if (!File.Exists(destination)) { File.Copy(file, destination, true); copied++; }
                }

            log("CNT metadata merged into sce_sys: " + copied + " file(s).");
        }

        private static int CopyTree(string source, string destination)
        {
            int copied = 0;
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                copied++;
            }
            return copied;
        }
    }
}
