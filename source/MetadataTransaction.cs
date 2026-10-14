using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class MetadataPatch
    {
        internal int Offset;
        internal byte[] Before, After;
    }

    internal sealed class MetadataTransaction
    {
        internal long FileLength, CntOffset;
        internal int TailLength;
        internal byte[] IdentityHash, BeforeHash, AfterHash;
        internal readonly List<MetadataPatch> Patches = new List<MetadataPatch>();
        private const int MaxBackup = 64 * 1024 * 1024;

        internal static string PendingPath(string package) { return package + ".sdk-recovery"; }

        internal static string Save(PackageSnapshot expected, string passcode, ulong sdk, CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        { return SaveInternal(expected, passcode, sdk, token, report, log); }

        internal static string Repair(PackageSnapshot expected, string passcode, CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        { return SaveInternal(expected, passcode, null, token, report, log); }

        private static string SaveInternal(PackageSnapshot expected, string passcode, ulong? sdk, CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            string pending = PendingPath(expected.Path);
            if (File.Exists(pending) || Directory.Exists(pending)) throw new IOException("A pending metadata recovery exists. Use Restore metadata before another save: " + pending);
            FileWorkspace.NoReparse(expected.Path);
            using (FileStream stream = new FileStream(expected.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Checking metadata", "Reading the CNT/SI tail", 0, 0));
                CntMetadata doc = CntMetadata.Read(stream, expected.Path, passcode); PackageSdk.EnsureUnchanged(expected, doc.Snapshot);
                report(new ConversionProgress(sdk.HasValue ? "Preparing SDK edit" : "Preparing metadata repair", "Updating metadata checksums in memory", 0, 0));
                byte[] next = sdk.HasValue ? doc.Edit(sdk.Value, passcode, token, log) : doc.RepairMetadata(token, log);
                MetadataTransaction journal = Create(expected.Length, expected.CntOffset, expected.IdentityHash, doc.Tail, next);
                if (journal.Patches.Count == 0) throw new InvalidOperationException("The PKG already contains the requested metadata.");
                token.ThrowIfCancellationRequested(); journal.Write(pending);
                // After the durable journal exists, finish or roll back as one non-cancellable section.
                // No payload writes, resizing, extraction, reconstruction, or whole-PKG backup occur.
                try
                {
                    report(new ConversionProgress("Saving PKG", "Writing the changed metadata blocks", 0, 0));
                    journal.WriteRanges(stream, true, null); stream.Flush(true);
                    byte[] written = PackageSdk.ReadRange(stream, journal.CntOffset, journal.TailLength);
                    if (stream.Length != journal.FileLength || !FileWorkspace.Equal(PackageSdk.Sha256(written), journal.AfterHash)) throw new IOException("Metadata read-back verification failed.");
                    PackageSnapshot actual = CntMetadata.Read(stream, expected.Path, passcode).Snapshot;
                    if (sdk.HasValue && (SdkVersion.Parse(actual.Sdk) != sdk.Value || SdkVersion.Parse(actual.Required) != sdk.Value)) throw new IOException("SDK read-back verification failed.");
                    if (!sdk.HasValue && !FileWorkspace.Equal(actual.ParamBytes, expected.ParamBytes)) throw new IOException("Repair unexpectedly changed param.json.");
                }
                catch (Exception error)
                {
                    try
                    {
                        journal.WriteRanges(stream, false, null); stream.Flush(true);
                        if (!FileWorkspace.Equal(PackageSdk.Sha256(PackageSdk.ReadRange(stream, journal.CntOffset, journal.TailLength)), journal.BeforeHash)) throw new IOException("Rollback read-back failed.");
                        log("Original metadata restored after the error. Recovery file: " + pending);
                    }
                    catch (Exception rollback) { throw new IOException("Save and automatic recovery failed. Keep the recovery file and use Restore metadata: " + pending + "\r\n" + rollback.Message, error); }
                    throw;
                }
                long changed = 0; foreach (MetadataPatch patch in journal.Patches) changed += patch.After.Length;
                log("Written: " + FileWorkspace.Size(changed) + " in " + journal.Patches.Count + " metadata ranges. PKG length and PFS bytes unchanged.");
            }
            string backup = expected.Path + ".sdk-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sdk-backup";
            try { File.Move(pending, backup); }
            catch (IOException error) { log("Saved, but the recovery file could not be renamed: " + error.Message); backup = pending; }
            log("Metadata backup: " + backup); return backup;
        }

        internal static MetadataTransaction Create(long length, long cnt, byte[] identity, byte[] before, byte[] after)
        {
            if (before.Length != after.Length) throw new InvalidDataException("Metadata editing must not resize the package.");
            MetadataTransaction journal = new MetadataTransaction { FileLength = length, CntOffset = cnt, TailLength = before.Length,
                IdentityHash = identity, BeforeHash = PackageSdk.Sha256(before), AfterHash = PackageSdk.Sha256(after) };
            // Small pages keep the backup bounded and avoid one write per changed hash byte.
            for (int at = 0; at < before.Length; at += 4096)
            {
                int size = Math.Min(4096, before.Length - at); bool changed = false;
                for (int n = 0; n < size; n++) if (before[at + n] != after[at + n]) { changed = true; break; }
                if (changed) journal.Patches.Add(new MetadataPatch { Offset = at, Before = BinaryData.Slice(before, at, size), After = BinaryData.Slice(after, at, size) });
            }
            return journal;
        }

        internal void Write(string path)
        {
            byte[] content;
            using (MemoryStream memory = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8, true))
                {
                    writer.Write(Encoding.ASCII.GetBytes("FPKGSDK6")); writer.Write(1); writer.Write(FileLength); writer.Write(CntOffset); writer.Write(TailLength);
                    writer.Write(IdentityHash); writer.Write(BeforeHash); writer.Write(AfterHash); writer.Write(Patches.Count);
                    foreach (MetadataPatch patch in Patches)
                    { writer.Write(patch.Offset); writer.Write(patch.Before.Length); writer.Write(patch.Before); writer.Write(patch.After); if (memory.Length > MaxBackup - 32) throw new NotSupportedException("The metadata backup would exceed 64 MiB; fast save was stopped before writing."); }
                }
                content = memory.ToArray();
            }
            string staged = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { file.Write(content, 0, content.Length); byte[] digest = PackageSdk.Sha256(content); file.Write(digest, 0, digest.Length); file.Flush(true); }
                File.Move(staged, path); // A partially written backup is never published as a recovery journal.
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }

        internal static MetadataTransaction Read(string path)
        {
            FileWorkspace.NoReparse(path);
            byte[] bytes;
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 164 || input.Length > MaxBackup) throw new InvalidDataException("Invalid metadata backup size.");
                bytes = PackageSdk.ReadRange(input, 0, (int)input.Length);
            }
            byte[] content = BinaryData.Slice(bytes, 0, bytes.Length - 32);
            if (!FileWorkspace.Equal(PackageSdk.Sha256(content), BinaryData.Slice(bytes, bytes.Length - 32, 32))) throw new InvalidDataException("The metadata backup checksum is invalid.");
            using (MemoryStream input = new MemoryStream(content, false))
            using (BinaryReader reader = new BinaryReader(input))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "FPKGSDK6" || reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported metadata backup version.");
                MetadataTransaction result = new MetadataTransaction { FileLength = reader.ReadInt64(), CntOffset = reader.ReadInt64(), TailLength = reader.ReadInt32(),
                    IdentityHash = reader.ReadBytes(32), BeforeHash = reader.ReadBytes(32), AfterHash = reader.ReadBytes(32) };
                int count = reader.ReadInt32();
                if (result.CntOffset < 65536 || result.CntOffset % 65536 != 0 || result.TailLength < 0 || result.TailLength > CntMetadata.MaxTail ||
                    result.FileLength < result.CntOffset || result.FileLength - result.CntOffset != result.TailLength || count < 1 || count > 16384)
                    throw new InvalidDataException("Invalid metadata backup ranges.");
                int last = 0;
                for (int n = 0; n < count; n++)
                {
                    int offset = reader.ReadInt32(), size = reader.ReadInt32();
                    if (offset < last || size < 1 || size > 4096 || offset > result.TailLength || size > result.TailLength - offset || (long)size * 2 > input.Length - input.Position)
                        throw new InvalidDataException("Invalid metadata backup patch.");
                    result.Patches.Add(new MetadataPatch { Offset = offset, Before = reader.ReadBytes(size), After = reader.ReadBytes(size) }); last = offset + size;
                }
                if (input.Position != input.Length) throw new InvalidDataException("Unexpected trailing backup data."); return result;
            }
        }

        internal void WriteRanges(FileStream stream, bool forward, Action<int> afterWrite)
        {
            if (stream.Length != FileLength) throw new IOException("The PKG size no longer matches the backup.");
            int number = 0;
            foreach (MetadataPatch patch in Patches)
            {
                byte[] data = forward ? patch.After : patch.Before;
                stream.Position = checked(CntOffset + patch.Offset); stream.Write(data, 0, data.Length);
                if (afterWrite != null) afterWrite(++number);
            }
        }

        internal static void Restore(string package, string backup, Action<string> log)
        {
            MetadataTransaction journal = Read(backup); FileWorkspace.NoReparse(package);
            using (FileStream stream = new FileStream(package, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (stream.Length != journal.FileLength || !FileWorkspace.Equal(PackageSdk.Sha256(PackageSdk.ReadRange(stream, 0, 65536)), journal.IdentityHash))
                    throw new IOException("This backup does not belong to the selected PKG.");
                byte[] candidate = PackageSdk.ReadRange(stream, journal.CntOffset, journal.TailLength);
                foreach (MetadataPatch patch in journal.Patches)
                {
                    for (int n = 0; n < patch.Before.Length; n++)
                    {
                        byte actual = candidate[patch.Offset + n];
                        if (actual != patch.Before[n] && actual != patch.After[n]) throw new IOException("The PKG has unrelated changes. Restore was stopped before writing.");
                    }
                    BinaryData.Put(candidate, patch.Offset, patch.Before);
                }
                if (!FileWorkspace.Equal(PackageSdk.Sha256(candidate), journal.BeforeHash)) throw new IOException("Unchanged metadata no longer matches this backup.");
                journal.WriteRanges(stream, false, null); stream.Flush(true);
                if (!FileWorkspace.Equal(PackageSdk.Sha256(PackageSdk.ReadRange(stream, journal.CntOffset, journal.TailLength)), journal.BeforeHash)) throw new IOException("Restore verification failed. Keep the backup and retry.");
            }
            if (Path.GetFullPath(backup).Equals(Path.GetFullPath(PendingPath(package)), StringComparison.OrdinalIgnoreCase))
                File.Move(backup, package + ".restored-" + Guid.NewGuid().ToString("N") + ".sdk-backup");
            log("Original metadata restored. Package length and PFS bytes unchanged.");
        }
    }
}
