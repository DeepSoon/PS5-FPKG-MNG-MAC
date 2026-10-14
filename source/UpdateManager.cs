using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal sealed class ReleaseInfo
    {
        internal string Tag;
        internal string Name;
        internal string Notes;
        internal string PageUrl;
        internal string AssetUrl;
        internal string AssetName;
    }

    internal static class UpdateManager
    {
        private const string LatestApi = "https://api.github.com/repos/zerodaysofficial/ps5-exfat-pkg-builder-v1/releases/latest";

        internal static void Check(Form owner, string currentVersion, Label sidebarVersion, bool manual = false)
        {
            Task.Run(delegate
            {
                try
                {
                    ReleaseInfo release = ReadLatest(currentVersion);
                    if (release == null)
                    {
                        if (manual) ShowManualMessage(owner, "Update check", "GitHub did not return a valid latest release.", MessageBoxIcon.Warning);
                        return;
                    }
                    if (CompareVersions(Normalize(release.Tag), Normalize(currentVersion)) <= 0)
                    {
                        SetSidebar(owner, sidebarVersion, "v" + currentVersion + "  •  up to date");
                        if (manual) ShowManualMessage(owner, "You're up to date", "PS5 FPKG Builder v" + currentVersion + " is the latest available version.", MessageBoxIcon.Information);
                        return;
                    }

                    SetSidebar(owner, sidebarVersion, "v" + currentVersion + "  •  update " + release.Tag);
                    owner.BeginInvoke(new Action(delegate
                    {
                        if (owner.IsDisposed) return;
                        using (UpdatePrompt prompt = new UpdatePrompt(currentVersion, release))
                        {
                            DialogResult choice = prompt.ShowDialog(owner);
                            if (choice == DialogResult.Yes)
                            {
                                BeginAutomaticUpdate(owner, currentVersion, release);
                            }
                            else if (choice == DialogResult.Retry)
                            {
                                OpenRelease(release.PageUrl);
                            }
                        }
                    }));
                }
                catch (Exception error)
                {
                    // Offline, rate limited, or GitHub unavailable: startup remains completely unaffected.
                    SetSidebar(owner, sidebarVersion, "v" + currentVersion + "  •  local build");
                    if (manual) ShowManualMessage(owner, "Update check failed", "Could not check GitHub Releases.\r\n\r\n" + error.Message, MessageBoxIcon.Warning);
                }
            });
        }

        private static ReleaseInfo ReadLatest(string currentVersion)
        {
            using (WebClient client = NewClient(currentVersion))
            {
                string json = client.DownloadString(LatestApi);
                string tag = JsonString(json, "tag_name");
                if (string.IsNullOrEmpty(tag)) return null;
                string assetUrl = SelectZipAssetUrl(json);
                return new ReleaseInfo
                {
                    Tag = tag,
                    Name = JsonString(json, "name"),
                    Notes = JsonString(json, "body"),
                    PageUrl = JsonString(json, "html_url"),
                    AssetUrl = assetUrl,
                    AssetName = AssetFileName(assetUrl)
                };
            }
        }

        private static WebClient NewClient(string version)
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "PS5-FPKG-Builder/" + version;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static void BeginAutomaticUpdate(Form owner, string currentVersion, ReleaseInfo release)
        {
            UpdateDownloadForm progress = new UpdateDownloadForm(release.Tag);
            progress.Show(owner);
            Task.Run(delegate
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(release.AssetUrl))
                        throw new InvalidOperationException("The latest GitHub release does not contain a ZIP asset for automatic update.");

                    string root = Path.Combine(Path.GetTempPath(), "Zer0Day-FPKG-Updater");
                    Directory.CreateDirectory(root);
                    string assetName = string.IsNullOrWhiteSpace(release.AssetName)
                        ? "PS5-FPKG-Builder-" + SafeFileName(release.Tag) + ".zip"
                        : SafeFileName(release.AssetName);
                    string zip = Path.Combine(root, assetName);
                    if (File.Exists(zip)) File.Delete(zip);

                    using (WebClient client = NewClient(currentVersion))
                        client.DownloadFile(release.AssetUrl, zip);

                    ValidateZip(zip);
                    string script = CreateUpdaterScript(root, zip, release.Tag, Process.GetCurrentProcess().Id, AppDomain.CurrentDomain.BaseDirectory);
                    owner.BeginInvoke(new Action(delegate
                    {
                        try { progress.SetComplete(); } catch { }
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = root
                        };
                        Process.Start(psi);
                        Application.Exit();
                    }));
                }
                catch (Exception error)
                {
                    owner.BeginInvoke(new Action(delegate
                    {
                        try { progress.Close(); progress.Dispose(); } catch { }
                        DialogResult result = MessageBox.Show(owner,
                            "The automatic update could not be downloaded.\r\n\r\n" + error.Message +
                            "\r\n\r\nOpen the GitHub release instead?",
                            "Update failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result == DialogResult.Yes) OpenRelease(release.PageUrl);
                    }));
                }
            });
        }

        private static string CreateUpdaterScript(string root, string zip, string tag, int pid, string appDirectory)
        {
            string stage = Path.Combine(root, "stage-" + SafeFileName(tag));
            string script = Path.Combine(root, "apply-update-" + SafeFileName(tag) + ".ps1");
            string text =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$pidToWait = " + pid.ToString() + "\r\n" +
                "$zip = '" + Ps(zip) + "'\r\n" +
                "$stage = '" + Ps(stage) + "'\r\n" +
                "$app = '" + Ps(appDirectory.TrimEnd('\\')) + "'\r\n" +
                "$self = $MyInvocation.MyCommand.Path\r\n" +
                "try { Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue } catch {}\r\n" +
                "Start-Sleep -Milliseconds 500\r\n" +
                "if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }\r\n" +
                "New-Item -ItemType Directory -Path $stage -Force | Out-Null\r\n" +
                "Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force\r\n" +
                "$source = $stage\r\n" +
                "$items = @(Get-ChildItem -LiteralPath $stage)\r\n" +
                "if ($items.Count -eq 1 -and $items[0].PSIsContainer -and (Test-Path -LiteralPath (Join-Path $items[0].FullName 'START.cmd'))) { $source = $items[0].FullName }\r\n" +
                "Copy-Item -Path (Join-Path $source '*') -Destination $app -Recurse -Force\r\n" +
                "Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue\r\n" +
                "Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                "Start-Process -FilePath (Join-Path $app 'START.cmd') -WorkingDirectory $app\r\n" +
                "Start-Sleep -Milliseconds 300\r\n" +
                "Remove-Item -LiteralPath $self -Force -ErrorAction SilentlyContinue\r\n";
            File.WriteAllText(script, text, new UTF8Encoding(false));
            return script;
        }

        private static void ValidateZip(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 1024) throw new InvalidDataException("The release asset is empty or incomplete.");
            using (FileStream stream = File.OpenRead(path))
            {
                int a = stream.ReadByte(), b = stream.ReadByte();
                if (a != 0x50 || b != 0x4B) throw new InvalidDataException("The downloaded release is not a ZIP archive.");
            }
        }

        private static string SelectZipAssetUrl(string json)
        {
            MatchCollection matches = Regex.Matches(json ?? "", "\\\"browser_download_url\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.Singleline);
            string firstZip = "";
            for (int i = 0; i < matches.Count; i++)
            {
                string url = JsonUnescape(matches[i].Groups[1].Value);
                if (string.IsNullOrWhiteSpace(url)) continue;
                string path = url;
                int query = path.IndexOf('?');
                if (query >= 0) path = path.Substring(0, query);
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(firstZip)) firstZip = url;

                string lower = url.ToLowerInvariant();
                if (lower.Contains("ps5-fpkg-builder") || lower.Contains("fpkg-builder")) return url;
            }
            return firstZip;
        }

        private static string AssetFileName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            try
            {
                Uri uri = new Uri(url);
                string name = Path.GetFileName(uri.LocalPath);
                return Uri.UnescapeDataString(name ?? "");
            }
            catch
            {
                string clean = url;
                int query = clean.IndexOf('?');
                if (query >= 0) clean = clean.Substring(0, query);
                int slash = clean.LastIndexOf('/');
                return slash >= 0 ? clean.Substring(slash + 1) : clean;
            }
        }

        private static string JsonString(string json, string property)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(property) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.Singleline);
            if (!match.Success) return "";
            return JsonUnescape(match.Groups[1].Value);
        }

        private static string JsonUnescape(string value)
        {
            StringBuilder output = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length) { output.Append(c); continue; }
                char n = value[++i];
                if (n == 'n') output.Append('\n');
                else if (n == 'r') output.Append('\r');
                else if (n == 't') output.Append('\t');
                else if (n == 'b') output.Append('\b');
                else if (n == 'f') output.Append('\f');
                else if (n == '\\' || n == '/' || n == '"') output.Append(n);
                else if (n == 'u' && i + 4 < value.Length)
                {
                    int code;
                    string hex = value.Substring(i + 1, 4);
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                    { output.Append((char)code); i += 4; }
                    else { output.Append('\\').Append(n); }
                }
                else output.Append(n);
            }
            return output.ToString();
        }

        private static void ShowManualMessage(Form owner, string title, string message, MessageBoxIcon icon)
        {
            try
            {
                if (owner == null || owner.IsDisposed) return;
                Action show = delegate { if (!owner.IsDisposed) MessageBox.Show(owner, message, title, MessageBoxButtons.OK, icon); };
                if (owner.InvokeRequired) owner.BeginInvoke(show); else show();
            }
            catch { }
        }

        private static void SetSidebar(Form owner, Label label, string text)
        {
            try
            {
                if (owner.IsDisposed) return;
                if (owner.InvokeRequired) owner.BeginInvoke(new Action(delegate { if (!label.IsDisposed) label.Text = text; }));
                else label.Text = text;
            }
            catch { }
        }

        private static void OpenRelease(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) url = "https://github.com/zerodaysofficial/ps5-exfat-pkg-builder-v1/releases";
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private static string Normalize(string value) { return (value ?? "").Trim().TrimStart('v', 'V'); }
        private static int CompareVersions(string left, string right)
        {
            string[] a = (left ?? "").Split('.'), b = (right ?? "").Split('.');
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                int x = 0, y = 0;
                if (i < a.Length) int.TryParse(Regex.Match(a[i], "^\\d+").Value, out x);
                if (i < b.Length) int.TryParse(Regex.Match(b[i], "^\\d+").Value, out y);
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        private static string SafeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace(' ', '_');
        }

        private static string Ps(string value) { return value.Replace("'", "''"); }
    }

    internal sealed class UpdatePrompt : Form
    {
        internal UpdatePrompt(string currentVersion, ReleaseInfo release)
        {
            Text = "Update available";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(590, 455);
            BackColor = Color.FromArgb(15, 19, 27);
            ForeColor = Color.FromArgb(238, 242, 248);
            Font = new Font("Segoe UI", 9.5f);

            Label badge = new Label { Text = "NEW UPDATE", AutoSize = true, ForeColor = Color.FromArgb(78, 156, 255), Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Location = new Point(28, 24) };
            Label title = new Label { Text = "PS5 FPKG Builder " + release.Tag, AutoSize = true, Font = new Font("Segoe UI Semibold", 19.0f, FontStyle.Bold), Location = new Point(25, 51) };
            Label versions = new Label { Text = "Installed  v" + currentVersion + "     Latest  " + release.Tag, AutoSize = true, ForeColor = Color.FromArgb(167, 177, 194), Location = new Point(28, 92) };
            Label whatsNew = new Label { Text = "WHAT'S NEW", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Location = new Point(28, 128) };
            TextBox notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(21, 26, 36), ForeColor = Color.FromArgb(238, 242, 248), Location = new Point(28, 153), Size = new Size(534, 205), Text = string.IsNullOrWhiteSpace(release.Notes) ? "A new release is available on GitHub." : release.Notes };
            Button update = MakeButton("UPDATE NOW", 28, 385, 164); update.DialogResult = DialogResult.Yes;
            Button view = MakeButton("VIEW RELEASE", 202, 385, 164); view.DialogResult = DialogResult.Retry;
            Button later = MakeButton("LATER", 376, 385, 186); later.DialogResult = DialogResult.Cancel;
            AcceptButton = update; CancelButton = later;
            Controls.AddRange(new Control[] { badge, title, versions, whatsNew, notes, update, view, later });
        }

        private static Button MakeButton(string text, int x, int y, int width)
        {
            Button button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 39, 53), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold) };
            button.FlatAppearance.BorderColor = Color.FromArgb(58, 70, 91);
            return button;
        }
    }

    internal sealed class UpdateDownloadForm : Form
    {
        private readonly Label message;
        internal UpdateDownloadForm(string version)
        {
            Text = "Updating PS5 FPKG Builder";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false; ShowInTaskbar = false;
            ClientSize = new Size(470, 145);
            BackColor = Color.FromArgb(15, 19, 27); ForeColor = Color.White; Font = new Font("Segoe UI", 9.5f);
            message = new Label { Text = "Downloading " + version + "...", Location = new Point(24, 22), AutoSize = true, Font = new Font("Segoe UI Semibold", 12.0f, FontStyle.Bold) };
            ProgressBar bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Location = new Point(24, 65), Size = new Size(422, 22) };
            Label hint = new Label { Text = "The app will close, update itself and restart automatically.", Location = new Point(24, 101), AutoSize = true, ForeColor = Color.FromArgb(167, 177, 194) };
            Controls.AddRange(new Control[] { message, bar, hint });
        }
        internal void SetComplete() { message.Text = "Download complete. Applying update..."; Refresh(); }
    }
}
