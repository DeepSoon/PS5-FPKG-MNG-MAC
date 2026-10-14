using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    internal static class UfsExtraction
    {
        private static string Tool { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ufs2", "UFS2Tool.exe"); } }

        internal static void ValidateBackend()
        {
            if (!File.Exists(Tool) || !File.Exists(Path.Combine(Path.GetDirectoryName(Tool), "UFS2Tool.dll")))
                throw new FileNotFoundException("The bundled UFS2/FFPKG reader is missing. Extract the complete release ZIP, including tools/ufs2.");
        }

        private static ToolResult Run(string[] args, int timeout, CancellationToken token, Action<string> line)
        {
            ValidateBackend();
            ProcessStartInfo info = new ProcessStartInfo(Tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(Tool)
            };
            // UFS2Tool is net8; allow machines that only have the newer .NET desktop/runtime installed.
            info.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "Major";
            info.Arguments = ProcessRunner.Arguments(args);
            using (Process process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null && line != null) line(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null && line != null) line(e.Data); };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                DateTime started = DateTime.UtcNow;
                while (!process.WaitForExit(200))
                {
                    if (token.IsCancellationRequested) { try { process.Kill(); } catch { } token.ThrowIfCancellationRequested(); }
                    if (timeout > 0 && (DateTime.UtcNow - started).TotalMilliseconds > timeout)
                    { try { process.Kill(); } catch { } throw new TimeoutException("UFS2Tool timed out."); }
                }
                process.WaitForExit();
                return new ToolResult { ExitCode = process.ExitCode, Output = "" };
            }
        }

        internal static void Probe()
        {
            ToolResult result = Run(new string[] { "info", "--help" }, 15000, CancellationToken.None, null);
            // Older builds return non-zero for help; presence of the executable is the main probe.
            if (!File.Exists(Tool)) throw new FileNotFoundException("UFS2Tool is missing.");
        }

        internal static string Extract(string image, string work, CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            ValidateBackend();
            string destination = Path.Combine(work, "ufs2-extract");
            if (Directory.Exists(destination)) throw new IOException("The UFS2 extraction folder already exists.");
            Directory.CreateDirectory(destination);
            report(new ConversionProgress("Reading FFPKG / UFS2", "Extracting the filesystem", 0, 0));
            int exit = 0;
            ToolResult result = Run(new string[] { "extract", image, destination }, 0, token, delegate(string value) { log("UFS2Tool: " + value); });
            exit = result.ExitCode;
            if (exit != 0) throw new InvalidDataException("FFPKG / UFS2 extraction failed (UFS2Tool exit " + exit + ").");
            string root = FileWorkspace.GameRoot(destination);
            if (!File.Exists(Path.Combine(root, "sce_sys", "param.json")))
                throw new InvalidDataException("sce_sys/param.json is missing from the extracted FFPKG image.");
            return root;
        }

        internal static string ExtractPreview(string image, string destination, CancellationToken token, Action<string> log)
        {
            ValidateBackend();
            Directory.CreateDirectory(destination);
            // Extract only sce_sys for fast metadata/cover preview.
            ToolResult result = Run(new string[] { "extract", image, destination, "/sce_sys" }, 120000, token,
                delegate(string value) { if (log != null) log("UFS2 preview: " + value); });
            if (result.ExitCode != 0)
            {
                // Some images place the game under a single top-level folder. Discover param.json and retry its parent sce_sys path.
                string found = null;
                ToolResult find = Run(new string[] { "find", image, "param.json", "-type", "f" }, 30000, token,
                    delegate(string value) { if (value.IndexOf("sce_sys", StringComparison.OrdinalIgnoreCase) >= 0) found = value.Trim(); });
                if (find.ExitCode != 0 || string.IsNullOrEmpty(found)) throw new InvalidDataException("Could not locate sce_sys/param.json inside the FFPKG image.");
                int cut = found.Replace('\\','/').LastIndexOf("/sce_sys/param.json", StringComparison.OrdinalIgnoreCase);
                if (cut < 0) throw new InvalidDataException("Could not resolve the FFPKG game root.");
                string fsPath = found.Replace('\\','/').Substring(0, cut) + "/sce_sys";
                result = Run(new string[] { "extract", image, destination, fsPath }, 120000, token,
                    delegate(string value) { if (log != null) log("UFS2 preview: " + value); });
                if (result.ExitCode != 0) throw new InvalidDataException("Could not extract FFPKG preview metadata.");
            }
            string[] param = Directory.GetFiles(destination, "param.json", SearchOption.AllDirectories);
            if (param.Length != 1) throw new InvalidDataException("FFPKG preview did not yield a unique param.json.");
            return Directory.GetParent(Directory.GetParent(param[0]).FullName).FullName;
        }
    }
}
