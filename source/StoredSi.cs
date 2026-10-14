using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace FpkgExfat
{
    // The supplied publisher emits an uncompressed ZIP SI. Preserve its layout byte for byte.
    internal sealed class StoredSi
    {
        internal sealed class Member { internal string Name; internal int Local, Central, Data, Size; }
        internal int Start;
        internal readonly List<Member> Members = new List<Member>();

        internal static StoredSi Open(byte[] bytes)
        {
            int end = -1;
            for (int p = bytes.Length - 22; p >= Math.Max(0, bytes.Length - 65557); p--)
                if (BinaryData.U32(bytes, p, true) == 0x06054b50 && p + 22 + BinaryData.U16(bytes, p + 20, true) == bytes.Length) { end = p; break; }
            if (end < 0) return null;
            int count = BinaryData.U16(bytes, end + 10, true), centralSize = checked((int)BinaryData.U32(bytes, end + 12, true));
            int centralOffset = checked((int)BinaryData.U32(bytes, end + 16, true));
            StoredSi zip = new StoredSi { Start = checked(end - centralSize - centralOffset) };
            if (zip.Start < 0x1180 || count == 0 || count == 65535 || BinaryData.U16(bytes, end + 4, true) != 0 ||
                BinaryData.U16(bytes, end + 6, true) != 0 || BinaryData.U16(bytes, end + 8, true) != count)
                throw new NotSupportedException("Unsupported SI ZIP directory.");
            int c = zip.Start + centralOffset;
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                BinaryData.Range(bytes, c, 46);
                if (BinaryData.U32(bytes, c, true) != 0x02014b50) throw new InvalidDataException("Invalid SI central directory.");
                int nameLength = BinaryData.U16(bytes, c + 28, true), extra = BinaryData.U16(bytes, c + 30, true), comment = BinaryData.U16(bytes, c + 32, true);
                int local = checked(zip.Start + (int)BinaryData.U32(bytes, c + 42, true)), size = checked((int)BinaryData.U32(bytes, c + 24, true));
                int flags = BinaryData.U16(bytes, c + 8, true), method = BinaryData.U16(bytes, c + 10, true);
                if (method != 0 || (flags & ~0x800) != 0 || BinaryData.U32(bytes, c + 20, true) != size || BinaryData.U16(bytes, c + 34, true) != 0)
                    throw new NotSupportedException("Fast mode supports stored SI members without encryption or data descriptors.");
                string name = new UTF8Encoding(false, true).GetString(BinaryData.Slice(bytes, c + 46, nameLength));
                if (!names.Add(name)) throw new InvalidDataException("Duplicate SI member.");
                BinaryData.Range(bytes, local, 30);
                int data = checked(local + 30 + BinaryData.U16(bytes, local + 26, true) + BinaryData.U16(bytes, local + 28, true));
                if (local < zip.Start || data > zip.Start + centralOffset || size > zip.Start + centralOffset - data ||
                    BinaryData.U32(bytes, local, true) != 0x04034b50 || BinaryData.U16(bytes, local + 8, true) != 0 ||
                    BinaryData.U16(bytes, local + 6, true) != flags || BinaryData.U32(bytes, local + 18, true) != size || BinaryData.U32(bytes, local + 22, true) != size ||
                    !FileWorkspace.Equal(BinaryData.Slice(bytes, local + 30, BinaryData.U16(bytes, local + 26, true)), BinaryData.Slice(bytes, c + 46, nameLength)))
                    throw new InvalidDataException("SI local and central headers disagree.");
                uint crc = BinaryData.Crc(bytes, data, size, false);
                if (crc != BinaryData.U32(bytes, c + 16, true) || crc != BinaryData.U32(bytes, local + 14, true)) throw new InvalidDataException("An SI ZIP member has an invalid CRC.");
                zip.Members.Add(new Member { Name = name, Local = local, Central = c, Data = data, Size = size });
                c = checked(c + 46 + nameLength + extra + comment);
            }
            if (c != end) throw new InvalidDataException("Invalid SI directory size.");
            List<Member> ordered = new List<Member>(zip.Members); ordered.Sort(delegate(Member a, Member b) { return a.Local.CompareTo(b.Local); });
            for (int i = 1; i < ordered.Count; i++) if (ordered[i].Local < ordered[i - 1].Data + ordered[i - 1].Size) throw new InvalidDataException("Overlapping SI members.");
            return zip;
        }

        internal void Update(byte[] next, byte[] before, long cntOffset, string contentId, string oldSdk, string oldRequired, ulong sdk, int paramDigestOffset, CancellationToken token)
        {
            UpdateValues(next, before, cntOffset, contentId, oldSdk, oldRequired, SdkVersion.Hex(sdk), SdkVersion.Hex(sdk), paramDigestOffset, false, token);
        }

        internal void UpdateValues(byte[] next, byte[] before, long cntOffset, string contentId, string oldSdk, string oldRequired,
            string newSdk, string newRequired, int paramDigestOffset, bool repair, CancellationToken token)
        {
            bool hasXml = false, hasCrc = false;
            foreach (Member member in Members)
            {
                if (member.Name == "common/etc/pfsimage.xml") hasXml = true;
                if (member.Name == "config/" + contentId + "/playgo-chunk.crc") hasCrc = true;
            }
            if (!hasXml || !hasCrc) throw new NotSupportedException("The SI is missing the expected pfsimage.xml or PlayGo CRC table for this content ID.");
            foreach (Member entry in Members)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Name == "common/etc/pfsimage.xml")
                {
                    string xml = new UTF8Encoding(false, true).GetString(BinaryData.Slice(before, entry.Data, entry.Size));
                    ValidateXml(xml);
                    xml = ReplaceValue(xml, "sdkVersion", oldSdk, newSdk, false, false);
                    xml = ReplaceValue(xml, "requiredSystemSoftwareVersion", oldRequired, newRequired, false, false);
                    foreach (KeyValuePair<string, int> digest in new Dictionary<string, int> { { "body-digest", 0x160 }, { "param-digest", paramDigestOffset }, { "package-digest", 0xfe0 } })
                    {
                        string oldHex = BitConverter.ToString(BinaryData.Slice(before, digest.Value, 32)).Replace("-", "");
                        string newHex = BitConverter.ToString(BinaryData.Slice(next, digest.Value, 32)).Replace("-", "");
                        xml = ReplaceValue(xml, digest.Key, oldHex, newHex, true, repair);
                    }
                    byte[] changed = new UTF8Encoding(false).GetBytes(xml); ValidateXml(xml);
                    if (changed.Length != entry.Size) throw new NotSupportedException("The SI XML edit would change its stored size.");
                    BinaryData.Put(next, entry.Data, changed);
                }
                else if (entry.Name == "config/" + contentId + "/playgo-chunk.crc")
                {
                    long mountSize = checked(cntOffset + Start);
                    if (entry.Size != checked((mountSize + 65535) / 65536 * 4)) throw new InvalidDataException("The PlayGo CRC table has an unexpected size.");
                    // CNT starts on a 64 KiB boundary. All affected blocks are in this metadata buffer.
                    for (int offset = 0; offset < Start; offset += 65536)
                    {
                        token.ThrowIfCancellationRequested(); int length = Math.Min(65536, Start - offset); bool changed = false;
                        for (int i = 0; i < length; i++) if (before[offset + i] != next[offset + i]) { changed = true; break; }
                        if (!changed && !repair) continue;
                        int index = checked(entry.Data + (int)((cntOffset + offset) / 65536 * 4));
                        uint previous = BinaryData.U32(before, index, true), updated = BinaryData.Crc(next, offset, length, true);
                        if (previous != BinaryData.Crc(before, offset, length, true) && !(repair && previous == updated))
                            throw new InvalidDataException("The existing PlayGo checksum for a CNT block is invalid and cannot be reconciled with the validated metadata.");
                        BinaryData.Put32(next, index, updated, true);
                    }
                }
                uint crc = BinaryData.Crc(next, entry.Data, entry.Size, false);
                BinaryData.Put32(next, entry.Local + 14, crc, true); BinaryData.Put32(next, entry.Central + 16, crc, true);
            }
            Open(next); // Recheck every local header, directory entry and member CRC before any disk write.
        }

        private static void ValidateXml(string xml)
        {
            using (StringReader text = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = CntMetadata.MaxTail }))
                while (reader.Read()) { }
        }

        private static string ReplaceValue(string xml, string tag, string oldValue, string newValue, bool hex, bool acceptRepaired)
        {
            Regex pattern = new Regex("<" + tag + ">([^<]*)</" + tag + ">", RegexOptions.CultureInvariant);
            MatchCollection matches = pattern.Matches(xml);
            if (matches.Count != 1) throw new NotSupportedException("An SI field is missing or ambiguous: " + tag);
            Group group = matches[0].Groups[1]; string value = group.Value, replacement;
            if (hex)
            {
                MatchCollection octets = Regex.Matches(value, "0[xX]([0-9a-fA-F]{2})");
                if (octets.Count != 32 || Regex.Replace(value, "0[xX][0-9a-fA-F]{2}|\\s", "").Length != 0)
                    throw new NotSupportedException("Unsupported SI digest notation: " + tag);
                StringBuilder compact = new StringBuilder(), updated = new StringBuilder(value);
                foreach (Match octet in octets) compact.Append(octet.Groups[1].Value);
                if (!compact.ToString().Equals(oldValue, StringComparison.OrdinalIgnoreCase) && !(acceptRepaired && compact.ToString().Equals(newValue, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The SI " + tag + " does not match the CNT or its validated repair.");
                for (int i = 0; i < octets.Count; i++)
                { int at = octets[i].Groups[1].Index; updated[at] = char.ToLowerInvariant(newValue[i * 2]); updated[at + 1] = char.ToLowerInvariant(newValue[i * 2 + 1]); }
                replacement = updated.ToString();
            }
            else
            {
                string compact = value.Trim();
                if (!compact.Equals(oldValue, StringComparison.OrdinalIgnoreCase) || compact.Length != newValue.Length) throw new NotSupportedException("The SI " + tag + " is not a matching fixed-width value.");
                int at = value.IndexOf(compact, StringComparison.Ordinal); replacement = value.Substring(0, at) + newValue + value.Substring(at + compact.Length);
            }
            return xml.Substring(0, group.Index) + replacement + xml.Substring(group.Index + group.Length);
        }
    }
}
