using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class TrophyUnlockerEngine
    {
        private const int LoaderPort = 9021;
        private const string StandardPayloadResource = "FpkgExfat.TrophyUnlocker.Ps5R10";
        private const string UdsPayloadResource = "FpkgExfat.TrophyUnlocker.Ps5UdsV12";
        private static byte[] standardPayloadCache;
        private static byte[] udsPayloadCache;
        private readonly object sessionSync = new object();
        private string preparedHost = string.Empty;
        private string preparedNpwr = string.Empty;
        private int preparedTotal = -1;
        private static readonly Regex PpsaRegex = new Regex(@"PPSA\d{5}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal string DetectActiveGame(string host, Action<string> log)
        {
            ValidateHost(host);
            ResetSession();
            if (log != null) log("Connecting to PS5 FTP on port 2121...");

            List<GameCandidate> candidates = new List<GameCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidates(host, "/mnt/sandbox/pfsmnt/", candidates, seen);
            AddCandidates(host, "/mnt/sandbox/", candidates, seen);

            if (candidates.Count == 0)
                throw new InvalidOperationException("No active PS5 game detected. Launch a PS5 game and make sure FTP is running on port 2121.");

            candidates.Sort(delegate(GameCandidate a, GameCandidate b)
            {
                int score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : string.Compare(a.Ppsa, b.Ppsa, StringComparison.OrdinalIgnoreCase);
            });
            GameCandidate best = candidates[0];
            if (log != null) log("Active game detected: " + best.Ppsa);
            return best.Ppsa;
        }

        internal void ResetSession()
        {
            lock (sessionSync)
            {
                preparedHost = string.Empty;
                preparedNpwr = string.Empty;
                preparedTotal = -1;
            }
        }

        internal TrophyListResult GetTrophyList(string host, string ppsa, Action<string> log)
        {
            ValidateHost(host);
            ValidatePpsa(ppsa);
            if (log != null) log("Reading Trophy2 catalog for " + ppsa + "...");
            return TrophyCatalogReader.Read(host, ppsa, log);
        }

        internal TrophyListResult RefreshTrophyState(string host, TrophyListResult catalog, Action<string> log)
        {
            ValidateHost(host);
            if (catalog == null) throw new ArgumentNullException("catalog");
            return TrophyCatalogReader.RefreshState(host, catalog, log);
        }

        internal TrophyListResult UnlockSingle(string host, TrophyListResult catalog, int trophyId, Action<string> log)
        {
            ValidateHost(host);
            if (catalog == null) throw new ArgumentNullException("catalog");
            TrophyItem selected = FindById(catalog, trophyId);
            if (selected == null) throw new ArgumentOutOfRangeException("trophyId", "The selected trophy is not present in the current catalog.");
            if (selected.Unlocked)
            {
                if (log != null) log("Trophy " + trophyId + " is already unlocked.");
                return catalog;
            }

            string config = "mode=id\nid=" + trophyId.ToString(CultureInfo.InvariantCulture) + "\n";
            if (log != null) log("Unlocking trophy " + trophyId + ": " + selected.Name);
            SendCommand(host, catalog, config, false, "standard", log);
            TrophyListResult state = WaitForStateFast(host, catalog, trophyId, true, 2600, log);
            if (IsUnlocked(state, trophyId)) return state;

            if (log != null) log("Standard route did not confirm the unlock. Trying UDS V12 recovery...");
            SendCommand(host, state, config, true, "UDS V12", log);
            state.UsedUdsRoute = true;
            state = WaitForStateFast(host, state, trophyId, true, 4600, log);
            state.UsedUdsRoute = true;
            if (IsUnlocked(state, trophyId)) return state;

            state.CommitPending = IsTitleMounted(host, state.Ppsa);
            LogPayloadDiagnostic(host, log);
            if (log != null)
            {
                if (state.CommitPending)
                    log("UDS V12 was sent, but the game is still running. This title may commit Trophy2 only after the game exits.");
                else
                    log("Trophy " + trophyId + " is still reported as locked after both routes.");
            }
            return state;
        }

        internal TrophyListResult UnlockAll(string host, TrophyListResult catalog, Action<string> log)
        {
            ValidateHost(host);
            if (catalog == null) throw new ArgumentNullException("catalog");
            TrophyListResult state = catalog;
            state.CommitPending = false;
            List<TrophyItem> initialLocked = GetLocked(state);
            if (initialLocked.Count == 0)
            {
                if (log != null) log("Unlock All: every listed trophy is already unlocked. Nothing to send.");
                return state;
            }

            List<TrophyItem> normal = GetLockedNonPlatinum(state);
            if (normal.Count > 0)
            {
                if (log != null) log("Unlock All: standard exact-ID pass for " + normal.Count + " locked non-Platinum trophies...");
                foreach (string config in BuildExactListConfigs(normal))
                {
                    SendCommand(host, state, config, false, "standard", log);
                    state = WaitForAllUnlockedFast(host, state, 3200, null);
                    if (GetLockedNonPlatinum(state).Count == 0) break;
                }
            }

            if (GetLocked(state).Count == 0)
            {
                if (log != null) log("Unlock All complete. Stopping immediately.");
                return state;
            }

            List<TrophyItem> remainingNormal = GetLockedNonPlatinum(state);
            if (remainingNormal.Count > 0)
            {
                if (log != null) log("Standard route left " + remainingNormal.Count + " non-Platinum trophies locked. Starting UDS V12 exact-ID recovery...");
                foreach (string config in BuildExactListConfigs(remainingNormal))
                {
                    SendCommand(host, state, config, true, "UDS V12", log);
                    state.UsedUdsRoute = true;
                    state = WaitForAllUnlockedFast(host, state, 5200, null);
                    state.UsedUdsRoute = true;
                    if (GetLockedNonPlatinum(state).Count == 0) break;
                }
            }

            // Platinum is deliberately handled only after all currently detectable non-Platinum IDs.
            List<TrophyItem> platinum = GetLockedPlatinum(state);
            if (GetLockedNonPlatinum(state).Count == 0 && platinum.Count > 0)
            {
                if (log != null) log("Unlock All: non-Platinum pass complete. Handling Platinum last...");
                foreach (string config in BuildExactListConfigs(platinum))
                {
                    SendCommand(host, state, config, false, "standard Platinum", log);
                    state = WaitForAllUnlockedFast(host, state, 2400, null);
                    if (GetLockedPlatinum(state).Count == 0) break;
                }
                platinum = GetLockedPlatinum(state);
                if (platinum.Count > 0)
                {
                    foreach (string config in BuildExactListConfigs(platinum))
                    {
                        SendCommand(host, state, config, true, "UDS V12 Platinum", log);
                        state.UsedUdsRoute = true;
                        state = WaitForAllUnlockedFast(host, state, 4200, null);
                        state.UsedUdsRoute = true;
                        if (GetLockedPlatinum(state).Count == 0) break;
                    }
                }
            }

            try { state = TrophyCatalogReader.RefreshState(host, state, null); } catch { }
            int locked = GetLocked(state).Count;
            if (locked == 0)
            {
                state.CommitPending = false;
                if (log != null) log("Unlock All complete. Stopping immediately.");
                return state;
            }

            if (state.UsedUdsRoute && IsTitleMounted(host, state.Ppsa))
            {
                state.CommitPending = true;
                if (log != null)
                {
                    log("UDS V12 commands were accepted but " + locked + " trophies are still reported as locked while the game is running.");
                    log("This title may require the game to exit before Trophy2 flushes the acquisitions to TRPTITLE.DAT.");
                }
            }
            else if (log != null)
            {
                log("Unlock All finished the available routes; " + locked + " catalog trophy/trophies are still reported as locked.");
            }
            LogPayloadDiagnostic(host, log);
            return state;
        }

        internal TrophyListResult CompletePendingCommit(string host, TrophyListResult catalog, int waitForCloseMs, Action<string> log)
        {
            ValidateHost(host);
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (!catalog.CommitPending)
                return TrophyCatalogReader.RefreshState(host, catalog, log);

            if (log != null) log("Waiting for the active game to close so Trophy2 can flush its state...");
            DateTime closeDeadline = DateTime.UtcNow.AddMilliseconds(Math.Max(3000, waitForCloseMs));
            while (DateTime.UtcNow < closeDeadline)
            {
                if (!IsTitleMounted(host, catalog.Ppsa)) break;
                Thread.Sleep(450);
            }
            if (IsTitleMounted(host, catalog.Ppsa))
            {
                if (log != null) log("The game is still active. Trophy2 commit verification was cancelled.");
                return catalog;
            }

            if (log != null) log("Game closed. Verifying Trophy2 commit...");
            TrophyListResult state = catalog;
            DateTime stateDeadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < stateDeadline)
            {
                try
                {
                    state = TrophyCatalogReader.RefreshState(host, state, null);
                    state.UsedUdsRoute = true;
                    if (GetLocked(state).Count == 0)
                    {
                        state.CommitPending = false;
                        if (log != null) log("Trophy2 commit complete: " + state.UnlockedCount + "/" + state.Total + " unlocked.");
                        return state;
                    }
                }
                catch { }
                Thread.Sleep(500);
            }
            try { state = TrophyCatalogReader.RefreshState(host, state, null); } catch { }
            state.UsedUdsRoute = true;
            state.CommitPending = false;
            if (log != null) log("Trophy2 commit check finished: " + state.UnlockedCount + "/" + state.Total + " unlocked.");
            LogPayloadDiagnostic(host, log);
            return state;
        }

        private static TrophyListResult WaitForStateFast(string host, TrophyListResult catalog, int id, bool expectedUnlocked, int timeoutMs, Action<string> log)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            TrophyListResult latest = catalog;
            Thread.Sleep(220);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    latest = TrophyCatalogReader.RefreshState(host, latest, null);
                    TrophyItem item = FindById(latest, id);
                    if (item != null && item.StateKnown && item.Unlocked == expectedUnlocked)
                    {
                        if (log != null) log("Trophy " + id + " verified as " + (item.Unlocked ? "Unlocked" : "Locked") + ".");
                        return latest;
                    }
                }
                catch { }
                Thread.Sleep(280);
            }
            try { latest = TrophyCatalogReader.RefreshState(host, latest, null); } catch { }
            TrophyItem finalItem = FindById(latest, id);
            if (finalItem != null && finalItem.StateKnown && finalItem.Unlocked == expectedUnlocked)
            {
                if (log != null) log("Trophy " + id + " verified as Unlocked.");
                return latest;
            }
            if (log != null) log("Trophy " + id + " was not confirmed before the fast timeout.");
            return latest;
        }

        private static TrophyListResult WaitForAllUnlockedFast(string host, TrophyListResult catalog, int timeoutMs, Action<string> log)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            TrophyListResult latest = catalog;
            Thread.Sleep(250);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    latest = TrophyCatalogReader.RefreshState(host, latest, null);
                    if (latest.StateKnown && GetLocked(latest).Count == 0)
                    {
                        if (log != null) log("Unlock All state: " + latest.UnlockedCount + "/" + latest.Total + " unlocked.");
                        return latest;
                    }
                }
                catch { }
                Thread.Sleep(300);
            }
            try { latest = TrophyCatalogReader.RefreshState(host, latest, null); } catch { }
            if (log != null) log("Unlock All state: " + latest.UnlockedCount + "/" + latest.Total + " unlocked.");
            return latest;
        }

        private static List<TrophyItem> GetLocked(TrophyListResult list)
        {
            List<TrophyItem> result = new List<TrophyItem>();
            foreach (TrophyItem item in list.Trophies) if (!item.Unlocked) result.Add(item);
            return result;
        }

        private static bool IsUnlocked(TrophyListResult list, int id)
        {
            TrophyItem item = FindById(list, id);
            return item != null && item.StateKnown && item.Unlocked;
        }

        private static List<TrophyItem> GetLockedNonPlatinum(TrophyListResult list)
        {
            List<TrophyItem> result = new List<TrophyItem>();
            foreach (TrophyItem item in GetLocked(list))
                if (!string.Equals(item.GradeName, "Platinum", StringComparison.OrdinalIgnoreCase)) result.Add(item);
            return result;
        }

        private static List<TrophyItem> GetLockedPlatinum(TrophyListResult list)
        {
            List<TrophyItem> result = new List<TrophyItem>();
            foreach (TrophyItem item in GetLocked(list))
                if (string.Equals(item.GradeName, "Platinum", StringComparison.OrdinalIgnoreCase)) result.Add(item);
            return result;
        }

        private static List<string> BuildExactListConfigs(List<TrophyItem> trophies)
        {
            List<int> ids = new List<int>();
            foreach (TrophyItem item in trophies)
                if (!ids.Contains(item.Id)) ids.Add(item.Id);
            ids.Sort();

            const int chunkSize = 40;
            List<string> configs = new List<string>();
            for (int start = 0; start < ids.Count; start += chunkSize)
            {
                int count = Math.Min(chunkSize, ids.Count - start);
                System.Text.StringBuilder line = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) line.Append(',');
                    line.Append(ids[start + i].ToString(CultureInfo.InvariantCulture));
                }
                configs.Add("mode=list\nids=" + line.ToString() + "\n");
            }
            return configs;
        }

        private static bool IsTitleMounted(string host, string ppsa)
        {
            if (string.IsNullOrWhiteSpace(ppsa)) return false;
            string needle = ppsa.Trim().ToUpperInvariant();
            string[] paths = new string[] { "/mnt/sandbox/pfsmnt/", "/mnt/sandbox/" };
            foreach (string path in paths)
            {
                try
                {
                    foreach (string name in TrophyFtp.ListNames(host, path))
                        if ((name ?? string.Empty).ToUpperInvariant().Contains(needle)) return true;
                }
                catch { }
            }
            return false;
        }

        private static void LogPayloadDiagnostic(string host, Action<string> log)
        {
            if (log == null) return;
            string text = TrophyFtp.TryDownloadText(host, "/data/trophy_unlocker_result.txt");
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Replace("\r", " ").Replace("\n", " | ").Trim();
            if (text.Length > 1200) text = text.Substring(0, 1200) + "...";
            log("Payload diagnostic: " + text);
        }

        private static TrophyItem FindById(TrophyListResult list, int id)
        {
            if (list == null) return null;
            foreach (TrophyItem item in list.Trophies) if (item.Id == id) return item;
            return null;
        }

        private void SendCommand(string host, TrophyListResult catalog, string config, bool useUds, string routeName, Action<string> log)
        {
            TrophyFtp.TryDelete(host, "/data/trophy_unlocker_result.txt");
            PrepareCommandFilesFast(host, catalog, config);
            byte[] payload = ReadEmbeddedPayload(useUds);
            Exception last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (log != null) log("Sending " + routeName + " trophy payload to " + host + ":" + LoaderPort + (attempt > 1 ? " (retry " + attempt + ")" : "") + "...");
                    using (TcpClient client = Connect(host, LoaderPort, 2500))
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.Write(payload, 0, payload.Length);
                        stream.Flush();
                    }
                    if (log != null) log(routeName + " trophy command sent.");
                    return;
                }
                catch (Exception error)
                {
                    last = error;
                    Thread.Sleep(200 * attempt);
                }
            }
            throw new InvalidOperationException("Unable to send the " + routeName + " trophy payload to port 9021. Make sure the payload loader is running.", last);
        }

        private void PrepareCommandFilesFast(string host, TrophyListResult catalog, string config)
        {
            TrophyFtp.UploadText(host, "/data/trophy_unlocker_config.txt", config);
            string npwr = (catalog.Npwr ?? string.Empty).Trim();
            bool staticFilesNeeded;
            lock (sessionSync)
            {
                staticFilesNeeded = !string.Equals(preparedHost, host, StringComparison.OrdinalIgnoreCase) ||
                                    !string.Equals(preparedNpwr, npwr, StringComparison.OrdinalIgnoreCase) ||
                                    preparedTotal != catalog.Total;
            }
            if (!staticFilesNeeded) return;

            TrophyFtp.UploadText(host, "/data/trophy_unlocker_platform.txt", "ps5");
            if (!string.IsNullOrWhiteSpace(npwr))
                TrophyFtp.UploadText(host, "/data/trophy_unlocker_npcomm.txt", npwr);
            if (catalog.Total > 0)
                TrophyFtp.UploadText(host, "/data/trophy_unlocker_count.txt", catalog.Total.ToString(CultureInfo.InvariantCulture));
            lock (sessionSync)
            {
                preparedHost = host;
                preparedNpwr = npwr;
                preparedTotal = catalog.Total;
            }
        }

        private static byte[] ReadEmbeddedPayload(bool useUds)
        {
            byte[] cached = useUds ? udsPayloadCache : standardPayloadCache;
            if (cached != null) return cached;
            lock (typeof(TrophyUnlockerEngine))
            {
                cached = useUds ? udsPayloadCache : standardPayloadCache;
                if (cached != null) return cached;
                string resourceName = useUds ? UdsPayloadResource : StandardPayloadResource;
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) throw new InvalidOperationException("The embedded PS5 trophy payload is unavailable. Rebuild the application.");
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        byte[] bytes = buffer.ToArray();
                        if (bytes.Length < 4 || bytes[0] != 0x7F || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
                            throw new InvalidDataException("The embedded PS5 trophy payload is invalid.");
                        if (useUds) udsPayloadCache = bytes; else standardPayloadCache = bytes;
                        return bytes;
                    }
                }
            }
        }

        private static TcpClient Connect(string host, int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            IAsyncResult async = client.BeginConnect(host, port, null, null);
            try
            {
                if (!async.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    client.Close();
                    throw new TimeoutException("Connection timeout to " + host + ":" + port + ".");
                }
                client.EndConnect(async);
                return client;
            }
            finally
            {
                try { async.AsyncWaitHandle.Close(); } catch { }
            }
        }

        private static void AddCandidates(string host, string path, List<GameCandidate> output, HashSet<string> seen)
        {
            string[] entries;
            try { entries = TrophyFtp.ListNames(host, path); }
            catch { return; }
            foreach (string entry in entries)
            {
                Match match = PpsaRegex.Match(entry ?? string.Empty);
                if (!match.Success) continue;
                string ppsa = match.Value.ToUpperInvariant();
                string key = ppsa + "|" + entry;
                if (!seen.Add(key)) continue;
                string lower = (entry ?? string.Empty).ToLowerInvariant();
                int score = 10;
                if (lower.Contains("app0-patch0-union")) score += 100;
                else if (lower.Contains("app0")) score += 70;
                if (lower.StartsWith(ppsa.ToLowerInvariant() + "_")) score += 80;
                output.Add(new GameCandidate(ppsa, score));
            }
        }

        private static void ValidateHost(string host)
        {
            IPAddress parsed;
            if (string.IsNullOrWhiteSpace(host) || !IPAddress.TryParse(host.Trim(), out parsed))
                throw new ArgumentException("Enter a valid PS5 IP address.");
        }

        private static void ValidatePpsa(string ppsa)
        {
            if (string.IsNullOrWhiteSpace(ppsa) || !PpsaRegex.IsMatch(ppsa))
                throw new ArgumentException("Detect the active PS5 game first.");
        }

        private sealed class GameCandidate
        {
            internal readonly string Ppsa;
            internal readonly int Score;
            internal GameCandidate(string ppsa, int score) { Ppsa = ppsa; Score = score; }
        }
    }
}
