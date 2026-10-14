using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class FullRebuildResult
    {
        internal string Package;
        internal string Backup;
        internal string Sha256;
    }

    internal static class FullPackageRebuild
    {
        private const int BufferSize = 4 * 1024 * 1024;

        internal static FullRebuildResult Rebuild(PackageSnapshot expected, string passcode, ulong sdk,
            CancellationToken token, Action<ConversionProgress> report, Action<string> log)
        {
            string sourcePath = Path.GetFullPath(expected.Path);
            string directory = Path.GetDirectoryName(sourcePath);
            FileWorkspace.NoReparse(sourcePath);
            string tempPath = sourcePath + ".full-rebuild-" + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = sourcePath + ".sdk-full-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".pkg-backup";
            bool sourceMoved = false;
            try
            {
                report(new ConversionProgress("Preparing full rebuild", "Validating source package and free space", 0, expected.Length));
                long required = checked(expected.Length + 256L * 1024 * 1024);
                long free = FileWorkspace.FreeBytes(directory);
                log("Full rebuild output requires about " + FileWorkspace.Size(required) + " free on the PKG drive; available: " + FileWorkspace.Size(free) + ".");
                if (free < required) throw new IOException("Not enough free space for Full Rebuild. Required: " + FileWorkspace.Size(required) + "; available: " + FileWorkspace.Size(free) + ".");

                byte[] rebuiltTail;
                long prefixLength;
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                {
                    CntMetadata doc = CntMetadata.Read(input, sourcePath, passcode);
                    PackageSdk.EnsureUnchanged(expected, doc.Snapshot);
                    prefixLength = expected.CntOffset;
                    rebuiltTail = doc.EditForFullRebuild(sdk, passcode, token, log);
                    if (rebuiltTail.Length != expected.Length - prefixLength) throw new InvalidDataException("The rebuilt metadata tail changed package size.");

                    report(new ConversionProgress("Rebuilding PKG", "Streaming all package bytes into a new file", 0, expected.Length));
                    using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.SequentialScan))
                    {
                        byte[] buffer = new byte[BufferSize];
                        long done = 0;
                        input.Position = 0;
                        Stopwatch clock = Stopwatch.StartNew();
                        while (done < prefixLength)
                        {
                            token.ThrowIfCancellationRequested();
                            int want = (int)Math.Min(buffer.Length, prefixLength - done);
                            int read = input.Read(buffer, 0, want);
                            if (read <= 0) throw new EndOfStreamException("Unexpected end of source PKG during full rebuild.");
                            output.Write(buffer, 0, read);
                            done += read;
                            if (clock.ElapsedMilliseconds >= 250)
                            {
                                double mbps = done <= 0 ? 0 : done / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds);
                                report(new ConversionProgress("Rebuilding PKG", FileWorkspace.Size(done) + " / " + FileWorkspace.Size(expected.Length) + " (" + mbps.ToString("0.0", CultureInfo.InvariantCulture) + " MiB/s)", done, expected.Length));
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        output.Write(rebuiltTail, 0, rebuiltTail.Length);
                        done += rebuiltTail.Length;
                        output.Flush(true);
                        if (output.Length != expected.Length) throw new IOException("Full rebuild produced an unexpected file size.");
                        report(new ConversionProgress("Rebuilding PKG", FileWorkspace.Size(done) + " / " + FileWorkspace.Size(expected.Length), done, expected.Length));
                    }
                }

                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Verifying rebuilt PKG", "Checking SDK, canonical CNT hashes and header wrap", expected.Length, expected.Length));
                PackageSnapshot rebuilt = PackageSdk.Inspect(tempPath, passcode);
                if (SdkVersion.Parse(rebuilt.Sdk) != sdk || SdkVersion.Parse(rebuilt.Required) != sdk)
                    throw new IOException("Rebuilt PKG SDK read-back verification failed.");
                using (FileStream check = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                {
                    CntMetadata verified = CntMetadata.Read(check, tempPath, passcode);
                    verified.ValidateCanonical(token, log);
                }

                report(new ConversionProgress("Final verification", "Calculating SHA-256 of the rebuilt PKG", 0, expected.Length));
                string sha = HashFile(tempPath, expected.Length, token, report);
                log("SHA-256 rebuilt PKG: " + sha);

                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Committing rebuilt PKG", "Keeping the original as a full .pkg-backup", expected.Length, expected.Length));
                File.Move(sourcePath, backupPath);
                sourceMoved = true;
                try
                {
                    File.Move(tempPath, sourcePath);
                    sourceMoved = false;
                }
                catch
                {
                    if (File.Exists(sourcePath)) File.Delete(sourcePath);
                    File.Move(backupPath, sourcePath);
                    sourceMoved = false;
                    throw;
                }
                log("Full PKG backup: " + backupPath);
                return new FullRebuildResult { Package = sourcePath, Backup = backupPath, Sha256 = sha };
            }
            finally
            {
                if (sourceMoved && !File.Exists(sourcePath) && File.Exists(backupPath))
                {
                    try { File.Move(backupPath, sourcePath); } catch { }
                }
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static string HashFile(string path, long total, CancellationToken token, Action<ConversionProgress> report)
        {
            byte[] buffer = new byte[BufferSize];
            long done = 0;
            using (SHA256 hash = SHA256.Create())
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            {
                Stopwatch clock = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                    done += read;
                    if (clock.ElapsedMilliseconds >= 250)
                        report(new ConversionProgress("Final verification", FileWorkspace.Size(done) + " / " + FileWorkspace.Size(total), done, total));
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
