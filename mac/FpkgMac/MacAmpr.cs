using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    // AMPR LZ4 packs via the bundled python driver (mac/ampr/macampr.py),
    // which wraps drakmor/ampr_emu reference tools (GPLv3, vendored).
    // Input: game folder or raw .exfat image. Output: pack directory
    // (ampr_assets.index + .pak volumes). The image/folder is only read.
    internal static class MacAmpr
    {
        internal static string Pack(string source, string output, int level, int workers,
            CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            if (level < 0 || level > 12) throw new ArgumentException("Level must be between 0 and 12.");
            source = Path.GetFullPath(source.Trim());
            output = Path.GetFullPath(output.Trim());
            bool isDir = Directory.Exists(source);
            if (!isDir && !File.Exists(source)) throw new FileNotFoundException("Source not found.", source);
            if (!isDir)
            {
                ImageFormat detected = ImageFormats.Detect(source);
                if (detected != ImageFormat.Exfat)
                    throw new NotSupportedException("AMPR packing accepts a game folder or a raw exFAT image (" +
                        ImageFormats.Name(detected) + " is not supported here).");
            }
            else if (!File.Exists(Path.Combine(source, "sce_sys", "param.json")))
            {
                log("Note: sce_sys/param.json not found; packing files as-is.");
            }
            if (File.Exists(output) || Directory.Exists(output))
                throw new IOException("The output already exists. Choose another name.");
            string script = FindScript();
            string python = FindPython();
            string found = null;
            ToolResult r = ProcessRunner.Run(python,
                new string[] { script, "pack", source, output, "--level", level.ToString(CultureInfo.InvariantCulture),
                    "--workers", Math.Max(1, workers).ToString(CultureInfo.InvariantCulture) },
                0, token, delegate(string line) { HandleLine(line, report, log, ref found); }, true);
            if (r.ExitCode != 0) throw new InvalidDataException("AMPR packing failed.\r\n" + r.Output);
            if (string.IsNullOrEmpty(found)) throw new InvalidDataException("The AMPR driver did not return a pack folder.");
            string root = Path.GetFullPath(found);
            if (!root.Equals(output, StringComparison.OrdinalIgnoreCase) && !FileWorkspace.Inside(root, output))
                throw new InvalidDataException("The AMPR driver returned an unexpected folder.");
            if (!File.Exists(Path.Combine(root, "ampr_assets.index")))
                throw new InvalidDataException("ampr_assets.index is missing from the pack output.");
            return root;
        }

        internal static string Unpack(string packDir, string output,
            CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            packDir = Path.GetFullPath(packDir.Trim());
            output = Path.GetFullPath(output.Trim());
            if (!Directory.Exists(packDir)) throw new DirectoryNotFoundException("Pack folder not found: " + packDir);
            if (File.Exists(output) || Directory.Exists(output))
                throw new IOException("The output already exists. Choose another name.");
            string script = FindScript();
            string python = FindPython();
            string found = null;
            ToolResult r = ProcessRunner.Run(python,
                new string[] { script, "unpack", packDir, output },
                0, token, delegate(string line) { HandleLine(line, report, log, ref found); }, true);
            if (r.ExitCode != 0) throw new InvalidDataException("AMPR extraction failed.\r\n" + r.Output);
            if (string.IsNullOrEmpty(found)) throw new InvalidDataException("The AMPR driver did not return an output folder.");
            return Path.GetFullPath(found);
        }

        private static void HandleLine(string line, Action<ConversionProgress> report, Action<string> log, ref string found)
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
        }

        private static string FindPython()
        {
            // ampr_pack.py needs tomllib (3.11+) and the vendored lz4 wheel
            // (cp312). Probe candidates with the bundled site-packages and
            // keep the first interpreter that actually imports lz4.block.
            if (cachedPython != null) return cachedPython;
            string sitePackages = null;
            try
            {
                string script = FindScript();
                sitePackages = Path.Combine(Path.GetDirectoryName(script), "site-packages");
            }
            catch { }
            foreach (string c in new string[] {
                "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3",
                "/opt/homebrew/bin/python3",
                "/usr/bin/python3",
                "python3" })
            {
                try
                {
                    if (c.Contains("/") && !File.Exists(c)) continue;
                    var env = new System.Collections.Generic.Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(sitePackages)) env["PYTHONPATH"] = sitePackages;
                    ToolResult r = ProcessRunner.RunWithEnvironment(c,
                        new string[] { "-c", "import lz4.block" }, 20000,
                        CancellationToken.None, null,
                        AppDomain.CurrentDomain.BaseDirectory, env);
                    if (r.ExitCode == 0) { cachedPython = c; return c; }
                }
                catch { }
            }
            return "python3";
        }
        private static string cachedPython;

        private static string FindScript()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string c in new string[]
            {
                Path.Combine(baseDir, "ampr", "macampr.py"),
                Path.Combine(baseDir, "..", "Resources", "ampr", "macampr.py"),
                Path.Combine(baseDir, "..", "..", "Resources", "ampr", "macampr.py"),
            })
            {
                try { if (File.Exists(c)) return Path.GetFullPath(c); }
                catch { }
            }
            throw new FileNotFoundException("The bundled AMPR driver (ampr/macampr.py) is missing.");
        }
    }
}
