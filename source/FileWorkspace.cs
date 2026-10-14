using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class ConversionProgress
    {
        internal string Stage;
        internal string Detail;
        internal long Done;
        internal long Total;
        internal ConversionProgress(string stage, string detail, long done, long total)
        { Stage = stage; Detail = detail; Done = done; Total = total; }
    }

    internal sealed class CopyEntry
    {
        internal string Relative;
        internal bool Directory;
        internal long Length;
    }

    internal sealed class CopyPlan
    {
        internal string Source;
        internal long Bytes;
        internal int Files;
        internal readonly List<CopyEntry> Entries = new List<CopyEntry>();
    }

    internal static class FileWorkspace
    {
        internal static string Full(string path) { return Path.GetFullPath(path); }

        internal static bool Inside(string path, string folder)
        {
            string root = Full(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string item = Full(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return item.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static void NoReparse(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsupported link or reparse point: " + path);
        }

        internal static string GameRoot(string mountRoot)
        {
            if (File.Exists(Path.Combine(mountRoot, "sce_sys", "param.json"))) return mountRoot;
            string result = null;
            foreach (string child in Directory.EnumerateDirectories(mountRoot))
            {
                string name = Path.GetFileName(child);
                if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)) continue;
                NoReparse(child);
                if (!File.Exists(Path.Combine(child, "sce_sys", "param.json"))) continue;
                if (result != null) throw new InvalidDataException("The image contains multiple dumps. Select an image containing one game.");
                result = child;
            }
            if (result == null) throw new InvalidDataException("sce_sys/param.json was not found at the image root or one level below it.");
            return result;
        }

        internal static CopyPlan Scan(string source, CancellationToken token)
        {
            CopyPlan plan = new CopyPlan { Source = Full(source) };
            Queue<string> pending = new Queue<string>(); pending.Enqueue("");
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                string relative = pending.Dequeue();
                string parent = Path.Combine(plan.Source, relative);
                NoReparse(parent);
                foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
                {
                    token.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(entry);
                    if (relative.Length == 0 && (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))) continue;
                    NoReparse(entry);
                    string next = Path.Combine(relative, name);
                    bool directory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                    long size = directory ? 0 : new FileInfo(entry).Length;
                    plan.Entries.Add(new CopyEntry { Relative = next, Directory = directory, Length = size });
                    if (directory) pending.Enqueue(next);
                    else { plan.Bytes = checked(plan.Bytes + size); plan.Files++; }
                }
            }
            return plan;
        }

        internal static void Copy(CopyPlan plan, string destination, bool verify, CancellationToken token, Action<ConversionProgress> progress)
        {
            if (Inside(destination, plan.Source)) throw new IOException("The copy destination cannot be inside the source.");
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("The temporary folder already exists.");
            Directory.CreateDirectory(destination);
            byte[] buffer = new byte[1024 * 1024];
            long done = 0;
            Stopwatch reportClock = Stopwatch.StartNew();
            foreach (CopyEntry entry in plan.Entries)
            {
                token.ThrowIfCancellationRequested();
                string from = Path.Combine(plan.Source, entry.Relative), to = Path.Combine(destination, entry.Relative);
                if (!Inside(from, plan.Source) || !Inside(to, destination)) throw new IOException("Invalid dump path.");
                NoReparse(from);
                if (entry.Directory) { Directory.CreateDirectory(to); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                byte[] expectedHash = null;
                try
                {
                    using (FileStream input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                    using (FileStream output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
                    using (SHA256 hash = SHA256.Create())
                    {
                        if (input.Length != entry.Length) throw new IOException("The source file size changed: " + entry.Relative);
                        long copied = 0; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, count);
                            if (verify) hash.TransformBlock(buffer, 0, count, buffer, 0);
                            copied += count; done += count;
                            if (reportClock.ElapsedMilliseconds >= 200)
                            { progress(new ConversionProgress("Copying dump", entry.Relative, done, plan.Bytes)); reportClock.Restart(); }
                        }
                        if (copied != entry.Length) throw new IOException("Incomplete copy: " + entry.Relative);
                        if (verify) { hash.TransformFinalBlock(new byte[0], 0, 0); expectedHash = hash.Hash; }
                        output.Flush();
                    }
                    if (verify)
                    {
                        progress(new ConversionProgress("Verifying copied files", entry.Relative, 0, 0));
                        byte[] actualHash = HashFile(to, token);
                        if (!Equal(expectedHash, actualHash)) throw new IOException("SHA-256 verification failed: " + entry.Relative);
                    }
                    File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
                    progress(new ConversionProgress("Copying dump", entry.Relative, done, plan.Bytes));
                }
                catch { if (File.Exists(to)) File.Delete(to); throw; }
            }
        }

        internal static byte[] HashFile(string path, CancellationToken token)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[1024 * 1024]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
                hash.TransformFinalBlock(new byte[0], 0, 0); return hash.Hash;
            }
        }

        internal static bool Equal(byte[] first, byte[] second)
        {
            if (first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++) if (first[index] != second[index]) return false;
            return true;
        }

        internal static string CreateOwned(string parent)
        {
            Directory.CreateDirectory(parent);
            string path = Path.Combine(Full(parent), ".fpkg-exfat-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("The working folder already exists.");
            Directory.CreateDirectory(path);
            return path;
        }

        // Only paths returned by CreateOwned may be passed here. Do not follow a
        // junction during cleanup, even if something changed after creation.
        internal static void DeleteOwned(string path, Action<string> log)
        {
            if (path == null) return;
            string name = Path.GetFileName(path);
            Guid identity;
            if (!name.StartsWith(".fpkg-exfat-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(12), "N", out identity))
                throw new IOException("Refused to clean a folder not owned by this operation.");
            try { DeleteTree(path); }
            catch (Exception error) { if (log != null) log("Temporary files to remove after closing the program: " + path + " (" + error.Message + ")"); }
        }

        private static void DeleteTree(string directory)
        {
            if (!Directory.Exists(directory)) return;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(directory); return; }
            foreach (string file in Directory.EnumerateFiles(directory))
            { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            foreach (string child in Directory.EnumerateDirectories(directory)) DeleteTree(child);
            Directory.Delete(directory);
        }

        internal static long FreeBytes(string directory)
        {
            string root = Path.GetPathRoot(Full(directory));
            return new DriveInfo(root).AvailableFreeSpace;
        }

        internal static string Size(long bytes) { return (bytes / 1073741824.0).ToString("0.00") + " GiB"; }

        internal static void Commit(string stagedPackage, string finalPackage)
        {
            if (File.Exists(finalPackage) || Directory.Exists(finalPackage))
                throw new IOException("The output file already exists. Choose another name: " + finalPackage);
            // Both paths are on the output volume. File.Move never overwrites an
            // existing package, including a file created after the initial check.
            File.Move(stagedPackage, finalPackage);
        }
    }
}
