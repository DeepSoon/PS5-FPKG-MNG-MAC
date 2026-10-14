using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FpkgExfat
{
    // These checks are run on Windows after compilation. They do not require
    // OSFMount, a console, game content or admin rights. No real disk is mounted.
    internal static class SelfTests
    {
        private static readonly StringBuilder Report = new StringBuilder();

        internal static int Run()
        {
            string root = null;
            int code = 0;
            try
            {
                root = FileWorkspace.CreateOwned(Path.GetTempPath());
                Check(BuilderBridge.FormType != null, "Original GUI and builder API binding");
                PfsExtraction.Probe();
                Check(true, "Bundled FFPFSC / PFS reader starts with its portable runtime");
                TestMountIdentity();
                PackageSdkTests.Quick(root, Check);
                BackportUpdateTests.Quick(root, Check);
                TestImageHeader(root);
                TestCopyAndCommit(root);
                TestBuilderOptions(root);
                TestProcesses();
                Report.AppendLine("PASS: module checks completed. OSFMount and a full conversion are not included in these quick tests.");
            }
            catch (Exception error) { Report.AppendLine("FAIL: " + error); code = 1; }
            finally
            {
                if (root != null) FileWorkspace.DeleteOwned(root, delegate(string line) { Report.AppendLine(line); });
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat");
                try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "self-test.txt"), Report.ToString()); }
                catch { code = 1; }
            }
            return code;
        }

        private static void Check(bool condition, string caption)
        { if (!condition) throw new Exception(caption); Report.AppendLine("PASS " + caption); }

        private static void Throws<T>(Action action, string caption) where T : Exception
        {
            try { action(); }
            catch (T) { Report.AppendLine("PASS " + caption); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + caption);
        }

        private static void TestMountIdentity()
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification", "osfmount-regressions.tsv");
            int number = 0;
            foreach (string line in File.ReadAllLines(fixture, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] columns = line.Split('\t');
                if (columns.Length != 5) throw new InvalidDataException("Invalid OSFMount regression fixture.");
                ExfatMount.MountEvidence actual = ExfatMount.MappingEvidence(columns[4].Replace("<NL>", "\r\n"), columns[2], columns[3], columns[1] == "creation");
                Check(actual.ToString() == columns[0], "OSFMount output regression " + (++number));
            }
            Check(number >= 15, "OSFMount regression fixtures present");
            string device = @"\Device\OSFMount0";
            Check(ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, true, device, device, 123, 123),
                "Accept confirmed creation when listing syntax differs and device/volume are unchanged");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, false, device, device, 123, 123),
                "Never borrow an unconfirmed mount");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, true, device, @"\Device\OSFMount1", 123, 123),
                "Reject a reused drive letter with a different device");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, true, device, device, 123, 456),
                "Reject a changed volume on the same device");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Different, true, device, device, 123, 123),
                "Conflicting image path overrides previous creation receipt");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, true, null, null, 123, 123),
                "Missing device identity cannot authorize fallback");
            Check(!ExfatMount.CanUseMount(ExfatMount.MountEvidence.Unknown, true, device, device, null, null),
                "Missing volume identity cannot authorize fallback");
            Check(ExfatMount.CanUseMount(ExfatMount.MountEvidence.Matches, false, null, null, null, null),
                "Exact queried image mapping is sufficient");
            string path = @"C:\Dump con spazi & simboli\game.exfat";
            string[] args = ExfatMount.MountArguments(path, "Z:");
            Check(Array.IndexOf(args, "ro,logical,hd") >= 0 && Array.IndexOf(args, "rw") < 0 && Array.IndexOf(args, "format") < 0,
                "Read-only logical mount without formatting");
        }

        private static void TestImageHeader(string root)
        {
            // Geometry fixture only, not a mountable exFAT filesystem.
            string image = Path.Combine(root, "header.exfat");
            byte[] bytes = new byte[128 * 512];
            Encoding.ASCII.GetBytes("EXFAT   ").CopyTo(bytes, 3);
            bytes[0] = 0xeb; bytes[1] = 0x76; bytes[2] = 0x90;
            BitConverter.GetBytes((ulong)128).CopyTo(bytes, 72);
            BitConverter.GetBytes((uint)24).CopyTo(bytes, 80);
            BitConverter.GetBytes((uint)1).CopyTo(bytes, 84);
            BitConverter.GetBytes((uint)25).CopyTo(bytes, 88);
            BitConverter.GetBytes((uint)103).CopyTo(bytes, 92);
            BitConverter.GetBytes((uint)2).CopyTo(bytes, 96);
            BitConverter.GetBytes((ushort)0x100).CopyTo(bytes, 104);
            bytes[108] = 9; bytes[109] = 0; bytes[110] = 1; bytes[510] = 0x55; bytes[511] = 0xaa;
            File.WriteAllBytes(image, bytes); ExfatMount.ValidateImage(image);
            Check(true, "Accept bounded raw exFAT geometry");
            using (FileStream stream = new FileStream(image, FileMode.Open, FileAccess.Write)) stream.SetLength(512);
            Throws<InvalidDataException>(delegate { ExfatMount.ValidateImage(image); }, "Reject truncated image");
            bytes[108] = 31; File.WriteAllBytes(image, bytes);
            Throws<InvalidDataException>(delegate { ExfatMount.ValidateImage(image); }, "Reject invalid sector shift");
            bytes[108] = 9; bytes[3] = (byte)'X'; File.WriteAllBytes(image, bytes);
            Throws<InvalidDataException>(delegate { ExfatMount.ValidateImage(image); }, "Reject unrelated format");
        }

        private static void TestCopyAndCommit(string root)
        {
            string input = Path.Combine(root, "input");
            Directory.CreateDirectory(Path.Combine(input, "empty"));
            Directory.CreateDirectory(Path.Combine(input, "nested"));
            byte[] payload = new byte[2500123];
            new Random(913).NextBytes(payload);
            string sourceFile = Path.Combine(input, "nested", "dati & prova.bin");
            File.WriteAllBytes(sourceFile, payload); File.WriteAllText(Path.Combine(input, "empty.txt"), "");
            CopyPlan plan = FileWorkspace.Scan(input, CancellationToken.None);
            string destination = Path.Combine(root, "copied");
            FileWorkspace.Copy(plan, destination, true, CancellationToken.None, delegate { });
            Check(FileWorkspace.Equal(File.ReadAllBytes(Path.Combine(destination, "nested", "dati & prova.bin")), payload), "Copy preserves multi-buffer payload");
            Check(Directory.Exists(Path.Combine(destination, "empty")), "Preserve empty directories");
            Check(new FileInfo(Path.Combine(destination, "empty.txt")).Length == 0, "Preserve zero-byte files");
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                Throws<OperationCanceledException>(delegate {
                    FileWorkspace.Copy(plan, Path.Combine(root, "cancelled"), true, cancel.Token,
                        delegate { cancel.Cancel(); });
                }, "Cancellation during copy verification");
            }
            string staged = Path.Combine(root, "staged.pkg"), target = Path.Combine(root, "existing.pkg");
            File.WriteAllText(staged, "new"); File.WriteAllText(target, "original");
            Throws<IOException>(delegate { FileWorkspace.Commit(staged, target); }, "Never overwrite an existing package");
            Check(File.ReadAllText(target) == "original" && File.ReadAllText(staged) == "new", "Both files survive output collision");
            Check(!FileWorkspace.Inside(Path.Combine(root, "input-other"), input), "Directory containment respects separators");
        }

        private static void TestBuilderOptions(string root)
        {
            string input = Path.Combine(root, "metadata"); Directory.CreateDirectory(Path.Combine(input, "sce_sys"));
            string param = Path.Combine(input, "sce_sys", "param.json");
            File.WriteAllText(param, "{\"contentId\":\"UP0000-PPSA99999_00-EXFATTEST0000001\",\"contentVersion\":\"01.00\",\"localizedParameters\":{\"defaultLanguage\":\"en-US\",\"en-US\":{\"titleName\":\"exFAT integration test\"}}}");
            using (Form form = (Form)Activator.CreateInstance(BuilderBridge.FormType, true))
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                TextBox previousSource = (TextBox)BuilderBridge.Get(form, "sourceText"); previousSource.Text = "Previous source";
                TextBox previousId = (TextBox)BuilderBridge.Get(form, "contentIdText"); previousId.Text = "UP0000-PPSA99999_00-PREVIOUSTEST0001";
                object options = BuilderBridge.PrepareOptions(form, input, Path.Combine(root, "build-output"), Path.Combine(root, "build-temp"), cancel.Token);
                Check((string)BuilderBridge.Get(options, "ContentId") == "UP0000-PPSA99999_00-EXFATTEST0000001", "Options use image metadata");
                Check(previousSource.Text == "Previous source", "Restore existing GUI source after preparing conversion");
                cancel.Cancel(); Check(((CancellationToken)BuilderBridge.Get(options, "CancellationToken")).IsCancellationRequested, "Builder receives cancellable token");
                File.WriteAllText(param, "{broken-json");
                Throws<InvalidDataException>(delegate { BuilderBridge.PrepareOptions(form, input, Path.Combine(root, "build-output"), Path.Combine(root, "build-temp"), CancellationToken.None); }, "Corrupt metadata cannot reuse previous game's ID");
            }
        }

        private static void TestProcesses()
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FPKG-GUI-exFAT.exe");
            string unusual = "space & percent% \"quote\" end\\";
            ToolResult result = ProcessRunner.Run(executable, new string[] { "--tool-probe", unusual }, 20000, CancellationToken.None, null);
            Check(result.ExitCode == 0 && result.Output.TrimEnd('\r', '\n') == unusual, "Windows process argument roundtrip");
            result = ProcessRunner.Run(executable, new string[] { "--tool-probe", "fail" }, 20000, CancellationToken.None, null);
            Check(result.ExitCode == 9, "Preserve external tool failure status");
            Throws<TimeoutException>(delegate { ProcessRunner.Run(executable, new string[] { "--tool-probe", "wait" }, 600, CancellationToken.None, null); }, "Timeout terminates only the spawned process");
        }
    }
}
