using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    internal static class MacProgram
    {
        private static int Main(string[] args)
        {
            try
            {
                BuilderBridge.Initialize();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("INIT-FAIL: " + e.Message);
                return 1;
            }
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            {
                PrintHelp();
                return 0;
            }
            try
            {
                string cmd = (args[0] ?? "").ToLowerInvariant();
                switch (cmd)
                {
                    case "version": return CmdVersion();
                    case "inspect-folder": return CmdInspectFolder(args);
                    case "inspect-pkg": return CmdInspectPkg(args);
                    case "list-pkg": return CmdListPkg(args);
                    case "extract-file": return CmdExtractFile(args);
                    case "extract-all": return CmdExtractAll(args);
                    case "extract-pkg-folder": return CmdExtractPkgFolder(args);
                    case "build-folder": return CmdBuild(args, false);
                    case "build-exfat": return CmdBuild(args, true);
                    case "detect-image": return CmdDetectImage(args);
                    case "trophy-detect": return CmdTrophyDetect(args);
                    case "trophy-list": return CmdTrophyList(args);
                    case "trophy-unlock": return CmdTrophyUnlock(args);
                    case "trophy-unlock-all": return CmdTrophyUnlockAll(args);
                    default:
                        Console.Error.WriteLine("Unknown command: " + args[0]);
                        PrintHelp();
                        return 2;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERROR: " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("FpkgMac 1.0.0-NG (macOS backend, portable do PS5-FPKG-Builder r30) · Made by Leite");
            Console.WriteLine("Uso:");
            Console.WriteLine("  FpkgMac version");
            Console.WriteLine("  FpkgMac inspect-folder <pasta-do-jogo>");
            Console.WriteLine("  FpkgMac inspect-pkg <arquivo.pkg> [passcode32]");
            Console.WriteLine("  FpkgMac list-pkg <arquivo.pkg> [passcode32] [--limit N]");
            Console.WriteLine("  FpkgMac extract-file <arquivo.pkg> <caminho-no-pkg> <destino> [passcode32]");
            Console.WriteLine("  FpkgMac extract-all <arquivo.pkg> <pasta-destino> [passcode32]");
            Console.WriteLine("  FpkgMac extract-pkg-folder <arquivo.pkg> <pasta-destino> <pasta-temp> [passcode32]");
            Console.WriteLine("  FpkgMac build-folder <pasta-do-jogo> <saida.pkg> <pasta-temp> [passcode32] [--sdk 9.00] [--mode Application]");
            Console.WriteLine("  FpkgMac build-exfat <imagem.exfat> <saida.pkg> <pasta-temp> [passcode32] [--sdk 9.00] [--mode Application]");
            Console.WriteLine("  FpkgMac detect-image <imagem>");
            Console.WriteLine("  FpkgMac trophy-detect <ps5-ip>");
            Console.WriteLine("  FpkgMac trophy-list <ps5-ip> <PPSAxxxxx>");
            Console.WriteLine("  FpkgMac trophy-unlock <ps5-ip> <PPSAxxxxx> <trophy-id>");
            Console.WriteLine("  FpkgMac trophy-unlock-all <ps5-ip> <PPSAxxxxx>");
            Console.WriteLine("");
            Console.WriteLine("NOTA Mac: criacao/rebuild de PKG exige Windows + prospero-pub-cmd.exe (Sony SDK).");
            Console.WriteLine("No Mac funcionam: inspecao, listagem, extracao e Trophy Unlocker via FTP.");
        }

        private static int CmdVersion()
        {
            Console.WriteLine("FpkgMac 1.0.0-NG macOS-arm64 r30-portable · Made by Leite");
            return 0;
        }

        private static string PasscodeOrDefault(string[] args, int index)
        {
            if (args.Length > index && !string.IsNullOrEmpty(args[index]) && args[index].Length == 32)
                return args[index];
            return new string('0', 32);
        }

        private static int CmdInspectFolder(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("Uso: inspect-folder <pasta-do-jogo>");
            SourceInfo info = SourceInspector.InspectFolder(args[1]);
            PrintInfo(info);
            return 0;
        }

        private static int CmdInspectPkg(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("Uso: inspect-pkg <arquivo.pkg> [passcode32]");
            string pass = args.Length >= 3 ? args[2] : new string('0', 32);
            SourceInfo info = SourceInspector.InspectPkg(args[1], pass);
            PrintInfo(info);
            return 0;
        }

        private static void PrintInfo(SourceInfo info)
        {
            Write("SourcePath", info.SourcePath);
            Write("SourceType", info.SourceType);
            Write("Title", info.Title);
            Write("ContentId", info.ContentId);
            Write("TitleId", info.TitleId);
            Write("Region", info.Region);
            Write("Version", info.Version);
            Write("PackageType", info.PackageType);
            Write("Sdk", info.Sdk);
            Write("RequiredSystem", info.RequiredSystem);
            Write("CoverPath", info.CoverPath);
            Write("BackgroundPath", info.BackgroundPath);
            Console.WriteLine("Size=" + info.Size);
            Console.WriteLine("Entries=" + info.Entries);
        }

        private static void Write(string k, string v)
        {
            Console.WriteLine(k + "=" + (v ?? ""));
        }

        private static int CmdListPkg(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("Uso: list-pkg <arquivo.pkg> [passcode32] [--limit N]");
            string pkg = args[1];
            string pass = new string('0', 32);
            int limit = 100000;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--limit" && i + 1 < args.Length) { int.TryParse(args[++i], out limit); }
                else if (args[i].Length == 32 && pass == new string('0', 32)) pass = args[i];
            }
            using (PkgFileSession session = PkgFileSession.Open(pkg, pass, CancellationToken.None, null))
            {
                Console.WriteLine("# count=" + session.Files.Count + " content=" + session.Snapshot.ContentId + " version=" + session.Snapshot.ContentVersion);
                int n = 0;
                foreach (PkgFileItem f in session.Files)
                {
                    if (n++ >= limit) break;
                    Console.WriteLine(f.Path + "\t" + f.Size + "\t" + f.State);
                }
            }
            return 0;
        }

        private static void Log(string message)
        {
            // Flush every line: stdout is block-buffered when piped, and the
            // Mac GUI streams these lines for live progress. Without this,
            // everything arrives as one burst at process exit.
            Console.WriteLine(message);
            try { Console.Out.Flush(); } catch { }
        }

        private static void ReportProgress(ConversionProgress p)
        {
            Log("PROGRESS\t" + p.Stage + "\t" + p.Done + "\t" + p.Total + "\t" + (p.Detail ?? ""));
        }

        private static int CmdExtractFile(string[] args)
        {
            if (args.Length < 4) throw new ArgumentException("Uso: extract-file <arquivo.pkg> <caminho-no-pkg> <destino> [passcode32]");
            string pkg = args[1], inner = args[2], dest = args[3];
            string pass = args.Length >= 5 ? args[4] : new string('0', 32);
            using (PkgFileSession session = PkgFileSession.Open(pkg, pass, CancellationToken.None, null))
            {
                PkgFileItem item = session.Find(inner);
                if (item == null) throw new FileNotFoundException("Arquivo nao encontrado no PKG: " + inner);
                long size = Math.Max(0, item.CurrentSize);
                ReportProgress(new ConversionProgress("Extracting file", inner, 0, Math.Max(1, size)));
                session.Export(item, dest, true, CancellationToken.None);
                ReportProgress(new ConversionProgress("Extracting file", inner, Math.Max(1, size), Math.Max(1, size)));
            }
            Console.WriteLine("OK " + dest);
            return 0;
        }

        private static int CmdExtractAll(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("Uso: extract-all <arquivo.pkg> <pasta-destino> [passcode32]");
            string pkg = args[1], dest = Path.GetFullPath(args[2]);
            string pass = args.Length >= 4 ? args[3] : new string('0', 32);
            if (File.Exists(dest) || Directory.Exists(dest))
                throw new IOException("Destino ja existe: " + dest);
            using (PkgFileSession session = PkgFileSession.Open(pkg, pass, CancellationToken.None, Console.WriteLine))
            {
                Directory.CreateDirectory(dest);
                session.Materialize(dest, false, CancellationToken.None, ReportProgress, Log);
            }
            Console.WriteLine("OK " + dest);
            return 0;
        }

        private static int CmdExtractPkgFolder(string[] args)
        {
            if (args.Length < 4) throw new ArgumentException("Uso: extract-pkg-folder <arquivo.pkg> <pasta-destino> <pasta-temp> [passcode32]");
            var req = new PkgExtractionRequest
            {
                Package = args[1],
                Destination = args[2],
                Temporary = args[3],
                Passcode = args.Length >= 5 ? args[4] : new string('0', 32),
                Verify = false,
            };
            string outp = PkgExtractionJob.Run(req, CancellationToken.None,
                ReportProgress,
                Log);
            Console.WriteLine("OK " + outp);
            return 0;
        }

        private static int CmdBuild(string[] args, bool isImage)
        {
            if (args.Length < 4) throw new ArgumentException(isImage
                ? "Uso: build-exfat <imagem.exfat> <saida.pkg> <pasta-temp> [passcode32] [--sdk 9.00] [--mode Application]"
                : "Uso: build-folder <pasta-do-jogo> <saida.pkg> <pasta-temp> [passcode32] [--sdk 9.00] [--mode Application]");
            var req = new MacBuildRequest
            {
                Source = args[1],
                Output = args[2],
                Temporary = args[3],
                Passcode = new string('0', 32),
                Sdk = "9.00",
                Mode = "Application",
            };
            for (int i = 4; i < args.Length; i++)
            {
                if (args[i] == "--sdk" && i + 1 < args.Length) req.Sdk = args[++i];
                else if (args[i] == "--mode" && i + 1 < args.Length) req.Mode = args[++i];
                else if (args[i].Length == 32 && req.Passcode == new string('0', 32)) req.Passcode = args[i];
            }
            req.Validate(isImage);
            string outp = MacBuilder.Build(req, System.Threading.CancellationToken.None,
                ReportProgress,
                Log);
            Console.WriteLine("OK " + outp);
            return 0;
        }

        private static int CmdDetectImage(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("Uso: detect-image <imagem>");
            ImageFormat f = ImageFormats.Detect(args[1]);
            Console.WriteLine("FORMAT=" + f);
            Console.WriteLine("NAME=" + ImageFormats.Name(f));
            return 0;
        }

        private static int CmdTrophyDetect(string[] args)        {
            if (args.Length < 2) throw new ArgumentException("Uso: trophy-detect <ps5-ip>");
            var eng = new TrophyUnlockerEngine();
            string ppsa = eng.DetectActiveGame(args[1].Trim(), Console.WriteLine);
            Console.WriteLine("PPSA=" + ppsa);
            return 0;
        }

        private static int CmdTrophyList(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("Uso: trophy-list <ps5-ip> <PPSAxxxxx>");
            var eng = new TrophyUnlockerEngine();
            TrophyListResult list = eng.GetTrophyList(args[1].Trim(), args[2].Trim().ToUpperInvariant(), Console.WriteLine);
            PrintTrophies(list);
            return 0;
        }

        private static int CmdTrophyUnlock(string[] args)
        {
            if (args.Length < 4) throw new ArgumentException("Uso: trophy-unlock <ps5-ip> <PPSAxxxxx> <trophy-id>");
            var eng = new TrophyUnlockerEngine();
            string host = args[1].Trim(), ppsa = args[2].Trim().ToUpperInvariant();
            int id = int.Parse(args[3].Trim());
            TrophyListResult catalog = eng.GetTrophyList(host, ppsa, Console.WriteLine);
            TrophyListResult after = eng.UnlockSingle(host, catalog, id, Console.WriteLine);
            PrintTrophies(after);
            return 0;
        }

        private static int CmdTrophyUnlockAll(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("Uso: trophy-unlock-all <ps5-ip> <PPSAxxxxx>");
            var eng = new TrophyUnlockerEngine();
            string host = args[1].Trim(), ppsa = args[2].Trim().ToUpperInvariant();
            TrophyListResult catalog = eng.GetTrophyList(host, ppsa, Console.WriteLine);
            TrophyListResult after = eng.UnlockAll(host, catalog, Console.WriteLine);
            PrintTrophies(after);
            return 0;
        }

        private static void PrintTrophies(TrophyListResult list)
        {
            Console.WriteLine("PPSA=" + list.Ppsa + " TOTAL=" + list.Total + " UNLOCKED=" + list.UnlockedCount);
            foreach (TrophyItem t in list.Trophies)
            {
                string line = t.Id + "\t" + (t.Unlocked ? "unlocked" : "locked") + "\t" + t.GradeName + "\t" + t.Name;
                Console.WriteLine(line);
            }
        }
    }
}
