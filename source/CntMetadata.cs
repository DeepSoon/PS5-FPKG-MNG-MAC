using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class CntEntry
    {
        internal uint Id;
        internal int Offset, Size, StoredSize;
        internal bool Encrypted;
        internal string Name;
        internal object Meta;
    }

    // Only the FIH header and the CNT/SI metadata tail are read. The PFS is never decoded.
    internal sealed class CntMetadata
    {
        internal const int MaxTail = 256 * 1024 * 1024;
        internal PackageSnapshot Snapshot;
        internal byte[] Tail;
        internal readonly List<CntEntry> Entries = new List<CntEntry>();
        internal StoredSi Si;
        private int limit, bodyOffset, bodySize, tableOffset, scCount;
        private CntEntry paramEntry;
        private readonly Dictionary<uint, CntEntry> byId = new Dictionary<uint, CntEntry>();

        internal static CntMetadata Read(Stream input, string path, string passcode)
        { return Read(input, path, passcode, false); }

        // Viewer/backport inspection does not need the potentially enormous SI supplement.
        internal static CntMetadata ReadSmall(Stream input, string path, string passcode)
        { return Read(input, path, passcode, true); }

        private static CntMetadata Read(Stream input, string path, string passcode, bool bounded)
        {
            if (passcode == null || passcode.Length != 32) throw new ArgumentException("Enter the package's 32-character passcode.");
            byte[] prefix = PackageSdk.ReadRange(input, 0, 65536);
            if (prefix[0] != 0x7f || prefix[1] != 'F' || prefix[2] != 'I' || prefix[3] != 'H' || prefix[5] != 0)
                throw new NotSupportedException("Fast SDK editing supports debug FIH/PPR PKGs. Retail, CNT-only and UFS2 packages are not supported.");
            long cnt = BinaryData.I64(prefix, 0x58, true), pfs = BinaryData.I64(prefix, 0x10, true), size = BinaryData.I64(prefix, 0x18, true);
            if (pfs != 65536 || size < 65536 || cnt != checked(pfs + size) || cnt % 65536 != 0 || cnt >= input.Length)
                throw new InvalidDataException("Unsupported FIH/PFS layout.");
            long tailSize = bounded ? BoundedCnt.Length(input, cnt, MaxTail) : input.Length - cnt;
            if (tailSize > MaxTail) throw new NotSupportedException("The metadata tail exceeds 256 MiB. Fast editing is unavailable for this layout; no full extraction will be started.");
            CntMetadata doc = new CntMetadata { Tail = PackageSdk.ReadRange(input, cnt, checked((int)tailSize)) };
            doc.Parse();
            byte[] param = doc.ResolveParam(passcode);
            PackageParam json = new PackageParam(param);
            string contentId = doc.ContentId();
            if (contentId != json.Text("contentId")) throw new InvalidDataException("The header and param.json contain different content IDs, or the passcode is incorrect.");
            long sb = BinaryData.I64(prefix, 0x20, true);
            if (sb < pfs || sb > cnt - 0x380) throw new InvalidDataException("Invalid PFS superblock location.");
            byte[] seed = PackageSdk.ReadRange(input, sb + 0x370, 16);
            doc.Snapshot = new PackageSnapshot { Path = Path.GetFullPath(path), Length = input.Length, Modified = File.GetLastWriteTimeUtc(path),
                CntOffset = cnt, ContentId = contentId, ContentVersion = json.Text("contentVersion"), Sdk = json.Text("sdkVersion"),
                Required = json.Text("requiredSystemSoftwareVersion"), ParamBytes = param, MetadataHash = PackageSdk.Sha256(doc.Tail),
                IdentityHash = PackageSdk.Sha256(prefix), Plaintext = Encoding.ASCII.GetString(seed) == "PPRPLAIN-NOAUTH!",
                ParamEntryId = doc.paramEntry.Id, ParamEntryName = doc.paramEntry.Name, EntryCount = doc.Entries.Count };
            return doc;
        }

        private string ContentId() { return Encoding.ASCII.GetString(BinaryData.Slice(Tail, 0x40, 0x30)).TrimEnd('\0'); }
        internal CntEntry Find(uint id)
        { CntEntry entry; if (!byId.TryGetValue(id, out entry)) throw new NotSupportedException("Required CNT entry is missing: 0x" + id.ToString("X")); return entry; }

        private void Parse()
        {
            BinaryData.Range(Tail, 0, 0x1180);
            if (Tail[0] != 0x7f || Tail[1] != 'C' || Tail[2] != 'N' || Tail[3] != 'T') throw new InvalidDataException("Invalid CNT magic.");
            Si = StoredSi.Open(Tail);
            limit = Si == null ? Tail.Length : Si.Start;
            int count = checked((int)BinaryData.U32(Tail, 0x10, false));
            if (count < 5 || count > 65536) throw new InvalidDataException("Invalid CNT entry count.");
            tableOffset = checked((int)BinaryData.U32(Tail, 0x18, false));
            scCount = BinaryData.U16(Tail, 0x14, false);
            bodyOffset = checked((int)BinaryData.I64(Tail, 0x20, false)); bodySize = checked((int)BinaryData.I64(Tail, 0x28, false));
            Require(tableOffset, checked(count * 32)); Require(bodyOffset, bodySize);
            if (tableOffset < 0x1180 || bodyOffset < 0x1180 || scCount > count || BinaryData.I64(Tail, 0x410, false) != 65536)
                throw new NotSupportedException("Unsupported CNT header profile.");
            Type metaType = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.MetaEntry", true);
            string[] fields = { "NameTableOffset", "Flags1", "Flags2", "DataOffset", "DataSize" };
            for (int i = 0; i < count; i++)
            {
                int position = tableOffset + i * 32;
                uint id = BinaryData.U32(Tail, position, false);
                if (byId.ContainsKey(id) || (i > 0 && id <= Entries[i - 1].Id)) throw new InvalidDataException("Duplicate or unsorted CNT entries.");
                object meta = Activator.CreateInstance(metaType);
                FieldInfo field = metaType.GetField("id"); field.SetValue(meta, Enum.ToObject(field.FieldType, id));
                for (int n = 0; n < fields.Length; n++) metaType.GetField(fields[n]).SetValue(meta, BinaryData.U32(Tail, position + 4 + n * 4, false));
                CntEntry entry = new CntEntry { Id = id, Meta = meta, Offset = checked((int)(uint)BuilderBridge.Get(meta, "DataOffset")),
                    Size = checked((int)(uint)BuilderBridge.Get(meta, "DataSize")), Encrypted = (bool)BuilderBridge.Get(meta, "Encrypted") };
                entry.StoredSize = entry.Encrypted ? checked((entry.Size + 15) & ~15) : entry.Size;
                Require(entry.Offset, entry.StoredSize);
                if (entry.Offset < 0x1180) throw new InvalidDataException("CNT entry overlaps its header.");
                Entries.Add(entry); byId.Add(id, entry);
            }
            List<CntEntry> ordered = new List<CntEntry>(Entries); ordered.Sort(delegate(CntEntry a, CntEntry b) { return a.Offset.CompareTo(b.Offset); });
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i].Offset < ordered[i - 1].Offset + ordered[i - 1].StoredSize) throw new InvalidDataException("Overlapping CNT entries.");
            if (Entries[0].Id != 1 || Find(1).Size != count * 32 || Find(0x10).Size != 0xb80 || Find(0x80).Size < 0xe0 || Find(0x100).Size != count * 32)
                throw new NotSupportedException("Fast editing requires the publisher debug CNT profile used by GUI 0.5.");
            foreach (uint id in new uint[] { 1, 0x10, 0x20, 0x80, 0x100 })
                if (byId.ContainsKey(id) && Find(id).Encrypted) throw new NotSupportedException("Encrypted CNT control entries are unsupported.");
            if (!FileWorkspace.Equal(BinaryData.Slice(Tail, tableOffset, count * 32), BinaryData.Slice(Tail, Find(0x100).Offset, count * 32)))
                throw new InvalidDataException("CNT metadata tables disagree.");
            if ((BinaryData.U32(Tail, Find(0x80).Offset + 28, false) & 0x40) == 0) throw new NotSupportedException("The param.json digest is not present.");
            if (Si == null)
            {
                int used = Math.Max(bodyOffset + bodySize, tableOffset + count * 32);
                foreach (CntEntry e in Entries) used = Math.Max(used, e.Offset + e.StoredSize);
                for (int i = used; i < Tail.Length; i++) if (Tail[i] != 0) throw new NotSupportedException("Unrecognized supplemental metadata. Fast editing was stopped.");
            }
        }

        private void ResolveNames()
        {
            try
            {
                Type readerType = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoPkgReader", false);
                if (readerType == null) return;
                MethodInfo read = readerType.GetMethod("Read", BuilderBridge.Members, null, new Type[] { typeof(Stream) }, null);
                if (read == null) return;
                using (MemoryStream stream = new MemoryStream(Tail, false))
                {
                    object package = BuilderBridge.Invoke(read, null, stream);
                    IEnumerable items = BuilderBridge.Get(package, "Entries") as IEnumerable;
                    if (items == null) return;
                    foreach (object item in items)
                    {
                        uint raw;
                        try { raw = Convert.ToUInt32(BuilderBridge.Get(item, "RawId")); } catch { continue; }
                        CntEntry entry; if (!byId.TryGetValue(raw, out entry)) continue;
                        try { entry.Name = BuilderBridge.Get(item, "Name") as string; } catch { }
                    }
                }
            }
            catch { } // Name resolution is an optimization; content validation below is authoritative.
        }

        private static bool IsParamName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string value = name.Replace('\\', '/').Trim().ToLowerInvariant();
            return value == "param.json" || value.EndsWith("/param.json", StringComparison.Ordinal);
        }

        private static void AddCandidate(List<CntEntry> list, HashSet<uint> ids, CntEntry entry)
        {
            if (entry != null && ids.Add(entry.Id)) list.Add(entry);
        }

        private bool LooksLikeParam(byte[] plain)
        {
            PackageParam json = new PackageParam(plain);
            if (!string.Equals(json.Text("contentId"), ContentId(), StringComparison.Ordinal)) return false;
            string sdk = json.Text("sdkVersion"), required = json.Text("requiredSystemSoftwareVersion");
            if (string.IsNullOrEmpty(sdk) || string.IsNullOrEmpty(required)) return false;
            SdkVersion.Parse(sdk); SdkVersion.Parse(required);
            return true;
        }

        // Do not assume that param.json has entry id 0x1000. Third-party/debug
        // packages can use a different raw id while keeping the same publisher CNT
        // geometry. Prefer the reader-resolved name, then the legacy id, then a
        // bounded metadata scan validated by contentId + fixed-width SDK fields.
        private byte[] ResolveParam(string passcode)
        {
            ResolveNames();
            List<CntEntry> candidates = new List<CntEntry>(); HashSet<uint> ids = new HashSet<uint>();
            foreach (CntEntry entry in Entries) if (IsParamName(entry.Name)) AddCandidate(candidates, ids, entry);
            CntEntry legacy; if (byId.TryGetValue(0x1000, out legacy)) AddCandidate(candidates, ids, legacy);
            foreach (CntEntry entry in Entries)
            {
                if (entry.Id == 1 || entry.Id == 0x10 || entry.Id == 0x20 || entry.Id == 0x80 || entry.Id == 0x100) continue;
                if (entry.Size <= 0 || entry.Size > 16 * 1024 * 1024) continue;
                AddCandidate(candidates, ids, entry);
            }
            Exception last = null;
            foreach (CntEntry entry in candidates)
            {
                if (entry.Size <= 0 || entry.Size > 16 * 1024 * 1024) continue;
                try
                {
                    byte[] plain = Plain(entry, passcode);
                    if (!LooksLikeParam(plain)) continue;
                    paramEntry = entry; return plain;
                }
                catch (Exception error) { last = error; }
            }
            throw new NotSupportedException("Could not locate or decode the CNT param.json entry. The package may use an unsupported metadata profile, or the 32-character passcode may be incorrect.", last);
        }


        internal bool TryReadNamedEntry(string passcode, string[] candidates, out byte[] data, out string resolvedName)
        {
            data = null; resolvedName = null;
            if (candidates == null || candidates.Length == 0) return false;
            ResolveNames();

            foreach (CntEntry entry in Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string normalized = entry.Name.Replace('\\', '/').Trim().ToLowerInvariant();
                bool match = false;
                for (int i = 0; i < candidates.Length && !match; i++)
                {
                    string candidate = (candidates[i] ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
                    if (candidate.Length == 0) continue;
                    match = normalized == candidate || normalized.EndsWith("/" + candidate, StringComparison.Ordinal);
                }
                if (!match) continue;

                try
                {
                    byte[] plain = Plain(entry, passcode);
                    if (plain != null && plain.Length > 0)
                    {
                        data = plain; resolvedName = entry.Name; return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private void Require(int offset, int length)
        { if (offset < 0 || length < 0 || offset > limit || length > limit - offset) throw new InvalidDataException("CNT range overlaps supplemental data or is outside the file."); }

        internal byte[] Plain(CntEntry entry, string passcode)
        {
            byte[] data = BinaryData.Slice(Tail, entry.Offset, entry.StoredSize);
            if (entry.Encrypted) data = (byte[])PackageSdk.Call("LibProsperoPkg.PKG.Entry", "Decrypt",
                new Type[] { typeof(byte[]), typeof(string), typeof(string), entry.Meta.GetType(), typeof(bool) }, data, ContentId(), passcode, entry.Meta, true);
            return BinaryData.Slice(data, 0, entry.Size);
        }

        private byte[] Encode(CntEntry entry, byte[] plain, string passcode)
        {
            if (plain.Length != entry.Size) throw new NotSupportedException("The edit would resize param.json. Fast mode only accepts fixed-width SDK fields.");
            if (!entry.Encrypted) return plain;
            Type generic = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.GenericEntry", true);
            object value = Activator.CreateInstance(generic, new object[] { BuilderBridge.Get(entry.Meta, "id"), string.IsNullOrEmpty(entry.Name) ? "param.json" : entry.Name });
            generic.GetField("FileData").SetValue(value, plain); generic.GetField("meta").SetValue(value, entry.Meta);
            using (MemoryStream output = new MemoryStream())
            {
                BuilderBridge.Invoke(generic.GetMethod("WriteEncrypted", new Type[] { typeof(Stream), typeof(string), typeof(string), typeof(bool) }), value, output, ContentId(), passcode, true);
                byte[] result = output.ToArray();
                if (result.Length != entry.StoredSize) throw new InvalidDataException("Encrypted metadata size changed."); return result;
            }
        }

        private static int FirstMismatchOutsideHeaderWrap(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length) return 0;
            for (int i = 0; i < expected.Length; i++)
            {
                if (i >= 0x1000 && i < 0x1180) continue;
                if (expected[i] != actual[i]) return i;
            }
            return -1;
        }

        internal byte[] Edit(ulong sdk, string passcode, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            SecondaryScProfile secondaryProfile = ResolveSecondaryProfile(Tail, token, log);
            if (secondaryProfile.FallbackCanonical && !Snapshot.Plaintext)
                throw new NotSupportedException("A non-canonical CNT+0x120 profile was found. Canonical reseal fallback is restricted to plaintext debug FIH packages; no bytes were written.");

            // On the guarded fallback path, validate the *source* package with its original
            // secondary hash preserved.  This proves that body/digest-table/SC1/descriptor/
            // package-digest/header-wrap relationships are otherwise the profile we know how
            // to reseal.  A mismatch anywhere else aborts before writing.
            byte[] preservedSecondary = secondaryProfile.FallbackCanonical ? BinaryData.Slice(Tail, 0x120, 32) : null;
            byte[] checkedTail = (byte[])Tail.Clone(); Seal(checkedTail, Snapshot.ParamBytes, secondaryProfile, token, preservedSecondary);
            int mismatch = FirstMismatchOutsideHeaderWrap(Tail, checkedTail);
            if (mismatch >= 0)
            {
                log("Existing metadata validation mismatch at CNT + 0x" + mismatch.ToString("X") + "; flags 0x" + BinaryData.U32(Tail, 4, false).ToString("X8"));
                throw new InvalidDataException("Existing CNT hashes do not match the supported PS5 profile (CNT + 0x" + mismatch.ToString("X") + "). No bytes were written.");
            }
            byte[] sourceHeader = BinaryData.Slice(Tail, 0, 0x1000);
            byte[] sourceWrap = BinaryData.Slice(Tail, 0x1000, 0x180);
            if (!PackageSdk.VerifyHeaderWrap(sourceHeader, sourceWrap))
                throw new InvalidDataException("The existing CNT header wrap at CNT + 0x1000 could not be verified. No bytes were written.");
            log("Existing CNT deterministic hashes and the original 0x180-byte header wrap validated.");
            if (secondaryProfile.FallbackCanonical)
                log("Guarded fallback validated: every deterministic CNT seal except the source +0x120 value reproduces exactly. The edited package will be resealed with the canonical PS5 SHA3-256 secondary-SC formula.");
            log("Per-entry digest semantics: SHA3-256 over DataSize bytes (encryption padding excluded), matching ProsperoPkgBuilder.CalcBodyDigests.");

            byte[] param = FixedParam.Retarget(Snapshot.ParamBytes, sdk);
            if (paramEntry == null) throw new InvalidOperationException("param.json was not resolved for this package.");
            byte[] next = (byte[])Tail.Clone(); BinaryData.Put(next, paramEntry.Offset, Encode(paramEntry, param, passcode));
            Seal(next, param, secondaryProfile, token, null);
            if (Si != null) Si.Update(next, Tail, Snapshot.CntOffset, Snapshot.ContentId, Snapshot.Sdk, Snapshot.Required, sdk, Find(0x80).Offset + 0xc0, token);
            token.ThrowIfCancellationRequested();
            log("Metadata read: " + FileWorkspace.Size(Tail.Length) + "; no PFS extraction, decompression or rebuild.");
            log("CNT hashes/header wrap and affected SI/PlayGo checksums updated.");
            return next;
        }

        internal byte[] EditForFullRebuild(ulong sdk, string passcode, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            byte[] param = FixedParam.Retarget(Snapshot.ParamBytes, sdk);
            if (paramEntry == null) throw new InvalidOperationException("param.json was not resolved for this package.");
            SecondaryScProfile profile = CanonicalSecondaryProfile();
            byte[] next = (byte[])Tail.Clone();
            BinaryData.Put(next, paramEntry.Offset, Encode(paramEntry, param, passcode));
            Seal(next, param, profile, token, null);
            if (Si != null) Si.Update(next, Tail, Snapshot.CntOffset, Snapshot.ContentId, Snapshot.Sdk, Snapshot.Required, sdk, Find(0x80).Offset + 0xc0, token);
            token.ThrowIfCancellationRequested();
            log("Full rebuild metadata: canonical SHA3-256 CNT seals regenerated from the payload and edited param.json.");
            log("Secondary SC profile: " + profile.Label + "; metadata rows=" + profile.MetaCount + ".");
            return next;
        }

        internal void ValidateCanonical(CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            SecondaryScProfile profile = CanonicalSecondaryProfile();
            byte[] expected = (byte[])Tail.Clone();
            Seal(expected, Snapshot.ParamBytes, profile, token, null);
            int mismatch = FirstMismatchOutsideHeaderWrap(Tail, expected);
            if (mismatch >= 0) throw new InvalidDataException("Rebuilt CNT verification failed at CNT + 0x" + mismatch.ToString("X") + ".");
            if (!PackageSdk.VerifyHeaderWrap(BinaryData.Slice(Tail, 0, 0x1000), BinaryData.Slice(Tail, 0x1000, 0x180)))
                throw new InvalidDataException("Rebuilt CNT header wrap verification failed.");
            log("Rebuilt CNT canonical hashes and header wrap verified.");
        }

        private SecondaryScProfile CanonicalSecondaryProfile()
        {
            CntEntry metas = Find(0x100);
            if (metas.Size % 32 != 0) throw new InvalidDataException("The CNT metadata table has an invalid size.");
            int max = metas.Size / 32;
            int rows = scCount;
            if (rows < 0 || rows > max) throw new InvalidDataException("The CNT SC entry count is outside the metadata table.");
            uint[] ids = ExistingIds(new uint[] { 0x10, 0x20, 0x80, 0x100 });
            if (ids.Length < 3 || !ContainsId(ids, 0x10) || !ContainsId(ids, 0x80) || !ContainsId(ids, 0x100))
                throw new NotSupportedException("The package does not contain the control entries required for a canonical PS5 rebuild.");
            return new SecondaryScProfile { Disabled = false, Sha256 = false, FallbackCanonical = false, MetaCount = rows, EntryIds = ids, Label = "canonical SHA3-256 " + JoinIds(ids) };
        }

        internal byte[] RepairMetadata(CancellationToken token, Action<string> log)
        {
            // Only derived header fields may be repaired. Never bless damaged CNT
            // entry data by silently replacing its reference digest.
            CntEntry digests = Find(1);
            for (int i = 1; i < Entries.Count; i++)
            {
                token.ThrowIfCancellationRequested(); CntEntry entry = Entries[i];
                if (!FileWorkspace.Equal(BinaryData.Slice(Tail, digests.Offset + i * 32, 32), PackageSdk.Sha3(Tail, entry.Offset, entry.Size)))
                    throw new InvalidDataException("CNT entry 0x" + entry.Id.ToString("X") + " fails its stored digest. Repair cannot establish which copy is correct; no bytes were written.");
            }
            if (!FileWorkspace.Equal(BinaryData.Slice(Tail, Find(0x80).Offset + 0xc0, 32), PackageSdk.Sha3(Snapshot.ParamBytes, 0, Snapshot.ParamBytes.Length)))
                throw new InvalidDataException("The plaintext param.json digest is invalid. Automatic metadata repair is unavailable.");
            SecondaryScProfile secondaryProfile = ResolveSecondaryProfile(Tail, token, log);
            if (secondaryProfile.FallbackCanonical)
                throw new NotSupportedException("Automatic metadata repair will not replace an unrecognized source CNT+0x120 hash. Use Save PKG only if you intentionally want the guarded canonical reseal fallback.");
            byte[] next = (byte[])Tail.Clone(); Seal(next, Snapshot.ParamBytes, secondaryProfile, token, null);
            for (int i = 0x1180; i < Tail.Length; i++)
                if (next[i] != Tail[i]) throw new InvalidDataException("Repair would alter a CNT entry. The repair was stopped.");
            if (Si != null) Si.UpdateValues(next, Tail, Snapshot.CntOffset, Snapshot.ContentId,
                Snapshot.Sdk, Snapshot.Required, Snapshot.Sdk, Snapshot.Required, Find(0x80).Offset + 0xc0, true, token);
            token.ThrowIfCancellationRequested();
            log("Checked CNT entry digests, plaintext param digest, derived header hashes/wrap and available SI metadata.");
            log("PFS game files, executable SDK records and console compatibility were not checked. This is not a universal black-screen fix.");
            return next;
        }

        // The secondary SC rollup at CNT+0x120 is normally SHA3-256 over
        // ENTRY_KEYS + IMAGE_KEY + GENERAL_DIGESTS + a prefix of METAS. In the wild,
        // debug/third-party builders can emit the same CNT class with a slightly different
        // SC-entry selection/order. Resolve the exact preimage against the package's existing
        // 32-byte value before editing; never guess and never weaken validation.
        private sealed class SecondaryScProfile
        {
            internal bool Disabled, Sha256, FallbackCanonical;
            internal int MetaCount;
            internal uint[] EntryIds;
            internal string Label;
        }

        private SecondaryScProfile ResolveSecondaryProfile(byte[] source, CancellationToken token, Action<string> log)
        {
            CntEntry metas = Find(0x100);
            if (metas.Size % 32 != 0) throw new InvalidDataException("The CNT metadata table has an invalid size.");
            int max = metas.Size / 32;
            byte[] expected = BinaryData.Slice(source, 0x120, 32);
            bool zero = true;
            for (int z = 0; z < expected.Length; z++) if (expected[z] != 0) { zero = false; break; }
            if (zero)
            {
                log("CNT secondary-SC hash is disabled (all zeroes); preserving the disabled profile.");
                return new SecondaryScProfile { Disabled = true, MetaCount = -1, EntryIds = new uint[0], Label = "disabled" };
            }

            List<int> counts = new List<int>();
            AddCount(counts, scCount, max);
            AddCount(counts, BinaryData.U16(source, 0x16, false), max);
            AddCount(counts, scCount - 1, max); AddCount(counts, scCount + 1, max);
            AddCount(counts, Entries.Count, max); AddCount(counts, Entries.Count - 1, max);
            if (max <= 4096) for (int n = 0; n <= max; n++) AddCount(counts, n, max);

            List<uint[]> sequences = new List<uint[]>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            // Canonical PS5/LibProsperoPkg profile first.
            AddSequence(sequences, seen, ExistingIds(new uint[] { 0x10, 0x20, 0x80, 0x100 }));

            // Then derive the SC set from Flags1 bit 0x20000000. This avoids assuming that
            // every third-party builder used the same raw ids for its control set.
            List<CntEntry> flaggedById = new List<CntEntry>();
            foreach (CntEntry e in Entries) if (IsSecondaryScEntry(e)) flaggedById.Add(e);
            AddOrderedSubsets(sequences, seen, flaggedById);
            List<CntEntry> flaggedByOffset = new List<CntEntry>(flaggedById);
            flaggedByOffset.Sort(delegate(CntEntry a, CntEntry b) { return a.Offset.CompareTo(b.Offset); });
            AddOrderedSubsets(sequences, seen, flaggedByOffset);

            // Compatibility search for common CNT control ids. Ordered subsets cover builders
            // that omitted IMAGE_KEY or included DIGESTS/ENTRY_NAMES in the secondary rollup.
            List<CntEntry> controlsById = new List<CntEntry>();
            foreach (uint id in new uint[] { 1, 0x10, 0x20, 0x80, 0x100, 0x200 })
            {
                CntEntry e; if (byId.TryGetValue(id, out e)) controlsById.Add(e);
            }
            AddOrderedSubsets(sequences, seen, controlsById);
            List<CntEntry> controlsByOffset = new List<CntEntry>(controlsById);
            controlsByOffset.Sort(delegate(CntEntry a, CntEntry b) { return a.Offset.CompareTo(b.Offset); });
            AddOrderedSubsets(sequences, seen, controlsByOffset);

            // The four canonical secondary-SC records are small enough to try every order.
            List<uint> core = new List<uint>();
            foreach (uint id in new uint[] { 0x10, 0x20, 0x80, 0x100 }) if (byId.ContainsKey(id)) core.Add(id);
            AddPermutations(sequences, seen, core, new List<uint>(), new bool[core.Count]);

            for (int si = 0; si < sequences.Count; si++)
            {
                token.ThrowIfCancellationRequested();
                uint[] ids = sequences[si];
                bool hasMetas = ContainsId(ids, 0x100);
                int loops = hasMetas ? counts.Count : 1;
                for (int ci = 0; ci < loops; ci++)
                {
                    int count = hasMetas ? counts[ci] : 0;
                    byte[] sha3 = SecondaryScHash(source, ids, count, false);
                    if (FileWorkspace.Equal(sha3, expected))
                        return ReportSecondaryProfile(ids, count, false, log);
                    byte[] sha256 = SecondaryScHash(source, ids, count, true);
                    if (FileWorkspace.Equal(sha256, expected))
                        return ReportSecondaryProfile(ids, count, true, log);
                }
            }

            string flagged = JoinIds(flaggedById);

            // Some converted/debug FIH packages carry a non-canonical or stale value in
            // sc_entries2_hash even though the remaining CNT sealing fields follow the
            // normal PS5 layout.  Do not guess by writing immediately.  Instead return
            // the standard PS5 profile as a *candidate* and let Edit() prove that every
            // other derived CNT field reproduces the original package while preserving
            // its existing +0x120 bytes.  Only a plaintext debug FIH may use this path.
            uint[] canonical = ExistingIds(new uint[] { 0x10, 0x20, 0x80, 0x100 });
            if (canonical.Length == 4 && scCount >= 0 && scCount <= max)
            {
                log("CNT+0x120 could not be reproduced from the source package. " +
                    "Trying guarded canonical PS5 reseal fallback: SHA3-256, entries=" + JoinIds(canonical) +
                    ", metadata rows=" + scCount + ".");
                log("Source profile: sc_entry_count=" + scCount + ", flagged SC ids=" + flagged +
                    ". The original +0x120 value will be preserved during source validation.");
                return new SecondaryScProfile
                {
                    Disabled = false, Sha256 = false, MetaCount = scCount, EntryIds = canonical,
                    Label = JoinIds(canonical), FallbackCanonical = true
                };
            }

            throw new InvalidDataException("The secondary CNT hash at CNT + 0x120 could not be reproduced and no safe canonical fallback is available. " +
                "sc_entry_count=" + scCount + ", flagged SC ids=" + flagged + ". No bytes were written.");
        }

        private SecondaryScProfile ReportSecondaryProfile(uint[] ids, int count, bool sha256, Action<string> log)
        {
            string algorithm = sha256 ? "SHA-256" : "SHA3-256";
            string label = JoinIds(ids);
            log("CNT secondary-SC profile validated: " + algorithm + ", entries=" + label +
                (ContainsId(ids, 0x100) ? ", metadata rows=" + count : ", no METAS prefix") + ".");
            return new SecondaryScProfile { Disabled = false, Sha256 = sha256, MetaCount = count, EntryIds = ids, Label = label };
        }

        private bool IsSecondaryScEntry(CntEntry entry)
        {
            try { return (Convert.ToUInt32(BuilderBridge.Get(entry.Meta, "Flags1")) & 0x20000000U) != 0; }
            catch { return false; }
        }

        private uint[] ExistingIds(uint[] ids)
        {
            List<uint> result = new List<uint>();
            for (int i = 0; i < ids.Length; i++) if (byId.ContainsKey(ids[i])) result.Add(ids[i]);
            return result.ToArray();
        }

        private static bool ContainsId(uint[] ids, uint id)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return true;
            return false;
        }

        private static string SequenceKey(uint[] ids)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < ids.Length; i++) { if (i != 0) b.Append(','); b.Append(ids[i].ToString("X8")); }
            return b.ToString();
        }

        private static void AddSequence(List<uint[]> values, HashSet<string> seen, uint[] ids)
        {
            if (ids == null || ids.Length == 0) return;
            string key = SequenceKey(ids);
            if (seen.Add(key)) values.Add(ids);
        }

        private static void AddOrderedSubsets(List<uint[]> values, HashSet<string> seen, List<CntEntry> ordered)
        {
            if (ordered == null || ordered.Count == 0 || ordered.Count > 16) return;
            int combinations = 1 << ordered.Count;
            for (int mask = 1; mask < combinations; mask++)
            {
                List<uint> ids = new List<uint>();
                for (int i = 0; i < ordered.Count; i++) if ((mask & (1 << i)) != 0) ids.Add(ordered[i].Id);
                AddSequence(values, seen, ids.ToArray());
            }
        }

        private static void AddPermutations(List<uint[]> values, HashSet<string> seen, List<uint> source, List<uint> current, bool[] used)
        {
            if (current.Count != 0) AddSequence(values, seen, current.ToArray());
            if (current.Count == source.Count) return;
            for (int i = 0; i < source.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true; current.Add(source[i]);
                AddPermutations(values, seen, source, current, used);
                current.RemoveAt(current.Count - 1); used[i] = false;
            }
        }

        private static void AddCount(List<int> values, int value, int max)
        {
            if (value < 0 || value > max || values.Contains(value)) return;
            values.Add(value);
        }

        private string JoinIds(uint[] ids)
        {
            if (ids == null || ids.Length == 0) return "(none)";
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                if (i != 0) b.Append('+');
                b.Append("0x").Append(ids[i].ToString("X"));
            }
            return b.ToString();
        }

        private string JoinIds(List<CntEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "(none)";
            uint[] ids = new uint[entries.Count];
            for (int i = 0; i < entries.Count; i++) ids[i] = entries[i].Id;
            return JoinIds(ids);
        }

        private byte[] SecondaryScHash(byte[] bytes, uint[] ids, int metaCount, bool sha256)
        {
            using (MemoryStream concat = new MemoryStream())
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    CntEntry entry = Find(ids[i]);
                    int length = entry.Size;
                    if (ids[i] == 0x100) length = checked(metaCount * 32);
                    Require(entry.Offset, length);
                    concat.Write(bytes, entry.Offset, length);
                }
                byte[] data = concat.ToArray();
                return sha256 ? PackageSdk.Sha256(data) : PackageSdk.Sha3(data, 0, data.Length);
            }
        }

        private byte[] SecondaryScHash(byte[] bytes, SecondaryScProfile profile)
        {
            if (profile == null || profile.Disabled) return new byte[32];
            return SecondaryScHash(bytes, profile.EntryIds, profile.MetaCount, profile.Sha256);
        }

        // Mirrors ProsperoPkgBuilder.CalcBodyDigests and FinishContainer in the supplied DLL.
        // Important: the per-entry digest covers MetaEntry.DataSize bytes, not AES padding.
        // Entry encryption may occupy StoredSize=align16(DataSize) on disk; hashing the padding
        // changes the digest table and then makes CNT+0x140 fail even when the source is valid.
        private void Seal(byte[] bytes, byte[] plainParam, SecondaryScProfile secondaryProfile, CancellationToken token, byte[] preservedSecondary)
        {
            BinaryData.Put(bytes, Find(0x80).Offset + 0xc0, PackageSdk.Sha3(plainParam, 0, plainParam.Length));
            CntEntry digests = Find(1);
            for (int i = 1; i < Entries.Count; i++)
            {
                token.ThrowIfCancellationRequested(); CntEntry e = Entries[i];
                BinaryData.Put(bytes, digests.Offset + i * 32, PackageSdk.Sha3(bytes, e.Offset, e.Size));
            }
            BinaryData.Put(bytes, 0x160, PackageSdk.Sha3(bytes, bodyOffset, bodySize));
            BinaryData.Put(bytes, 0x140, PackageSdk.Sha3(bytes, digests.Offset, digests.Size));
            List<CntEntry> controls = new List<CntEntry>(); controls.Add(Find(0x10));
            if (byId.ContainsKey(0x20)) controls.Add(Find(0x20));
            controls.Add(Find(0x80)); controls.Add(Find(0x100)); controls.Add(digests);
            using (MemoryStream concat = new MemoryStream())
            {
                for (int i = 0; i < controls.Count; i++)
                {
                    CntEntry e = controls[i]; Require(e.Offset, e.Size); concat.Write(bytes, e.Offset, e.Size);
                }
                byte[] data = concat.ToArray(); BinaryData.Put(bytes, 0x100, PackageSdk.Sha3(data, 0, data.Length));
            }
            if (preservedSecondary != null)
            {
                if (preservedSecondary.Length != 32) throw new InvalidDataException("Invalid preserved CNT+0x120 digest length.");
                BinaryData.Put(bytes, 0x120, preservedSecondary);
            }
            else BinaryData.Put(bytes, 0x120, SecondaryScHash(bytes, secondaryProfile));
            int keyOffset = checked((int)BinaryData.U32(bytes, 0x510, false)), keySize = checked((int)BinaryData.U32(bytes, 0x514, false));
            int mandatoryOffset = checked((int)BinaryData.U32(bytes, 0x518, false)), mandatorySize = checked((int)BinaryData.U32(bytes, 0x51c, false));
            if (keySize != 0 && mandatorySize != 0)
            {
                Require(keyOffset, keySize); Require(mandatoryOffset, mandatorySize);
                BinaryData.Put(bytes, 0x520, PackageSdk.Sha3(bytes, keyOffset, keySize));
                BinaryData.Put(bytes, 0x540, PackageSdk.Sha3(bytes, mandatoryOffset, mandatorySize));
            }
            BinaryData.Put(bytes, 0xfe0, PackageSdk.Sha3(bytes, 0, 0xfe0));
            byte[] wrap = PackageSdk.HeaderWrap(BinaryData.Slice(bytes, 0, 0x1000));
            if (wrap.Length != 0x180) throw new InvalidDataException("Unexpected CNT wrap length.");
            BinaryData.Put(bytes, 0x1000, wrap);
        }
    }

    internal static class FixedParam
    {
        internal static byte[] Retarget(byte[] bytes, ulong value)
        {
            PackageParam validate = new PackageParam(bytes);
            string text = new UTF8Encoding(false, true).GetString(bytes);
            foreach (string name in new string[] { "sdkVersion", "requiredSystemSoftwareVersion" })
            {
                string before = validate.Text(name); SdkVersion.Parse(before);
                if (before.Length != 18 || !before.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Fast mode requires existing 16-digit hexadecimal " + name + " fields.");
                int found = -1, depth = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '{' || text[i] == '[') depth++;
                    else if (text[i] == '}' || text[i] == ']') depth--;
                    else if (text[i] == '"')
                    {
                        int start = ++i;
                        while (i < text.Length && text[i] != '"') { if (text[i] == '\\') i++; i++; }
                        if (i >= text.Length) throw new InvalidDataException("Unterminated JSON string.");
                        if (depth != 1 || text.Substring(start, i - start) != name) continue;
                        int colon = i + 1; while (colon < text.Length && char.IsWhiteSpace(text[colon])) colon++;
                        if (colon >= text.Length || text[colon] != ':') continue;
                        int valueStart = colon + 1; while (valueStart < text.Length && char.IsWhiteSpace(text[valueStart])) valueStart++;
                        if (valueStart + 20 > text.Length || text[valueStart] != '"' || text[valueStart + 19] != '"' || text.Substring(valueStart + 1, 18) != before || found != -1)
                            throw new NotSupportedException("Ambiguous or escaped SDK field; fixed-size editing is unavailable.");
                        found = valueStart + 1;
                    }
                }
                if (found < 0) throw new NotSupportedException("A top-level SDK field could not be located.");
                text = text.Substring(0, found) + SdkVersion.Hex(value) + text.Substring(found + 18);
            }
            byte[] result = new UTF8Encoding(false).GetBytes(text);
            if (result.Length != bytes.Length) throw new InvalidDataException("Metadata length changed.");
            PackageParam after = new PackageParam(result);
            if (SdkVersion.Parse(after.Text("sdkVersion")) != value || SdkVersion.Parse(after.Text("requiredSystemSoftwareVersion")) != value) throw new InvalidDataException("SDK validation failed.");
            return result;
        }
    }
}
