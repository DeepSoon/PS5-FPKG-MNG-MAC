using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class ExfatMount : IDisposable
    {
        private readonly string tool;
        private readonly string image;
        private readonly Action<string> log;
        private string letter;
        private bool attempted;
        private bool ownershipConfirmed;
        private string ownedDeviceTarget;
        private uint? ownedVolumeSerial;
        internal string CleanupWarning { get; private set; }
        internal string Root { get { return letter + "\\"; } }

        private static readonly object RegistryLock = new object();
        private static readonly Dictionary<string, string> OwnedMounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string LeaseDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat", "mounts");
            }
        }

        internal ExfatMount(string tool, string image, Action<string> log)
        { this.tool = tool; this.image = image; this.log = log; }

        internal static string[] MountArguments(string image, string letter)
        { return new string[] { "-a", "-t", "file", "-f", image, "-m", letter, "-o", "ro,logical,hd", "-b", "0" }; }

        internal static string FindTool()
        {
            foreach (string folder in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppDomain.CurrentDomain.BaseDirectory })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                foreach (string suffix in new string[] { "OSFMount\\OSFMount.com", "tools\\OSFMount.com", "OSFMount.com" })
                { string path = Path.Combine(folder, suffix); if (File.Exists(path)) return path; }
            }
            return "";
        }

        // Scope matches the RAW volume images produced by ps5-exfat-builder.
        // Whole disks (MBR/GPT), compressed images and unrelated formats are refused.
        internal static void ValidateImage(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] boot = new byte[512];
                int position = 0;
                while (position < boot.Length)
                { int read = stream.Read(boot, position, boot.Length - position); if (read == 0) throw new InvalidDataException("Truncated exFAT image."); position += read; }
                if (Encoding.ASCII.GetString(boot, 3, 8) != "EXFAT   " || boot[510] != 0x55 || boot[511] != 0xaa)
                    throw new InvalidDataException("Select a RAW exFAT volume image, such as one created by ps5-exfat-builder. Whole-disk MBR/GPT and compressed images are not supported.");
                int sectorShift = boot[108], clusterShift = boot[109];
                if (sectorShift < 9 || sectorShift > 12 || clusterShift + sectorShift > 25 ||
                    boot[110] < 1 || boot[110] > 2 || BitConverter.ToUInt16(boot, 104) != 0x100)
                    throw new InvalidDataException("Unsupported exFAT geometry or version.");
                ulong volume = BitConverter.ToUInt64(boot, 72);
                ulong fat = BitConverter.ToUInt32(boot, 80), fatLength = BitConverter.ToUInt32(boot, 84);
                ulong heap = BitConverter.ToUInt32(boot, 88), clusters = BitConverter.ToUInt32(boot, 92);
                uint root = BitConverter.ToUInt32(boot, 96);
                if (volume < 24 || volume > (ulong)stream.Length >> sectorShift || fat < 24 || fatLength == 0 ||
                    heap < fat + fatLength * boot[110] || heap >= volume || clusters == 0 || clusters > 0xfffffff5 ||
                    clusters > (volume - heap) >> clusterShift || (clusters + 2) * 4 > fatLength * (1UL << sectorShift) ||
                    root < 2 || root > clusters + 1)
                    throw new InvalidDataException("The image is truncated or contains invalid exFAT offsets.");
            }
        }

        internal void Attach(CancellationToken token)
        {
            ValidateImage(image);
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string drive in Directory.GetLogicalDrives()) used.Add(drive.Substring(0, 2));
            for (char candidate = 'Z'; candidate >= 'G'; candidate--)
                if (!used.Contains(candidate + ":")) { letter = candidate + ":"; break; }
            if (letter == null) throw new IOException("No available drive letter from G: to Z:.");
            log("Mounting exFAT read-only on " + letter);
            attempted = true;
            ToolResult result = ProcessRunner.Run(tool, MountArguments(image, letter), 90000, token, log);
            if (result.ExitCode != 0) throw new IOException("OSFMount refused to mount the image (code " + result.ExitCode + "). Check the log and the OSFMount driver.");

            // OSFMount versions do not all use the same text in '-l'. A successful
            // mount's exact 'Created device N: Z: -> image' receipt also proves
            // ownership. Record its DOS device and volume identity before querying.
            ownershipConfirmed = MappingEvidence(result.Output, letter, image, true) == MountEvidence.Matches;
            if (ownershipConfirmed) ownedDeviceTarget = DeviceTarget(letter);
            Stopwatch wait = Stopwatch.StartNew();
            while (!Directory.Exists(Root))
            {
                token.ThrowIfCancellationRequested();
                if (wait.ElapsedMilliseconds > 15000) throw new IOException("The exFAT volume did not become readable.");
                token.WaitHandle.WaitOne(100);
            }
            if (!string.Equals(new DriveInfo(Root).DriveFormat, "exFAT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The mounted volume is not exFAT.");
            if (ownershipConfirmed) ownedVolumeSerial = VolumeSerial(Root);

            ToolResult query = Query(token);
            MountEvidence evidence = query.ExitCode == 0 ? MappingEvidence(query.Output, letter, image, false) : MountEvidence.Unknown;
            if (!CanUseMount(evidence, ownershipConfirmed, ownedDeviceTarget, DeviceTarget(letter), ownedVolumeSerial, VolumeSerial(Root)))
                throw new IOException("Could not verify that " + letter + " belongs to the selected image. The OSFMount response is in the log.");
            if (evidence == MountEvidence.Matches)
            {
                ownershipConfirmed = true;
                ownedDeviceTarget = DeviceTarget(letter);
                ownedVolumeSerial = VolumeSerial(Root);
            }
            else log("Mount verified using the creation receipt, device and volume identity for " + letter + ".");
            RegisterOwnedMount(letter, image, tool);
        }

        internal enum MountEvidence { Unknown, Matches, Different }

        // Old guide: Z: = \??\C:\path\image.exfat
        // Current user screenshot: Created device 0: Z: -> F:\homebrew\PPSA31246.exfat
        // Match whole fields, never a substring of a path or another drive's record.
        private static readonly Regex MappingLine = new Regex(
            @"^\s*(?:(Created\s+)?device\s+(\d+)\s*:\s*)?([A-Za-z]:)[\\/]?\s*(?:->|=>|=)\s*(.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool ListingMatches(string listing, string expectedLetter, string expectedImage)
        { return MappingEvidence(listing, expectedLetter, expectedImage, false) == MountEvidence.Matches; }

        internal static MountEvidence MappingEvidence(string listing, string expectedLetter, string expectedImage, bool creationOnly)
        {
            string expected = NormalizeImagePath(expectedImage);
            if (expected == null || string.IsNullOrEmpty(listing)) return MountEvidence.Unknown;
            bool found = false;
            foreach (string line in listing.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = MappingLine.Match(line);
                if (!match.Success || (creationOnly && !match.Groups[1].Success) ||
                    !match.Groups[3].Value.Equals(expectedLetter, StringComparison.OrdinalIgnoreCase)) continue;
                string path = NormalizeImagePath(match.Groups[4].Value);
                if (path == null) continue;
                if (!path.Equals(expected, StringComparison.OrdinalIgnoreCase)) return MountEvidence.Different;
                found = true;
            }
            return found ? MountEvidence.Matches : MountEvidence.Unknown;
        }

        internal static string NormalizeImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"') path = path.Substring(1, path.Length - 2);
            path = path.Replace('/', '\\');
            if (path.StartsWith("\\??\\", StringComparison.Ordinal) || path.StartsWith("\\\\?\\", StringComparison.Ordinal)) path = path.Substring(4);
            if (path.StartsWith("UNC\\", StringComparison.OrdinalIgnoreCase)) path = "\\\\" + path.Substring(4);
            if (path.IndexOf('"') >= 0 || path.IndexOf('\0') >= 0) return null;
            bool drivePath = path.Length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) && path[1] == ':' && path[2] == '\\';
            return drivePath || path.StartsWith("\\\\", StringComparison.Ordinal) ? path : null;
        }

        internal static bool CanUseMount(MountEvidence evidence, bool confirmed,
            string recordedDevice, string currentDevice, uint? recordedSerial, uint? currentSerial)
        {
            if (evidence == MountEvidence.Different) return false;
            if (evidence == MountEvidence.Matches) return true;
            return confirmed && !string.IsNullOrEmpty(recordedDevice) &&
                string.Equals(recordedDevice, currentDevice, StringComparison.OrdinalIgnoreCase) &&
                recordedSerial.HasValue && currentSerial.HasValue && recordedSerial.Value == currentSerial.Value;
        }

        private ToolResult Query(CancellationToken token)
        {
            log("OSFMount check: -l -m " + letter);
            ToolResult result = ProcessRunner.Run(tool, new string[] { "-l", "-m", letter }, 15000, token,
                delegate(string line) { log("OSFMount query: " + line); });
            log("OSFMount check exit code: " + result.ExitCode);
            if (string.IsNullOrWhiteSpace(result.Output)) log("OSFMount query: empty response.");
            return result;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string deviceName, StringBuilder target, int capacity);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(string root, StringBuilder volumeName, int volumeNameSize,
            out uint serial, out uint maximumComponentLength, out uint flags, StringBuilder fileSystem, int fileSystemSize);

        private static string DeviceTarget(string drive)
        {
            StringBuilder target = new StringBuilder(32768);
            return QueryDosDevice(drive, target, target.Capacity) == 0 ? null : target.ToString();
        }

        private static uint? VolumeSerial(string root)
        {
            uint serial, maximum, flags;
            StringBuilder name = new StringBuilder(261), fileSystem = new StringBuilder(261);
            return GetVolumeInformation(root, name, name.Capacity, out serial, out maximum, out flags, fileSystem, fileSystem.Capacity)
                ? (uint?)serial : null;
        }

        private static void RegisterOwnedMount(string drive, string sourceImage, string osfTool)
        {
            if (string.IsNullOrEmpty(drive) || string.IsNullOrEmpty(sourceImage)) return;
            lock (RegistryLock) OwnedMounts[drive] = sourceImage;
            try
            {
                Directory.CreateDirectory(LeaseDirectory);
                string safe = drive.Replace(":", "");
                File.WriteAllLines(Path.Combine(LeaseDirectory, safe + ".mount"), new string[]
                {
                    drive,
                    Path.GetFullPath(sourceImage),
                    string.IsNullOrEmpty(osfTool) ? "" : Path.GetFullPath(osfTool)
                });
            }
            catch { }
        }

        private static void UnregisterOwnedMount(string drive)
        {
            if (string.IsNullOrEmpty(drive)) return;
            lock (RegistryLock) OwnedMounts.Remove(drive);
            try
            {
                string lease = Path.Combine(LeaseDirectory, drive.Replace(":", "") + ".mount");
                if (File.Exists(lease)) File.Delete(lease);
            }
            catch { }
        }

        private static bool DriveExists(string drive)
        {
            if (string.IsNullOrEmpty(drive)) return false;
            foreach (string item in Directory.GetLogicalDrives())
                if (item.StartsWith(drive, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        private static void RefreshExplorerDrives()
        {
            try
            {
                // SHCNE_DRIVEREMOVED + SHCNF_IDLIST(0) with null items forces the shell
                // to refresh drive state without touching any unrelated disk.
                SHChangeNotify(0x00000080, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        private bool DetachOwnedMount()
        {
            ToolResult query = Query(CancellationToken.None);
            MountEvidence evidence = query.ExitCode == 0 ? MappingEvidence(query.Output, letter, image, false) : MountEvidence.Unknown;
            string currentDevice = DeviceTarget(letter);
            uint? currentSerial = DriveExists(letter) ? VolumeSerial(Root) : null;
            if (!CanUseMount(evidence, ownershipConfirmed, ownedDeviceTarget, currentDevice, ownedVolumeSerial, currentSerial))
            {
                if (!DriveExists(letter))
                {
                    UnregisterOwnedMount(letter);
                    RefreshExplorerDrives();
                    return true;
                }
                throw new IOException("Mount identity could not be verified; check " + letter + " in OSFMount.");
            }

            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    log("Unmounting " + letter + " (attempt " + attempt + "/3)");
                    ToolResult result = ProcessRunner.Run(tool, new string[] { "-d", "-m", letter }, 20000, CancellationToken.None, log);
                    Thread.Sleep(attempt == 1 ? 250 : 650);

                    ToolResult verify = Query(CancellationToken.None);
                    MountEvidence remaining = verify.ExitCode == 0 ? MappingEvidence(verify.Output, letter, image, false) : MountEvidence.Unknown;
                    bool present = DriveExists(letter);
                    string device = DeviceTarget(letter);

                    if (!present || remaining == MountEvidence.Different ||
                        (remaining == MountEvidence.Unknown && string.IsNullOrEmpty(device)))
                    {
                        UnregisterOwnedMount(letter);
                        RefreshExplorerDrives();
                        log("Unmount complete: " + letter + " released automatically.");
                        return true;
                    }

                    if (result.ExitCode != 0)
                        last = new IOException("OSFMount detach returned code " + result.ExitCode + ".");
                    else
                        last = new IOException("Windows still reports " + letter + " after the detach request.");
                }
                catch (Exception error)
                {
                    last = error;
                }
                Thread.Sleep(350 * attempt);
            }

            if (last != null) throw last;
            return false;
        }

        internal static void CleanupStaleOwnedMounts(Action<string> logger)
        {
            string folder = LeaseDirectory;
            if (!Directory.Exists(folder)) return;
            foreach (string lease in Directory.GetFiles(folder, "*.mount"))
            {
                try
                {
                    string[] lines = File.ReadAllLines(lease);
                    if (lines.Length < 3) { File.Delete(lease); continue; }
                    string drive = lines[0], sourceImage = lines[1], osfTool = lines[2];
                    if (string.IsNullOrEmpty(drive) || string.IsNullOrEmpty(sourceImage) || string.IsNullOrEmpty(osfTool) || !File.Exists(osfTool))
                    { File.Delete(lease); continue; }

                    bool activeHere = false;
                    lock (RegistryLock)
                    {
                        string currentImage;
                        activeHere = OwnedMounts.TryGetValue(drive, out currentImage) &&
                            string.Equals(Path.GetFullPath(currentImage), Path.GetFullPath(sourceImage), StringComparison.OrdinalIgnoreCase);
                    }
                    if (activeHere) continue;

                    ToolResult listing = ProcessRunner.Run(osfTool, new string[] { "-l", "-m", drive }, 10000, CancellationToken.None,
                        delegate(string line) { if (logger != null) logger("OSFMount recovery: " + line); });
                    MountEvidence evidence = listing.ExitCode == 0 ? MappingEvidence(listing.Output, drive, sourceImage, false) : MountEvidence.Unknown;
                    if (evidence == MountEvidence.Matches)
                    {
                        if (logger != null) logger("Recovering a mount left by an interrupted previous session: " + drive);
                        ProcessRunner.Run(osfTool, new string[] { "-d", "-m", drive }, 20000, CancellationToken.None,
                            delegate(string line) { if (logger != null) logger("OSFMount recovery: " + line); });
                        Thread.Sleep(300);
                        RefreshExplorerDrives();
                    }
                    if (!DriveExists(drive) || evidence == MountEvidence.Different) File.Delete(lease);
                }
                catch (Exception error)
                {
                    if (logger != null) logger("Previous mount cleanup warning: " + error.Message);
                }
            }
        }

        public void Dispose()
        {
            if (!attempted) return;
            try
            {
                if (DetachOwnedMount())
                {
                    attempted = false;
                    CleanupWarning = null;
                }
            }
            catch (Exception error)
            {
                // Keep attempted=true so a later finally/exit cleanup gets another chance.
                CleanupWarning = "Automatic unmount needs attention: " + error.Message;
                log(CleanupWarning);
            }
        }
    }
}
