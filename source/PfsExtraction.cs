using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;

namespace FpkgExfat
{
    internal static class PfsExtraction
    {
        private static string Backend { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "pfs"); } }
        private static string Python { get { return Path.Combine(Backend, "python", "python.exe"); } }
        private static string Worker { get { return Path.Combine(Backend, "pfs_worker.py"); } }

        internal static void ValidateBackend()
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new PlatformNotSupportedException("FFPFSC / PFS extraction requires 64-bit Windows.");
            if (!File.Exists(Python) || !File.Exists(Worker) || !File.Exists(Path.Combine(Backend, "mkpfs", "pfs.py")))
                throw new FileNotFoundException("The bundled FFPFSC / PFS reader is missing. Extract the complete release ZIP, including tools/pfs.");
        }

        internal static void Probe()
        {
            ValidateBackend();
            ToolResult result = ProcessRunner.Run(Python, new string[] { "-I", "-B", "-X", "utf8", Worker, "--probe" },
                60000, CancellationToken.None, null, true);
            if (result.ExitCode != 0 || !result.Output.Contains("FPKG_PFS_READY"))
                throw new InvalidDataException("The FFPFSC / PFS reader could not start: " + result.Output);
        }

        internal static string ExtractPreview(string image, string work, CancellationToken token, Action<string> log)
        {
            ValidateBackend();
            string destination = Path.Combine(work, "pfs-preview");
            ToolResult result = ProcessRunner.Run(Python, new string[] { "-I", "-B", "-X", "utf8", Worker, "preview", image, destination },
                120000, token, log, true);
            if (result.ExitCode != 0) throw new InvalidDataException("FFPFS / FFPFSC preview failed.\r\n" + result.Output);
            if (!File.Exists(Path.Combine(destination, "sce_sys", "param.json"))) throw new InvalidDataException("Preview did not return sce_sys/param.json.");
            return destination;
        }

        internal static string Extract(string image, string work, bool verify, CancellationToken token,
            Action<ConversionProgress> report, Action<string> log)
        {
            ValidateBackend();
            string destination = Path.Combine(work, "pfs-extract");
            string resultFile = Path.Combine(work, "pfs-result.xml");
            report(new ConversionProgress("Reading FFPFSC / PFS", "Checking image structure", 0, 0));
            log("FFPFSC / PFS reader: MkPFS 0.0.9 from exFAT Image Builder; source opened read-only.");
            // Hold a Windows read lease so another process cannot modify or replace
            // the input while the worker reads it. No shell or system Python is used.
            using (FileStream guard = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ToolResult result = ProcessRunner.Run(Python,
                    new string[] { "-I", "-B", "-X", "utf8", Worker, "extract", image, destination, resultFile, verify ? "1" : "0" },
                    0, token, delegate(string line)
                    {
                        if (line.StartsWith("FPKG_PROGRESS\t", StringComparison.Ordinal))
                        {
                            string[] parts = line.Split(new char[] { '\t' }, 5);
                            long done, total;
                            if (parts.Length == 5 && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out done) &&
                                long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out total))
                                report(new ConversionProgress(parts[1], parts[4], done, total));
                        }
                        else log(line);
                    }, true);
                if (result.ExitCode != 0)
                    throw new InvalidDataException("FFPFSC / PFS extraction failed.\r\n" + result.Output);
            }
            token.ThrowIfCancellationRequested();
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(resultFile,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
                document.Load(reader);
            XmlNode node = document.SelectSingleNode("/extraction/dump");
            if (node == null) throw new InvalidDataException("The FFPFSC / PFS reader did not return a game folder.");
            string dump = Path.GetFullPath(node.InnerText);
            if (!FileWorkspace.Inside(dump, destination) || !Directory.Exists(dump))
                throw new InvalidDataException("The FFPFSC / PFS reader returned an unexpected folder.");
            FileWorkspace.NoReparse(dump);
            if (!File.Exists(Path.Combine(dump, "sce_sys", "param.json")))
                throw new InvalidDataException("sce_sys/param.json is missing from the extracted game.");
            return dump;
        }
    }
}
