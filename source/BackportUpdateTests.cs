using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FpkgExfat
{
    internal static class BackportUpdateTests
    {
        internal static void Quick(string root, Action<bool, string> check)
        {
            check(BackportVersion.IsValid("01.001.004"), "Accept PS5 content version 01.001.004");
            check(!BackportVersion.IsValid("UP9000-PPSA03671_00-MARVELSWOLVERINE-A0101-V0101"), "Reject content ID in the version field");
            check(BackportVersion.Compare("01.001.005", "01.001.004") > 0, "Compare dotted versions numerically");
            check(BackportVersion.Next("01.001.004") == "01.001.005", "Suggest the next patch version");

            bool unsafePath = false;
            try { BackportPath.NormalizeRelative("../outside.bin"); }
            catch (ArgumentException) { unsafePath = true; }
            check(unsafePath, "Reject backport paths that escape the game root");

            string input = Path.Combine(root, "backport-input");
            Directory.CreateDirectory(Path.Combine(input, "sce_module"));
            Directory.CreateDirectory(Path.Combine(input, "sce_sys", "about"));
            File.WriteAllText(Path.Combine(input, "eboot.bin"), "old eboot");
            File.WriteAllText(Path.Combine(input, "sce_module", "libc.prx"), "libc");
            File.WriteAllText(Path.Combine(input, "sce_sys", "about", "right.sprx"), "reserved");
            File.WriteAllText(Path.Combine(input, "backup.esbak"), "tool backup");

            BackportFileSet set = BackportFileSet.Load(input);
            check(set.Items.Count == 3, "Show regular and SDK-reserved backport files, hide .esbak backups");
            BackportFileItem eboot = set.Find("eboot.bin");
            check(eboot != null && eboot.Include, "Include ordinary backport files by default");
            BackportFileItem reserved = set.Find("sce_sys/about/right.sprx");
            check(reserved != null && !reserved.Include && !string.IsNullOrEmpty(reserved.SkipReason), "Disable SDK-reserved sce_sys/about files");

            string replacement = Path.Combine(root, "replacement.bin");
            File.WriteAllText(replacement, "new eboot");
            set.Replace("eboot.bin", replacement);
            set.IncludeFile("sce_module/libc.prx", false);
            string staged = Path.Combine(root, "selected-overlay");
            set.Stage(staged, null, System.Threading.CancellationToken.None);
            check(File.ReadAllText(Path.Combine(staged, "eboot.bin")) == "new eboot", "Stage replacement bytes at the original relative path");
            check(!File.Exists(Path.Combine(staged, "sce_module", "libc.prx")), "Do not stage an unchecked file");
            check(!File.Exists(Path.Combine(staged, "sce_sys", "about", "right.sprx")), "Never stage the SDK-reserved file");

            string empty = Path.Combine(root, "empty-overlay");
            Directory.CreateDirectory(empty);
            BackportFileSet emptySet = BackportFileSet.Load(empty);
            bool emptyRejected = false;
            try { emptySet.Stage(Path.Combine(root, "empty-stage"), null, System.Threading.CancellationToken.None); }
            catch (InvalidDataException) { emptyRejected = true; }
            check(emptyRejected, "Refuse to save an overlay with no selected files");

            TestGameVersionMatch(check);

            TestGp5Overlay(root, check);
        }

        private static void TestGameVersionMatch(Action<bool, string> check)
        {
            string contentId = "UP9000-PPSA03671_00-MARVELSWOLVERINE";
            string json = "{\"contentId\":\"" + contentId + "\",\"contentVersion\":\"01.001.004\",\"sdkVersion\":\"0x0900000000000000\",\"requiredSystemSoftwareVersion\":\"0x0900000000000000\"}";
            PackageSnapshot snapshot = new PackageSnapshot { ContentId = contentId, ContentVersion = "01.001.004" };
            bool matching = true, differentVersionRejected = false;
            try { BackportUpdateJob.ValidateGameFolderMatch(snapshot, new PackageParam(Encoding.UTF8.GetBytes(json))); }
            catch { matching = false; }
            try { BackportUpdateJob.ValidateGameFolderMatch(snapshot, new PackageParam(Encoding.UTF8.GetBytes(json.Replace("01.001.004", "01.001.005")))); }
            catch (InvalidDataException) { differentVersionRejected = true; }
            check(matching, "Accept a game folder from the exact base PKG version");
            check(differentVersionRejected, "Reject a game folder from a different base version");
        }

        private static void TestGp5Overlay(string root, Action<bool, string> check)
        {
            string game = Path.Combine(root, "gp5-game"), overlay = Path.Combine(root, "gp5-overlay");
            Directory.CreateDirectory(Path.Combine(game, "sce_sys"));
            Directory.CreateDirectory(Path.Combine(game, "sce_sys", "about"));
            Directory.CreateDirectory(overlay);
            string contentId = "UP9000-PPSA03671_00-MARVELSWOLVERINE";
            string json = "{\"contentId\":\"" + contentId + "\",\"contentVersion\":\"01.001.004\",\"sdkVersion\":\"0x0900000000000000\",\"requiredSystemSoftwareVersion\":\"0x0900000000000000\",\"localizedParameters\":{\"defaultLanguage\":\"en-US\",\"en-US\":{\"titleName\":\"GP5 test\"}}}";
            File.WriteAllText(Path.Combine(game, "sce_sys", "param.json"), json);
            File.WriteAllBytes(Path.Combine(game, "sce_sys", "keystone"), new byte[96]);
            File.WriteAllText(Path.Combine(game, "eboot.bin"), "base eboot");
            File.WriteAllText(Path.Combine(game, "sce_sys", "about", "right.sprx"), "reserved");
            File.WriteAllText(Path.Combine(game, "sce_sys", "z-last.prx"), "base z-last");
            File.WriteAllText(Path.Combine(overlay, "eboot.bin"), "patched eboot");
            File.WriteAllText(Path.Combine(overlay, "sce_sys", "param.json"), json.Replace("01.001.004", "01.001.005"));
            File.WriteAllText(Path.Combine(overlay, "sce_sys", "z-last.prx"), "patched z-last");
            Dictionary<string, string> mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "eboot.bin", Path.Combine(overlay, "eboot.bin") },
                { "sce_sys/param.json", Path.Combine(overlay, "sce_sys", "param.json") },
                { "sce_sys/z-last.prx", Path.Combine(overlay, "sce_sys", "z-last.prx") },
                { "sce_sys/about/right.sprx", Path.Combine(game, "sce_sys", "about", "right.sprx") }
            };
            string gp5 = Path.Combine(root, "backport-test.gp5");
            int count = BackportUpdateJob.CreatePatchProject(game, mappings, Path.Combine(game, "sce_sys", "keystone"), gp5,
                new string('0', 32), null);
            object project = PackageSdk.Call("LibProsperoPkg.GP5.Gp5Project", "ReadFrom", new Type[] { typeof(string) }, gp5);
            IList files = BuilderBridge.Get(project, "Files") as IList;
            bool patchedSource = false, paramSource = false, lastSource = false, keptKeystone = false, omittedAbout = true;
            foreach (object entry in files)
            {
                string destination = Convert.ToString(BuilderBridge.Get(entry, "DestinationPath"));
                string source = Convert.ToString(BuilderBridge.Get(entry, "SourcePath"));
                string normalized = destination.Replace('\\', '/');
                if (normalized == "eboot.bin") patchedSource = source == Path.Combine(overlay, "eboot.bin");
                if (normalized == "sce_sys/param.json") paramSource = source == Path.Combine(overlay, "sce_sys", "param.json");
                if (normalized == "sce_sys/z-last.prx") lastSource = source == Path.Combine(overlay, "sce_sys", "z-last.prx");
                if (normalized == "sce_sys/keystone") keptKeystone = source == Path.Combine(game, "sce_sys", "keystone");
                if (normalized.StartsWith("sce_sys/about/", StringComparison.OrdinalIgnoreCase)) omittedAbout = false;
            }
            check(count == files.Count, "Write an explicit GP5 project that can be reopened");
            check(patchedSource, "GP5 points a replaced file at its staged backport copy");
            check(paramSource, "GP5 points param.json at the requested update version");
            check(lastSource, "GP5 mappings remain correct after a reserved file is removed");
            check(keptKeystone, "GP5 keeps the base game's 96-byte keystone");
            check(omittedAbout, "GP5 omits SDK-reserved sce_sys/about files");
        }
    }
}
