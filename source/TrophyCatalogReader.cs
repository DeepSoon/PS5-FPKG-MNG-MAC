using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FpkgExfat
{
    internal static class TrophyCatalogReader
    {
        private static readonly Regex NpwrRegex = new Regex(@"NPWR\d{5}_\d{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HomeRegex = new Regex(@"^[0-9a-fA-F]{8}$", RegexOptions.CultureInvariant);
        private static readonly Regex MountedTitleRegex = new Regex(@"^(PPSA\d{5})_\d{3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static TrophyListResult Read(string host, string ppsa, Action<string> log)
        {
            string npwr = ResolveNpwr(host, ppsa, log);
            byte[] ucp;
            string ucpSource;
            DownloadTrophyUcp(host, ppsa, npwr, out ucp, out ucpSource);

            CatalogMetadata metadata = ReadMetadata(ucp);
            Dictionary<int, TrophyConfig> config = ReadConfig(ucp);
            if (metadata.Trophies.Count == 0 || config.Count == 0)
                throw new InvalidDataException("The Trophy2 catalog does not contain readable trophies.");

            foreach (int id in metadata.Trophies.Keys)
                if (!config.ContainsKey(id))
                    throw new InvalidDataException("Trophy2 metadata and grade catalog do not cover the same trophy IDs.");
            foreach (int id in config.Keys)
                if (!metadata.Trophies.ContainsKey(id))
                    throw new InvalidDataException("Trophy2 metadata and grade catalog do not cover the same trophy IDs.");

            string home;
            HashSet<int> unlocked;
            int stateCount;
            bool stateKnown = TryReadState(host, npwr, out home, out unlocked, out stateCount, log);

            TrophyListResult result = new TrophyListResult();
            result.Ppsa = ppsa;
            result.Npwr = npwr;
            result.GameTitle = metadata.TitleName.Length > 0 ? metadata.TitleName : ppsa;
            result.HomeId = home ?? string.Empty;
            result.CatalogSource = ucpSource;
            result.StateKnown = stateKnown;

            List<int> ids = new List<int>(metadata.Trophies.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                TrophyText text = metadata.Trophies[id];
                TrophyConfig cfg = config[id];
                TrophyItem item = new TrophyItem();
                item.Id = id;
                item.Name = text.Name.Length > 0 ? text.Name : "Trophy ID " + id.ToString(CultureInfo.InvariantCulture);
                item.Detail = text.Detail;
                item.Grade = cfg.Grade;
                item.GroupId = cfg.GroupId;
                string groupName;
                if (!metadata.Groups.TryGetValue(cfg.GroupId, out groupName) || string.IsNullOrWhiteSpace(groupName))
                    groupName = cfg.GroupId == "0000" ? "Main Game" : "Group " + cfg.GroupId;
                item.GroupName = groupName;
                item.StateKnown = stateKnown;
                item.Unlocked = stateKnown && unlocked.Contains(id);
                result.Trophies.Add(item);
            }

            result.Total = result.Trophies.Count;
            result.UnlockedCount = 0;
            foreach (TrophyItem item in result.Trophies) if (item.Unlocked) result.UnlockedCount++;

            if (stateKnown && stateCount > 0 && stateCount != result.Total && log != null)
                log("Trophy state count is " + stateCount + " while catalog count is " + result.Total + ". Using catalog IDs for the list.");
            if (log != null)
            {
                log("Trophy catalog: " + result.GameTitle + " · " + result.Total + " trophies · " + npwr);
                if (stateKnown) log("Trophy state: " + result.UnlockedCount + "/" + result.Total + " unlocked.");
                else log("Trophy state is not initialized yet; catalog loaded with Unknown status.");
            }
            return result;
        }

        internal static TrophyListResult RefreshState(string host, TrophyListResult catalog, Action<string> log)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            string home;
            HashSet<int> unlocked;
            int stateCount;
            bool known = TryReadState(host, catalog.Npwr, out home, out unlocked, out stateCount, null);

            TrophyListResult result = new TrophyListResult();
            result.Ppsa = catalog.Ppsa;
            result.Npwr = catalog.Npwr;
            result.GameTitle = catalog.GameTitle;
            result.HomeId = home ?? catalog.HomeId;
            result.CatalogSource = catalog.CatalogSource;
            result.Total = catalog.Total;
            result.StateKnown = known;
            result.UsedUdsRoute = catalog.UsedUdsRoute;
            result.CommitPending = catalog.CommitPending;

            foreach (TrophyItem original in catalog.Trophies)
            {
                TrophyItem item = new TrophyItem();
                item.Id = original.Id;
                item.Name = original.Name;
                item.Detail = original.Detail;
                item.Grade = original.Grade;
                item.GroupId = original.GroupId;
                item.GroupName = original.GroupName;
                item.StateKnown = known;
                item.Unlocked = known && unlocked.Contains(item.Id);
                result.Trophies.Add(item);
                if (item.Unlocked) result.UnlockedCount++;
            }

            if (log != null)
            {
                if (known) log("Trophy state refreshed: " + result.UnlockedCount + "/" + result.Total + " unlocked.");
                else log("Trophy state is not available yet.");
            }
            return result;
        }

        internal static string ResolveNpwr(string host, string ppsa, Action<string> log)
        {
            string[] candidates = new string[]
            {
                "/system_data/priv/appmeta/" + ppsa + "/trophy2/npbind.dat",
                "/system_data/priv/appmeta/" + ppsa + "/npbind.dat",
                "/system_data/priv/appmeta/" + ppsa + "/trophy/npbind.dat",
                "/system_data/priv/appmeta/" + ppsa + "/uds/npbind.dat",
                "/mnt/sandbox/pfsmnt/" + ppsa + "-app0-patch0-union/sce_sys/trophy2/npbind.dat",
                "/mnt/sandbox/pfsmnt/" + ppsa + "-app0/sce_sys/trophy2/npbind.dat"
            };

            foreach (string path in candidates)
            {
                try
                {
                    string npwr = ExtractNpwr(TrophyFtp.Download(host, path));
                    if (npwr.Length > 0)
                    {
                        if (log != null) log("Trophy set detected: " + npwr);
                        return npwr;
                    }
                }
                catch { }
            }

            try
            {
                string[] mounts = TrophyFtp.ListNames(host, "/mnt/sandbox/");
                foreach (string mount in mounts)
                {
                    Match m = MountedTitleRegex.Match(mount.ToUpperInvariant());
                    if (!m.Success || !string.Equals(m.Groups[1].Value, ppsa, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        string npwr = ExtractNpwr(TrophyFtp.Download(host, "/mnt/sandbox/" + mount + "/app0/sce_sys/trophy2/npbind.dat"));
                        if (npwr.Length > 0)
                        {
                            if (log != null) log("Trophy set detected: " + npwr);
                            return npwr;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            throw new InvalidOperationException("Unable to resolve the Trophy2 NPWR for " + ppsa + ". Keep the game running and make sure FTP is available on port 2121.");
        }

        private static string ExtractNpwr(byte[] data)
        {
            string text = Encoding.ASCII.GetString(data ?? new byte[0]);
            MatchCollection matches = NpwrRegex.Matches(text);
            string found = string.Empty;
            foreach (Match match in matches)
            {
                string value = match.Value.ToUpperInvariant();
                if (found.Length == 0) found = value;
                else if (!string.Equals(found, value, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            }
            return found;
        }

        private static void DownloadTrophyUcp(string host, string ppsa, string npwr, out byte[] data, out string source)
        {
            List<string> candidates = new List<string>();
            candidates.Add("/user/trophy2/nobackup/conf/" + npwr + "/TROPHY.UCP");
            candidates.Add("/mnt/sandbox/pfsmnt/" + ppsa + "-app0-patch0-union/sce_sys/trophy2/trophy00.ucp");
            candidates.Add("/mnt/sandbox/pfsmnt/" + ppsa + "-app0/sce_sys/trophy2/trophy00.ucp");

            try
            {
                string[] mounts = TrophyFtp.ListNames(host, "/mnt/sandbox/");
                foreach (string mount in mounts)
                {
                    Match match = MountedTitleRegex.Match(mount.ToUpperInvariant());
                    if (!match.Success || !string.Equals(match.Groups[1].Value, ppsa, StringComparison.OrdinalIgnoreCase)) continue;
                    candidates.Add("/mnt/sandbox/" + mount + "/app0/sce_sys/trophy2/trophy00.ucp");
                }
            }
            catch { }

            foreach (string path in candidates)
            {
                try
                {
                    byte[] blob = TrophyFtp.Download(host, path);
                    if (blob != null && blob.Length > 0)
                    {
                        data = blob;
                        source = path;
                        return;
                    }
                }
                catch { }
            }
            throw new InvalidOperationException("Unable to read the Trophy2 catalog for " + npwr + ". Keep the game running so trophy00.ucp is mounted.");
        }

        private static CatalogMetadata ReadMetadata(byte[] ucp)
        {
            string[] locales = new string[] { "en-US", "fr-FR", "fr-CA" };
            Exception last = null;
            foreach (string locale in locales)
            {
                try
                {
                    Dictionary<string, object> root = JsonLite.AsObject(ParseUcpJson(ucp, "tropmeta_" + locale + ".json"));
                    Dictionary<string, object> meta = JsonLite.AsObject(Get(root, "metadata"));
                    List<object> entries = JsonLite.AsArray(Get(meta, "trophyMetadata"));
                    if (entries.Count == 0) throw new InvalidDataException("trophyMetadata is empty");

                    CatalogMetadata result = new CatalogMetadata();
                    foreach (object raw in entries)
                    {
                        Dictionary<string, object> item = JsonLite.AsObject(raw);
                        int id = JsonLite.AsInt(Get(item, "id"));
                        TrophyText text = new TrophyText();
                        text.Name = LocalizedText(GetOptional(item, "name"), locale);
                        text.Detail = LocalizedText(GetOptional(item, "detail"), locale);
                        if (text.Name.Length == 0) text.Name = "Trophy ID " + id.ToString(CultureInfo.InvariantCulture);
                        result.Trophies[id] = text;
                    }

                    object groupsRaw = GetOptional(meta, "groupMetadata");
                    if (groupsRaw != null)
                    {
                        foreach (object raw in JsonLite.AsArray(groupsRaw))
                        {
                            Dictionary<string, object> group = JsonLite.AsObject(raw);
                            string id = JsonLite.AsString(GetOptional(group, "id")).Trim();
                            string name = LocalizedText(GetOptional(group, "name"), locale);
                            if (id.Length > 0 && name.Length > 0) result.Groups[id] = name;
                        }
                    }

                    object titleRaw = GetOptional(meta, "titleMetadata");
                    Dictionary<string, object> titleObject = titleRaw as Dictionary<string, object>;
                    if (titleObject != null) result.TitleName = LocalizedText(GetOptional(titleObject, "name"), locale);
                    else result.TitleName = LocalizedText(titleRaw, locale);
                    return result;
                }
                catch (Exception error) { last = error; }
            }
            throw new InvalidDataException("Unable to parse Trophy2 metadata.", last);
        }

        private static Dictionary<int, TrophyConfig> ReadConfig(byte[] ucp)
        {
            Dictionary<string, object> root = JsonLite.AsObject(ParseUcpJson(ucp, "tropconf.json"));
            List<object> entries = JsonLite.AsArray(Get(root, "trophies"));
            Dictionary<int, TrophyConfig> result = new Dictionary<int, TrophyConfig>();
            foreach (object raw in entries)
            {
                Dictionary<string, object> item = JsonLite.AsObject(raw);
                int id = JsonLite.AsInt(Get(item, "id"));
                string grade = JsonLite.AsString(GetOptional(item, "grade")).Trim().ToUpperInvariant();
                if (grade != "P" && grade != "G" && grade != "S" && grade != "B") grade = string.Empty;
                TrophyConfig cfg = new TrophyConfig();
                cfg.Grade = grade;
                cfg.GroupId = JsonLite.AsString(GetOptional(item, "groupId")).Trim();
                if (cfg.GroupId.Length == 0) cfg.GroupId = "0000";
                result[id] = cfg;
            }
            return result;
        }

        private static object ParseUcpJson(byte[] data, string filename)
        {
            byte[] marker = Encoding.ASCII.GetBytes(filename + "\0");
            int entry = IndexOf(data, marker);
            if (entry < 0) throw new InvalidDataException(filename + " is missing from the Trophy2 UCP.");
            int metadata = entry + 0x20;
            if (metadata + 16 > data.Length) throw new InvalidDataException(filename + " entry is truncated.");
            ulong offset = ReadBe64(data, metadata);
            ulong size = ReadBe64(data, metadata + 8);
            if (offset == 0 || size == 0 || offset > (ulong)data.Length || size > (ulong)data.Length || offset + size > (ulong)data.Length)
                throw new InvalidDataException(filename + " has an invalid UCP range.");
            string json = Encoding.UTF8.GetString(data, (int)offset, (int)size);
            return JsonLite.Parse(json);
        }

        private static bool TryReadState(string host, string npwr, out string home, out HashSet<int> unlocked, out int trophyCount, Action<string> log)
        {
            home = string.Empty;
            unlocked = new HashSet<int>();
            trophyCount = 0;
            string[] homes;
            try { homes = TrophyFtp.ListNames(host, "/user/home/"); }
            catch { return false; }

            foreach (string candidate in homes)
            {
                if (!HomeRegex.IsMatch(candidate)) continue;
                string path = "/user/home/" + candidate + "/trophy2/nobackup/data/" + npwr + "/TRPTITLE.DAT";
                try
                {
                    byte[] blob = TrophyFtp.Download(host, path);
                    int count;
                    HashSet<int> ids = ParseUnlockedState(blob, out count);
                    home = candidate;
                    unlocked = ids;
                    trophyCount = count;
                    if (log != null) log("Trophy state loaded from profile " + candidate + ".");
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static HashSet<int> ParseUnlockedState(byte[] data, out int trophyCount)
        {
            if (data == null || data.Length < 0x180) throw new InvalidDataException("TRPTITLE.DAT is too short.");
            if (!BytesEqual(data, 0, Encoding.ASCII.GetBytes("T2PD")) || !BytesEqual(data, 0x40, Encoding.ASCII.GetBytes("T2TD")))
                throw new InvalidDataException("TRPTITLE.DAT signatures are invalid.");
            uint countA = ReadBe32(data, 0x11C);
            uint countB = ReadBe32(data, 0x17C);
            int count = 0;
            if (countA > 0 && countA <= 128) count = (int)countA;
            if (countB > 0 && countB <= 128)
            {
                if (count != 0 && count != (int)countB) throw new InvalidDataException("TRPTITLE.DAT trophy counters disagree.");
                count = (int)countB;
            }
            if (count <= 0) throw new InvalidDataException("TRPTITLE.DAT trophy count is invalid.");
            trophyCount = count;

            List<byte> logical = new List<byte>();
            int pageBase = 0x20;
            const int pageHeader = 0x20;
            const int pagePayload = 0x400;
            const int pageStride = pageHeader + pagePayload;
            int pages = 0;
            using (SHA256 sha = SHA256.Create())
            {
                while (pageBase + pageStride + pageHeader <= data.Length)
                {
                    int start = pageBase + pageHeader;
                    int end = pageBase + pageStride;
                    byte[] digest = sha.ComputeHash(data, start, pagePayload);
                    if (!BytesEqual(data, end, digest)) throw new InvalidDataException("TRPTITLE.DAT page digest is invalid.");
                    for (int i = start; i < end; i++) logical.Add(data[i]);
                    pages++;
                    pageBase += pageStride;
                }
            }
            if (pages == 0) throw new InvalidDataException("TRPTITLE.DAT contains no validated Trophy2 pages.");
            if (pageBase + pageHeader != data.Length) throw new InvalidDataException("TRPTITLE.DAT page chain has an unexpected length.");

            byte[] stream = logical.ToArray();
            HashSet<int> unlocked = new HashSet<int>();
            for (int offset = 0; offset + 56 <= stream.Length; offset += 4)
            {
                uint id = ReadBe32(stream, offset);
                if (id >= count) continue;
                uint state = ReadBe32(stream, offset + 4);
                uint flags = ReadBe32(stream, offset + 8);
                uint reserved0 = ReadBe32(stream, offset + 12);
                if (state != 0x11 || flags != 0x2000 || reserved0 != 0) continue;
                ulong tick1 = ReadBe64(stream, offset + 16);
                ulong reserved1 = ReadBe64(stream, offset + 24);
                ulong tick2 = ReadBe64(stream, offset + 32);
                ulong reserved2 = ReadBe64(stream, offset + 40);
                ulong tail = ReadBe64(stream, offset + 48);
                if (reserved1 != 0 || reserved2 != 0) continue;
                if (tail != 0x5500000000000008UL && tail != 0UL) continue;
                if (tick1 == 0 || tick1 != tick2) continue;
                if (!unlocked.Add((int)id)) throw new InvalidDataException("TRPTITLE.DAT contains a duplicate unlocked trophy record.");
            }
            return unlocked;
        }

        private static string LocalizedText(object value, string locale)
        {
            if (value == null) return string.Empty;
            string text = value as string;
            if (text != null) return RepairText(text.Trim());
            List<object> list = value as List<object>;
            if (list != null)
            {
                foreach (object item in list)
                {
                    string candidate = LocalizedText(item, locale);
                    if (candidate.Length > 0) return candidate;
                }
                return string.Empty;
            }
            Dictionary<string, object> dict = value as Dictionary<string, object>;
            if (dict == null) return string.Empty;
            string[] preferred = new string[] { locale, "en-US", "fr-FR", "fr-CA", "default", "value", "text" };
            foreach (string key in preferred)
            {
                object nested;
                if (dict.TryGetValue(key, out nested))
                {
                    string candidate = LocalizedText(nested, locale);
                    if (candidate.Length > 0) return candidate;
                }
            }
            foreach (KeyValuePair<string, object> pair in dict)
            {
                string candidate = LocalizedText(pair.Value, locale);
                if (candidate.Length > 0) return candidate;
            }
            return string.Empty;
        }

        private static string RepairText(string text)
        {
            for (int i = 0; i < 2; i++)
            {
                if (text.IndexOf('\u00C3') < 0 && text.IndexOf('\u00C2') < 0) break;
                try
                {
                    byte[] bytes = new byte[text.Length];
                    bool validLatin1 = true;
                    for (int j = 0; j < text.Length; j++)
                    {
                        if (text[j] > 255) { validLatin1 = false; break; }
                        bytes[j] = (byte)text[j];
                    }
                    if (!validLatin1) break;
                    string repaired = Encoding.UTF8.GetString(bytes);
                    if (repaired == text) break;
                    text = repaired;
                }
                catch { break; }
            }
            return text.IndexOf('\uFFFD') >= 0 ? string.Empty : text;
        }

        private static object Get(Dictionary<string, object> dict, string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value)) throw new InvalidDataException("Missing JSON key: " + key);
            return value;
        }

        private static object GetOptional(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool same = true;
                for (int j = 0; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { same = false; break; }
                if (same) return i;
            }
            return -1;
        }

        private static bool BytesEqual(byte[] data, int offset, byte[] expected)
        {
            if (offset < 0 || expected == null || offset + expected.Length > data.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (data[offset + i] != expected[i]) return false;
            return true;
        }

        private static uint ReadBe32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static ulong ReadBe64(byte[] data, int offset)
        {
            return ((ulong)ReadBe32(data, offset) << 32) | ReadBe32(data, offset + 4);
        }

        private sealed class TrophyText { internal string Name = string.Empty; internal string Detail = string.Empty; }
        private sealed class TrophyConfig { internal string Grade = string.Empty; internal string GroupId = "0000"; }
        private sealed class CatalogMetadata
        {
            internal string TitleName = string.Empty;
            internal readonly Dictionary<int, TrophyText> Trophies = new Dictionary<int, TrophyText>();
            internal readonly Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static class JsonLite
        {
            internal static object Parse(string json) { return new Parser(json ?? string.Empty).ParseValue(); }
            internal static Dictionary<string, object> AsObject(object value)
            {
                Dictionary<string, object> dict = value as Dictionary<string, object>;
                if (dict == null) throw new InvalidDataException("Expected a JSON object.");
                return dict;
            }
            internal static List<object> AsArray(object value)
            {
                List<object> list = value as List<object>;
                if (list == null) throw new InvalidDataException("Expected a JSON array.");
                return list;
            }
            internal static string AsString(object value)
            {
                if (value == null) return string.Empty;
                string text = value as string;
                if (text != null) return text;
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            internal static int AsInt(object value)
            {
                if (value is long) return checked((int)(long)value);
                if (value is int) return (int)value;
                if (value is double) return checked((int)(double)value);
                int parsed;
                if (int.TryParse(AsString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
                throw new InvalidDataException("Expected an integer JSON value.");
            }

            private sealed class Parser
            {
                private readonly string text;
                private int index;
                internal Parser(string text) { this.text = text; }

                internal object ParseValue()
                {
                    SkipSpace();
                    if (index >= text.Length) throw new InvalidDataException("Unexpected end of JSON.");
                    char c = text[index];
                    if (c == '{') return ParseObject();
                    if (c == '[') return ParseArray();
                    if (c == '"') return ParseString();
                    if (c == 't') { Expect("true"); return true; }
                    if (c == 'f') { Expect("false"); return false; }
                    if (c == 'n') { Expect("null"); return null; }
                    return ParseNumber();
                }

                private Dictionary<string, object> ParseObject()
                {
                    Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                    index++;
                    SkipSpace();
                    if (Peek('}')) { index++; return result; }
                    while (true)
                    {
                        SkipSpace();
                        string key = ParseString();
                        SkipSpace();
                        Require(':');
                        result[key] = ParseValue();
                        SkipSpace();
                        if (Peek('}')) { index++; break; }
                        Require(',');
                    }
                    return result;
                }

                private List<object> ParseArray()
                {
                    List<object> result = new List<object>();
                    index++;
                    SkipSpace();
                    if (Peek(']')) { index++; return result; }
                    while (true)
                    {
                        result.Add(ParseValue());
                        SkipSpace();
                        if (Peek(']')) { index++; break; }
                        Require(',');
                    }
                    return result;
                }

                private string ParseString()
                {
                    Require('"');
                    StringBuilder result = new StringBuilder();
                    while (index < text.Length)
                    {
                        char c = text[index++];
                        if (c == '"') return result.ToString();
                        if (c != '\\') { result.Append(c); continue; }
                        if (index >= text.Length) throw new InvalidDataException("Invalid JSON escape.");
                        char e = text[index++];
                        switch (e)
                        {
                            case '"': result.Append('"'); break;
                            case '\\': result.Append('\\'); break;
                            case '/': result.Append('/'); break;
                            case 'b': result.Append('\b'); break;
                            case 'f': result.Append('\f'); break;
                            case 'n': result.Append('\n'); break;
                            case 'r': result.Append('\r'); break;
                            case 't': result.Append('\t'); break;
                            case 'u':
                                if (index + 4 > text.Length) throw new InvalidDataException("Invalid JSON unicode escape.");
                                int code;
                                if (!int.TryParse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    throw new InvalidDataException("Invalid JSON unicode escape.");
                                result.Append((char)code); index += 4; break;
                            default: throw new InvalidDataException("Invalid JSON escape.");
                        }
                    }
                    throw new InvalidDataException("Unterminated JSON string.");
                }

                private object ParseNumber()
                {
                    int start = index;
                    if (Peek('-')) index++;
                    while (index < text.Length && char.IsDigit(text[index])) index++;
                    bool floating = false;
                    if (Peek('.')) { floating = true; index++; while (index < text.Length && char.IsDigit(text[index])) index++; }
                    if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                    {
                        floating = true; index++;
                        if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++;
                        while (index < text.Length && char.IsDigit(text[index])) index++;
                    }
                    string raw = text.Substring(start, index - start);
                    if (!floating)
                    {
                        long integral;
                        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integral)) return integral;
                    }
                    double number;
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
                    throw new InvalidDataException("Invalid JSON number.");
                }

                private void Expect(string expected)
                {
                    if (index + expected.Length > text.Length || string.CompareOrdinal(text, index, expected, 0, expected.Length) != 0)
                        throw new InvalidDataException("Invalid JSON token.");
                    index += expected.Length;
                }
                private void Require(char c)
                {
                    SkipSpace();
                    if (index >= text.Length || text[index] != c) throw new InvalidDataException("Expected JSON character '" + c + "'.");
                    index++;
                }
                private bool Peek(char c) { return index < text.Length && text[index] == c; }
                private void SkipSpace() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
            }
        }
    }
}
