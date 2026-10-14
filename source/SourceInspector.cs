using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class SourceInfo
    {
        internal string SourcePath, SourceType, Title, ContentId, TitleId, Region, Version, PackageType, Sdk, RequiredSystem, CoverPath, BackgroundPath;
        internal long Size;
        internal int Entries;
    }

    internal static class SourceInspector
    {
        private static readonly string ReaderCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat", "reader-cache");

        internal static SourceInfo InspectFolder(string folder)
        {
            folder = Path.GetFullPath(folder);
            string paramPath = Path.Combine(folder, "sce_sys", "param.json");
            if (!File.Exists(paramPath)) throw new InvalidDataException("sce_sys/param.json was not found in the selected source folder.");
            byte[] rawParam = File.ReadAllBytes(paramPath);
            PackageParam param = new PackageParam(rawParam);
            SourceInfo info = FromParam(param);
            if (string.IsNullOrEmpty(info.Title)) info.Title = FindJsonString(rawParam, "titleName");
            info.SourcePath = folder; info.SourceType = "Folder"; info.PackageType = Category(param.Text("applicationCategoryType"));
            info.CoverPath = Existing(Path.Combine(folder, "sce_sys", "icon0.png"), Path.Combine(folder, "sce_sys", "icon0.jpg"), Path.Combine(folder, "sce_sys", "icon0.jpeg"));
            info.BackgroundPath = Existing(Path.Combine(folder, "sce_sys", "pic1.png"), Path.Combine(folder, "sce_sys", "pic0.png"), Path.Combine(folder, "sce_sys", "pic2.png"), Path.Combine(folder, "sce_sys", "pic1.jpg"));
            CountFolder(folder, out info.Size, out info.Entries);
            return info;
        }

        internal static SourceInfo InspectPkg(string path, string passcode)
        {
            path = Path.GetFullPath(path);
            string key = string.IsNullOrEmpty(passcode) ? new string('0', 32) : passcode;
            if (key.Length != 32) throw new ArgumentException("Passcode must contain exactly 32 characters.");

            SourceInfo info = new SourceInfo();
            info.SourcePath = path; info.SourceType = "PKG"; info.Size = new FileInfo(path).Length;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CntMetadata doc = CntMetadata.Read(stream, path, key);
                    PackageSnapshot snap = doc.Snapshot;
                    PackageParam param = new PackageParam(snap.ParamBytes);
                    SourceInfo from = FromParam(param);
                    info.Title = from.Title; info.ContentId = from.ContentId; info.TitleId = from.TitleId; info.Region = from.Region;
                    info.Version = from.Version; info.Sdk = from.Sdk; info.RequiredSystem = from.RequiredSystem;
                    info.PackageType = "Debug FPKG"; info.Entries = snap.EntryCount;
                    ExtractReaderArtwork(doc, key, info);
                }
            }
            catch (Exception first)
            {
                try
                {
                    object pkg = PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPkgReader", "Read", new Type[] { typeof(string) }, path);
                    object header = BuilderBridge.Get(pkg, "Header");
                    info.PackageType = Convert.ToString(BuilderBridge.Get(pkg, "Type"), CultureInfo.InvariantCulture);
                    if (header != null)
                    {
                        info.ContentId = Convert.ToString(BuilderBridge.Get(header, "ContentId"), CultureInfo.InvariantCulture);
                        info.TitleId = TitleFromContent(info.ContentId); info.Region = Region(info.ContentId);
                        object count = BuilderBridge.Get(header, "EntryCount");
                        if (count != null) info.Entries = Convert.ToInt32(count, CultureInfo.InvariantCulture);
                    }
                    if (string.IsNullOrEmpty(info.PackageType)) info.PackageType = "PKG";
                }
                catch
                {
                    throw new InvalidDataException("The PKG could not be read. If it uses encrypted metadata, verify the 32-character passcode.", first);
                }
            }
            return info;
        }

        private static void ExtractReaderArtwork(CntMetadata doc, string passcode, SourceInfo info)
        {
            try
            {
                Directory.CreateDirectory(ReaderCache);
                foreach (string file in Directory.GetFiles(ReaderCache)) { try { File.Delete(file); } catch { } }

                byte[] bytes; string name;
                if (doc.TryReadNamedEntry(passcode, new string[] { "sce_sys/icon0.png", "icon0.png", "sce_sys/icon0.dds", "icon0.dds" }, out bytes, out name))
                    info.CoverPath = WritePreviewImage(bytes, name, Path.Combine(ReaderCache, "cover"));

                if (doc.TryReadNamedEntry(passcode, new string[] { "sce_sys/pic1.png", "pic1.png", "sce_sys/pic1.dds", "pic1.dds", "sce_sys/pic0.png", "pic0.png", "sce_sys/pic0.dds", "pic0.dds", "sce_sys/pic2.png", "pic2.png", "sce_sys/pic2.dds", "pic2.dds" }, out bytes, out name))
                    info.BackgroundPath = WritePreviewImage(bytes, name, Path.Combine(ReaderCache, "background"));
            }
            catch { }
        }

        private static string WritePreviewImage(byte[] bytes, string sourceName, string destinationWithoutExtension)
        {
            if (bytes == null || bytes.Length < 4) return null;
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47)
            {
                string path = destinationWithoutExtension + ".png"; File.WriteAllBytes(path, bytes); return path;
            }
            if (bytes[0] == 0xff && bytes[1] == 0xd8)
            {
                string path = destinationWithoutExtension + ".jpg"; File.WriteAllBytes(path, bytes); return path;
            }
            if (bytes[0] == (byte)'D' && bytes[1] == (byte)'D' && bytes[2] == (byte)'S' && bytes[3] == (byte)' ')
            {
                string raw = destinationWithoutExtension + ".dds";
                string png = destinationWithoutExtension + ".png";
                File.WriteAllBytes(raw, bytes);
                if (ConvertWithMagick(raw, png)) { try { File.Delete(raw); } catch { } return png; }
                return null;
            }

            string extension = Path.GetExtension(sourceName ?? "").ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                string path = destinationWithoutExtension + extension; File.WriteAllBytes(path, bytes); return path;
            }
            return null;
        }

        private static bool ConvertWithMagick(string input, string output)
        {
            object image = null;
            try
            {
                string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Magick.NET-Q8-AnyCPU.dll");
                if (!File.Exists(assemblyPath)) return false;
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                Type type = assembly.GetType("ImageMagick.MagickImage", false);
                if (type == null) return false;

                ConstructorInfo ctor = type.GetConstructor(new Type[] { typeof(string) });
                if (ctor != null) image = ctor.Invoke(new object[] { input });
                else
                {
                    ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) return false;
                    image = ctor.Invoke(null);
                    MethodInfo read = type.GetMethod("Read", new Type[] { typeof(string) });
                    if (read == null) return false;
                    read.Invoke(image, new object[] { input });
                }

                MethodInfo write = type.GetMethod("Write", new Type[] { typeof(string) });
                if (write == null) return false;
                write.Invoke(image, new object[] { output });
                return File.Exists(output) && new FileInfo(output).Length > 0;
            }
            catch { return false; }
            finally
            {
                IDisposable disposable = image as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        internal static SourceInfo InspectPreviewFolder(string root, string sourceType, long sourceSize)
        {
            SourceInfo info = InspectFolder(root); info.SourceType = sourceType; info.Size = sourceSize; return info;
        }

        private static SourceInfo FromParam(PackageParam param)
        {
            SourceInfo info = new SourceInfo();
            info.ContentId = param.Text("contentId");
            info.TitleId = param.Text("titleId");
            if (string.IsNullOrEmpty(info.TitleId)) info.TitleId = TitleFromContent(info.ContentId);
            info.Title = param.Text("titleName");
            if (string.IsNullOrEmpty(info.Title)) info.Title = param.Text("name");
            info.Version = param.Text("contentVersion");
            info.Sdk = SdkVersion.Describe(param.Text("sdkVersion"));
            info.RequiredSystem = SdkVersion.Describe(param.Text("requiredSystemSoftwareVersion"));
            info.Region = Region(info.ContentId);
            return info;
        }

        internal static string Category(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Unknown";
            string v = value.ToLowerInvariant();
            if (v.Contains("gd")) return "Game / Application";
            if (v.Contains("gp")) return "Patch / Update";
            if (v.Contains("ac")) return "Additional Content";
            return value;
        }

        private static string TitleFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            int dash = content.IndexOf('-'); if (dash < 0 || dash + 10 >= content.Length) return "";
            string candidate = content.Substring(dash + 1);
            int underscore = candidate.IndexOf('_'); if (underscore > 0) candidate = candidate.Substring(0, underscore);
            return candidate.Length >= 9 ? candidate.Substring(0, 9) : candidate;
        }

        private static string Region(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 2) return "Unknown";
            string prefix = content.Substring(0, 2).ToUpperInvariant();
            if (prefix == "UP") return "US";
            if (prefix == "EP") return "Europe";
            if (prefix == "JP") return "Japan";
            if (prefix == "HP") return "Asia";
            if (prefix == "KP") return "Korea";
            return prefix;
        }

        private static string FindJsonString(byte[] bytes, string key)
        {
            try
            {
                string text = new UTF8Encoding(false, true).GetString(bytes);
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text, "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success) return "";
                string value = match.Groups[1].Value;
                return value.Replace("\\n", " ").Replace("\\r", " ").Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            catch { return ""; }
        }

        private static string Existing(params string[] paths)
        { foreach (string path in paths) if (File.Exists(path)) return path; return null; }

        private static void CountFolder(string root, out long size, out int entries)
        {
            size = 0; entries = 0;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            { FileInfo fi = new FileInfo(file); size += fi.Length; entries++; }
        }
    }
}
