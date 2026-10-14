using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal static class PackageSdkTests
    {
        internal static void Quick(string root, Action<bool, string> check)
        {
            check(SdkVersion.Parse("9") == 0x0900000000000000UL, "SDK 9 packed BCD");
            check(SdkVersion.Parse("10.00") == 0x1000000000000000UL, "SDK 10 package declaration uses packed BCD");
            check(SdkVersion.Parse("11.40") == 0x1140000000000000UL, "Custom SDK minor version");
            check(SdkVersion.Parse("0x1001000001234567") == 0x1001000001234567UL, "Raw SDK retains all 64 bits");
            check(SdkVersion.Describe("0x1140000000000000").StartsWith("11.40 "), "Automatic SDK display decoding");
            foreach (string value in new string[] { "", "firmware 9", "10.1", "100.00", "-1", "0x1234", "10.00.01" })
            { bool failed = false; try { SdkVersion.Parse(value); } catch (ArgumentException) { failed = true; } check(failed, "Reject malformed SDK: " + value); }
            string json = "{\n  \"sdkVersion\" : \"0x0900000000000000\",\"requiredSystemSoftwareVersion\":\"0x0900000000000000\",\"title\":\"caffè \\\" sdkVersion\",\"nested\":{\"sdkVersion\":\"keep\"},\"large\":18446744073709551615}";
            byte[] before = Encoding.UTF8.GetBytes(json), after = FixedParam.Retarget(before, SdkVersion.Parse("10.01"));
            check(Encoding.UTF8.GetString(after) == json.Replace("0x0900000000000000", "0x1001000000000000"), "Only top-level fixed-width SDK values change; formatting and all other JSON bytes survive");
            check(after.Length == before.Length, "SDK edit keeps metadata length");
            bool missing = false; try { FixedParam.Retarget(Encoding.UTF8.GetBytes("{\"sdkVersion\":\"0x0900000000000000\"}"), SdkVersion.Parse("10")); } catch (ArgumentException) { missing = true; }
            check(missing, "Missing SDK field cannot trigger resizing");
            PackageParam param = new PackageParam(before); param.Retarget(SdkVersion.Parse("11.40"));
            check(new PackageParam(param.Bytes()).Text("sdkVersion") == "0x1140000000000000", "exFAT JSON SDK retargeting");
            PackageParam numericParam = new PackageParam(Encoding.UTF8.GetBytes("{\"contentVersion\":1.99,\"applicationCategoryType\":0,\"sdkVersion\":648518346341351424}"));
            check(numericParam.Text("contentVersion") == "1.99", "Numeric JSON metadata is exposed as text");
            check(numericParam.Text("applicationCategoryType") == "0", "Numeric JSON category does not abort preview");
            check(SdkVersion.Parse(numericParam.Text("sdkVersion")) == 648518346341351424UL, "Decimal packed SDK metadata is accepted");
            byte[] vector = Encoding.ASCII.GetBytes("123456789");
            check(BinaryData.Crc(vector, 0, vector.Length, true) == 0xe3069283U, "CRC-32C standard vector");
            check(BinaryData.Crc(vector, 0, vector.Length, false) == 0xcbf43926U, "ZIP CRC-32 standard vector");
            byte[] emptyHash = PackageSdk.Sha3(new byte[0], 0, 0);
            check(BitConverter.ToString(emptyHash).Replace("-", "").ToLowerInvariant() == "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a", "Actual library SHA3-256 API");
            byte[] wrapProbeHeader = new byte[4096];
            byte[] wrapProbe = PackageSdk.HeaderWrap(wrapProbeHeader);
            check(wrapProbe.Length == 384, "Actual publisher header-wrap API and span adapter");
            check(PackageSdk.VerifyHeaderWrap(wrapProbeHeader, wrapProbe), "Publisher header-wrap verifier accepts generated wrap");
            TestRecovery(root, check);
            using (Form form = (Form)Activator.CreateInstance(BuilderBridge.FormType, true))
            {
                ComboBox versions = (ComboBox)BuilderBridge.Get(form, "sdkVersionCombo");
                check(versions.Items.Count == 11 && versions.Items[0].ToString() == "1" && versions.Items[10].ToString() == "11", "GUI 0.5 firmware presets retained");
                for (int major = 1; major <= 11; major++)
                {
                    ulong executable = SdkVersion.ForBuild(major.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    ulong guiValue = (ulong)BuilderBridge.Invoke(BuilderBridge.FormType.GetMethod("EncodeSdkMajor", BuilderBridge.Members), null, major);
                    check(executable == guiValue && SdkVersion.PackageVersion(executable) == SdkVersion.Parse(major.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Complete executable SDK and package version match 0.5 preset " + major);
                    byte[] tuple = (byte[])PackageSdk.Call("LibProsperoPkg.Content.ProsperoFself", "BuildSdkVersionTuple", new Type[] { typeof(ulong) }, executable);
                    check(tuple.Length == 8 && unchecked((ulong)BinaryData.I64(tuple, 0, false)) == executable, "Executable SDK tuple retains all 8 bytes for preset " + major);
                }
                bool unknownBuild = false; try { SdkVersion.ForBuild("11.40"); } catch (ArgumentException) { unknownBuild = true; }
                check(unknownBuild, "Do not invent a full executable SDK build from an unknown firmware minor");
                ((ComboBox)BuilderBridge.Get(form, "languageCombo")).SelectedIndex = 1;
                check((bool)BuilderBridge.Get(form, "English"), "Original GUI uses English");
            }
        }

        private static void TestRecovery(string root, Action<bool, string> check)
        {
            string pkg = Path.Combine(root, "journal-test.pkg"), backup = Path.Combine(root, "journal-test.sdk-backup");
            byte[] prefix = new byte[65536], oldTail = new byte[12000]; new Random(211).NextBytes(prefix); new Random(212).NextBytes(oldTail);
            byte[] next = (byte[])oldTail.Clone(); next[42] ^= 19; next[9000] ^= 27;
            using (FileStream file = File.Create(pkg)) { file.Write(prefix, 0, prefix.Length); file.Write(oldTail, 0, oldTail.Length); }
            MetadataTransaction journal = MetadataTransaction.Create(prefix.Length + oldTail.Length, prefix.Length, PackageSdk.Sha256(prefix), oldTail, next);
            journal.Write(backup); check(new FileInfo(backup).Length < new FileInfo(pkg).Length, "Compact metadata backup instead of a full PKG copy");
            using (FileStream file = new FileStream(pkg, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { journal.WriteRanges(file, true, delegate(int n) { if (n == 1) throw new IOException("Injected interruption"); }); } catch (IOException) { }
                file.Flush(true);
            }
            MetadataTransaction.Restore(pkg, backup, delegate { });
            using (FileStream file = File.OpenRead(pkg))
            {
                check(FileWorkspace.Equal(PackageSdk.ReadRange(file, 0, prefix.Length), prefix), "Recovery never changes payload/header region");
                check(FileWorkspace.Equal(PackageSdk.ReadRange(file, prefix.Length, oldTail.Length), oldTail), "Recovery restores an interrupted partial write exactly");
            }
            using (FileStream file = new FileStream(pkg, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) journal.WriteRanges(file, true, null);
            MetadataTransaction.Restore(pkg, backup, delegate { });
            check(File.Exists(backup), "Recovery backup retained after undo");
            using (FileStream file = new FileStream(pkg, FileMode.Open, FileAccess.Write)) { file.Position = 12; file.WriteByte((byte)(prefix[12] ^ 1)); }
            bool refused = false; try { MetadataTransaction.Restore(pkg, backup, delegate { }); } catch (IOException) { refused = true; }
            check(refused, "Refuse a backup for a different package identity");
            byte[] corrupt = File.ReadAllBytes(backup); corrupt[40] ^= 1; string bad = backup + ".bad"; File.WriteAllBytes(bad, corrupt);
            refused = false; try { MetadataTransaction.Read(bad); } catch (InvalidDataException) { refused = true; }
            check(refused, "Refuse corrupted backup before writing");
        }

        // Explicit Windows test; builds only tiny synthetic non-playable packages.
        internal static int RunIntegration()
        {
            string root = null; StringBuilder report = new StringBuilder();
            Action<string> log = delegate(string value) { report.AppendLine(value); Console.WriteLine(value); };
            try
            {
                root = FileWorkspace.CreateOwned(Path.GetTempPath());
                foreach (string mode in new string[] { "PlaintextNoAuth", "Native" })
                {
                    string test = Path.Combine(root, mode), dump = Path.Combine(test, "dump"), output = Path.Combine(test, "output"), scratch = Path.Combine(test, "scratch");
                    Directory.CreateDirectory(Path.Combine(dump, "sce_sys")); Directory.CreateDirectory(output); Directory.CreateDirectory(scratch);
                    File.WriteAllText(Path.Combine(dump, "sce_sys", "param.json"), "{\"contentId\":\"UP0000-PPSA99999_00-SDKEDITORTEST001\",\"contentVersion\":\"01.000.000\",\"masterVersion\":\"01.00\",\"sdkVersion\":\"0x0900000000000000\",\"requiredSystemSoftwareVersion\":\"0x0900000000000000\",\"localizedParameters\":{\"defaultLanguage\":\"en-US\",\"en-US\":{\"titleName\":\"Fast SDK synthetic test\"}}}");
                    byte[] data = new byte[2500123]; new Random(701).NextBytes(data); File.WriteAllBytes(Path.Combine(dump, "data.bin"), data);
                    string code = new string('0', 32), pkg;
                    using (Form form = (Form)Activator.CreateInstance(BuilderBridge.FormType, true))
                    {
                        ((ComboBox)BuilderBridge.Get(form, "languageCombo")).SelectedIndex = 1;
                        object options = BuilderBridge.PrepareOptions(form, dump, output, scratch, CancellationToken.None);
                        BuilderBridge.Set(options, "KrakenBackend", Enum.Parse(BuilderBridge.Get(options, "KrakenBackend").GetType(), "Uncompressed"));
                        BuilderBridge.Set(options, "PublisherImageMode", Enum.Parse(BuilderBridge.Get(options, "PublisherImageMode").GetType(), mode));
                        BuilderBridge.Set(options, "SdkVersionOverride", SdkVersion.ForBuild("9"));
                        pkg = BuilderBridge.Build(options, log);
                    }
                    foreach (string version in new string[] { "10.01", "11.40", "0x0900000000001234" })
                    {
                        PackageSnapshot snapshot = PackageSdk.Inspect(pkg, code); byte[] original = File.ReadAllBytes(pkg);
                        string backup = MetadataTransaction.Save(snapshot, code, SdkVersion.Parse(version), CancellationToken.None, delegate { }, log);
                        byte[] changed = File.ReadAllBytes(pkg); PackageSnapshot actual = PackageSdk.Inspect(pkg, code);
                        if (actual.Length != snapshot.Length || SdkVersion.Parse(actual.Sdk) != SdkVersion.Parse(version)) throw new Exception("SDK or file-length mismatch.");
                        for (int i = 0; i < snapshot.CntOffset; i++) if (original[i] != changed[i]) throw new Exception("A game payload byte was modified.");
                        if (!(bool)PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPackageArchive", "VerifyCntMetadataSignature", new Type[] { typeof(string) }, pkg)) throw new Exception("The library rejected the updated CNT header wrap.");
                        MetadataTransaction.Restore(pkg, backup, log);
                        if (!FileWorkspace.Equal(original, File.ReadAllBytes(pkg))) throw new Exception("Undo did not restore the entire synthetic PKG exactly.");
                        log("PASS " + mode + " SDK " + version + ": detected, metadata updated, PFS unchanged, library wrap verified, exact undo");
                    }
                    TestMetadataRepair(pkg, code, mode, log);
                    byte[] unmodified = File.ReadAllBytes(pkg); bool cancelled = false;
                    try { MetadataTransaction.Save(PackageSdk.Inspect(pkg, code), code, SdkVersion.Parse("10"), new CancellationToken(true), delegate { }, log); } catch (OperationCanceledException) { cancelled = true; }
                    if (!cancelled || !FileWorkspace.Equal(unmodified, File.ReadAllBytes(pkg))) throw new Exception("Cancellation changed the package.");
                }
                log("PASS synthetic fast SDK tests. Real-game/console compatibility is not tested."); return 0;
            }
            catch (Exception error) { log("FAIL " + error); return 1; }
            finally
            {
                FileWorkspace.DeleteOwned(root, log);
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat");
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "pkg-self-test.txt"), report.ToString());
            }
        }

        private static void TestMetadataRepair(string pkg, string code, string mode, Action<string> log)
        {
            byte[] good = File.ReadAllBytes(pkg); PackageSnapshot initial = PackageSdk.Inspect(pkg, code);
            if (PackageSdk.NeedsMetadataRepair(initial, code, CancellationToken.None, log)) throw new Exception("A fresh synthetic PKG unexpectedly needs repair.");
            byte[] damaged = (byte[])good.Clone(); damaged[checked((int)initial.CntOffset + 0x160)] ^= 0x40;
            File.WriteAllBytes(pkg, damaged);
            PackageSnapshot broken = PackageSdk.Inspect(pkg, code);
            if (!PackageSdk.NeedsMetadataRepair(broken, code, CancellationToken.None, log)) throw new Exception("Derived header corruption was not detected.");
            string backup = MetadataTransaction.Repair(broken, code, CancellationToken.None, delegate { }, log);
            if (!FileWorkspace.Equal(good, File.ReadAllBytes(pkg))) throw new Exception("Metadata repair did not recreate the original synthetic PKG exactly.");
            MetadataTransaction.Restore(pkg, backup, log);
            if (!FileWorkspace.Equal(damaged, File.ReadAllBytes(pkg))) throw new Exception("Repair undo was not byte-for-byte exact.");
            File.WriteAllBytes(pkg, good);
            int general;
            using (FileStream stream = File.OpenRead(pkg)) general = CntMetadata.Read(stream, pkg, code).Find(0x80).Offset;
            damaged = (byte[])good.Clone(); damaged[checked((int)initial.CntOffset + general + 0x40)] ^= 0x20;
            File.WriteAllBytes(pkg, damaged); bool refused = false;
            try { MetadataTransaction.Repair(PackageSdk.Inspect(pkg, code), code, CancellationToken.None, delegate { }, log); }
            catch (InvalidDataException) { refused = true; }
            if (!refused || !FileWorkspace.Equal(damaged, File.ReadAllBytes(pkg)) || File.Exists(MetadataTransaction.PendingPath(pkg)))
                throw new Exception("Repair did not refuse damaged CNT entry data before writing.");
            File.WriteAllBytes(pkg, good);
            log("PASS " + mode + " metadata checks: healthy input, exact derived-header repair, exact undo and refusal of damaged entry data");
        }
    }
}
