using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace FpkgExfat
{
    internal sealed class ExfatIntegration
    {
        private enum SourceMode { Folder, Exfat, Pfs, Ufs2, Pkg }
        private static readonly string Version = AppVersion.Read();
        private readonly Form form;
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly Panel pkgBuilderPage = CreatePagePanel("PKG Builder");
        private readonly Panel exfatPage = CreatePagePanel("exFAT → PKG");
        private readonly Panel pfsPage = CreatePagePanel("FFPFS / FFPFSC → PKG");
        private readonly Panel ffpkgPage = CreatePagePanel("FFPKG → PKG");
        private readonly Panel pkgPage = CreatePagePanel("PKG → PKG");
        private readonly Panel extractorPage = CreatePagePanel("PKG Extractor to Folder");
        private readonly Panel fileEditorPage = CreatePagePanel("Extract file from PKG");
        private PkgFileEditorControl fileEditor;
        private readonly Panel readerPage = CreatePagePanel("Reader");
        private readonly Panel backportPage = CreatePagePanel("Backport Update");
        private readonly Panel trophyPage = CreatePagePanel("Trophy Unlocker");
        private readonly Panel advancedPage = CreatePagePanel("Advanced");
        private readonly Panel creditsPage = CreatePagePanel("Credits");
        private Control buildWorkspace;
        private SourceMode sourceMode = SourceMode.Folder;
        private readonly ComboBox theme = new ComboBox { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox language = new ComboBox { Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ThemeManager themeManager;

        // UX 2.0 shell: custom sidebar navigation instead of the classic tab strip.
        private readonly Panel pageHost = new Panel { Dock = DockStyle.Fill };
        private readonly Label pageTitleLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 18.0f, FontStyle.Bold) };
        private readonly Label pageSubtitleLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Font = new Font("Segoe UI", 9.25f, FontStyle.Regular) };
        private readonly Label sidebarVersion = new Label { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0) };
        private readonly Button navBuilder = new Button();
        private readonly Button navExfat = new Button();
        private readonly Button navPfs = new Button();
        private readonly Button navFfpkg = new Button();
        private readonly Button navPkg = new Button();
        private readonly Button navExtractor = new Button();
        private readonly Button navFileEditor = new Button();
        private readonly Button navReader = new Button();
        private readonly Button navBackport = new Button();
        private readonly Button navTrophy = new Button();
        private readonly Button navAdvanced = new Button();
        private readonly Button navCredits = new Button();
        private readonly Button checkUpdates = new Button();
        private Button[] navigationButtons;
        private Control currentPage;

        // Unified Build page
        private readonly TextBox importPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button importFolder = new Button { Text = "Import folder", AutoSize = true };
        private readonly Button importFile = new Button { Text = "Import file", AutoSize = true };
        private readonly Button clearImport = new Button { Text = "Clear", AutoSize = true };
        private readonly TextBox outputFolder = new TextBox { Dock = DockStyle.Fill };
        private readonly Button browseOutput = new Button { Text = "Browse...", AutoSize = true };
        private readonly TextBox temporary = new TextBox { Dock = DockStyle.Fill };
        private readonly Button browseTemporary = new Button { Text = "Browse...", AutoSize = true };
        private readonly ComboBox targetSdk = new ComboBox { Width = 180 };
        private readonly CheckBox verifyCopy = new CheckBox { Text = "Verify SHA-256 of extracted files (folder/exFAT build directly)", Checked = true, AutoSize = true };
        private readonly TextBox pkgPasscode = new TextBox { Text = "00000000000000000000000000000000", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        private Control pkgPasscodeRow;
        private readonly Button build = new Button { Text = "Build PKG", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        private readonly Button openOutput = new Button { Text = "Open output", AutoSize = true, Enabled = false };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
        private readonly Label status = new Label { Text = "Ready", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly RichTextBox log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, DetectUrls = false };
        private readonly PictureBox cover = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly PictureBox background = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly Label previewTitle = new Label { Text = "No source selected", Font = new Font("Segoe UI", 13, FontStyle.Bold), AutoSize = true };
        private readonly TableLayoutPanel previewValues = new TableLayoutPanel();

        // PKG Extractor to Folder page
        private readonly TextBox extractorPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly TextBox extractorDestination = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox extractorPasscode = new TextBox { Text = "00000000000000000000000000000000", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        private readonly CheckBox extractorVerify = new CheckBox { Text = "Verify SHA-256 while writing the extracted folder", Checked = true, AutoSize = true };
        private readonly Button extractorImport = new Button { Text = "Import PKG", AutoSize = true };
        private readonly Button extractorBrowse = new Button { Text = "Browse...", AutoSize = true };
        private readonly Button extractorStart = new Button { Text = "Extract to Folder", AutoSize = true };
        private readonly Button extractorCancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        private readonly Button extractorOpen = new Button { Text = "Open Folder", AutoSize = true, Enabled = false };
        private readonly ProgressBar extractorProgress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
        private readonly Label extractorStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly RichTextBox extractorLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, DetectUrls = false };

        // Backport editor: the game folder is only used as a read-only GP5 source;
        // selected overlay files are staged separately and the base PKG is locked read-only.
        private readonly TextBox backportBasePath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportBaseBrowse = new Button { Text = "Scegli PKG...", AutoSize = true };
        private readonly TextBox backportGamePath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportGameBrowse = new Button { Text = "Scegli cartella...", AutoSize = true };
        private readonly TextBox backportFolderPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportFolderBrowse = new Button { Text = "Scegli cartella...", AutoSize = true };
        private readonly TextBox backportPublisherPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportPublisherBrowse = new Button { Text = "Scegli SDK...", AutoSize = true };
        private readonly TextBox backportPasscode = new TextBox { Text = "00000000000000000000000000000000", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        private readonly TextBox backportVersion = new TextBox { Dock = DockStyle.Fill, MaxLength = 11 };
        private readonly TextBox backportOutputFolder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportOutputBrowse = new Button { Text = "Scegli...", AutoSize = true };
        private readonly TextBox backportWorkFolder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button backportWorkBrowse = new Button { Text = "Scegli...", AutoSize = true };
        private readonly NumericUpDown backportCompression = new NumericUpDown { Minimum = 0, Maximum = 9, Value = 7, Width = 58 };
        private readonly Button backportAddFiles = new Button { Text = "Aggiungi file", AutoSize = true };
        private readonly Button backportAddFolder = new Button { Text = "Aggiungi cartella", AutoSize = true };
        private readonly Button backportReplace = new Button { Text = "Sostituisci", AutoSize = true };
        private readonly Button backportExtract = new Button { Text = "Estrai selezionati", AutoSize = true };
        private readonly Button backportRemove = new Button { Text = "Escludi", AutoSize = true };
        private readonly Button backportSelectAll = new Button { Text = "Seleziona tutti", AutoSize = true };
        private readonly Button backportSelectNone = new Button { Text = "Deseleziona", AutoSize = true };
        private readonly Button backportRefresh = new Button { Text = "Ricarica", AutoSize = true };
        private readonly Button backportSaveSelection = new Button { Text = "Salva file selezionati...", AutoSize = true };
        private readonly Button backportBuild = new Button { Text = "Crea PKG aggiornamento", AutoSize = true, Tag = "primary" };
        private readonly Button backportCancel = new Button { Text = "Annulla", AutoSize = true, Enabled = false };
        private readonly DataGridView backportGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = false, MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, BorderStyle = BorderStyle.None };
        private readonly Label backportBaseInfo = new Label { Text = "Seleziona il PKG base installato sulla console.", Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted" };
        private readonly Label backportFileSummary = new Label { Text = "Nessuna cartella backport caricata", Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted" };
        private readonly Label backportStatus = new Label { Text = "Pronto", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
        private readonly ProgressBar backportProgress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
        private readonly RichTextBox backportLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, DetectUrls = false };
        private readonly ConcurrentQueue<string> backportMessages = new ConcurrentQueue<string>();
        private CancellationTokenSource backportCancellation;
        private ConversionProgress backportLatest;
        private bool backportBusy;
        private bool backportInspecting;
        private PackageSnapshot backportSnapshot;
        private BackportFileSet backportFiles;
        private ContextMenuStrip backportContextMenu;

        // Reader page
        private readonly TextBox readerPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly TextBox readerPasscode = new TextBox { Text = "00000000000000000000000000000000", Width = 290, UseSystemPasswordChar = true };
        private readonly PictureBox readerCover = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly PictureBox readerBackground = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly Button readerImport = new Button { Text = "Import PKG", AutoSize = true };
        private readonly Button readerClear = new Button { Text = "Clear", AutoSize = true };
        private readonly Label readerTitle = new Label { Text = "No PKG selected", Font = new Font("Segoe UI", 13, FontStyle.Bold), AutoSize = true };
        private readonly Label readerHeroContent = new Label { Text = "—", AutoEllipsis = true, Dock = DockStyle.Fill };
        private readonly Label readerHeroFacts = new Label { Text = "Title ID  —    •    Region  —    •    Version  —", AutoEllipsis = true, Dock = DockStyle.Fill };
        private readonly Label readerHeroType = new Label { Text = "PKG", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
        private readonly TableLayoutPanel readerValues = new TableLayoutPanel();

        // Trophy Unlocker page
        private readonly TextBox trophyIp = new TextBox { Dock = DockStyle.Fill };
        private readonly Button trophyDetect = new Button { Text = "Detect Active Game", AutoSize = true, Tag = "primary" };
        private readonly Button trophyDisconnect = new Button { Text = "Disconnect", AutoSize = true, Enabled = false, Tag = "ghost" };
        private readonly Label trophyConnection = new Label { Text = "Disconnected", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
        private readonly Label trophyActiveGame = new Label { Text = "—", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold) };
        private readonly Button trophyGetList = new Button { Text = "Refresh Trophy List", AutoSize = true, Enabled = false, Tag = "primary" };
        private readonly Label trophySummary = new Label { Text = "No trophy list loaded", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
        private readonly DataGridView trophyGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, BorderStyle = BorderStyle.None };
        private readonly Button trophyUnlock = new Button { Text = "Unlock", Dock = DockStyle.Top, Height = 44, Enabled = false, Tag = "primary" };
        private readonly Button trophyUnlockAll = new Button { Text = "Unlock All", Dock = DockStyle.Top, Height = 44, Enabled = false };
        private readonly Button trophyClearLog = new Button { Text = "Clear Log", AutoSize = true, Tag = "ghost" };
        private readonly RichTextBox trophyLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = true, DetectUrls = false };
        private readonly TrophyUnlockerEngine trophyEngine = new TrophyUnlockerEngine();
        private TrophyListResult currentTrophyList;
        private string trophyDetectedPpsa = string.Empty;
        private bool trophyConnected;
        private bool trophyBusy;

        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> extractorMessages = new ConcurrentQueue<string>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 };
        private readonly Stopwatch elapsed = new Stopwatch();
        private CancellationTokenSource cancellation;
        private CancellationTokenSource extractorCancellation;
        private ConversionProgress latest;
        private ConversionProgress extractorLatest;
        private bool busy;
        private bool extractorBusy;
        private SourceInfo current;
        private string lastOutput;
        private string lastExtractorOutput;
        private string lastCleanupWarning;
        private string activeLogFile;
        private readonly string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat");

        private static Panel CreatePagePanel(string name)
        {
            return new Panel
            {
                Name = name.Replace(" ", "").Replace("/", "").Replace("→", "To") + "Page",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Visible = false,
                Tag = "page"
            };
        }

        internal ExfatIntegration(Form form)
        {
            this.form = form;
            MaskSensitiveInputs(form);
            form.SuspendLayout();
            form.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            form.BackColor = Color.FromArgb(11, 14, 20);

            // Preserve the original LibProsperoPkg controls inside Advanced.
            Control[] original = new Control[form.Controls.Count];
            form.Controls.CopyTo(original, 0);
            form.Controls.Clear();
            advancedPage.Controls.AddRange(original);

            BuildUnifiedPage();
            BuildExtractorPage();
            fileEditor = new PkgFileEditorControl(form, delegate { return busy || extractorBusy || backportBusy || backportInspecting; },
                delegate(string source, string output, string scratch, CancellationToken token)
                { return form.Invoke(new Func<object>(delegate { return BuilderBridge.PrepareOptions(form, source, output, scratch, token); })); });
            fileEditorPage.Controls.Add(fileEditor);
            BuildReaderPage();
            BuildBackportPage();
            BuildTrophyUnlockerPage();
            BuildCreditsPage();

            // Build the custom navigation model. The requested sections remain,
            // but they are presented as a modern left rail instead of classic WinForms tabs.
            navigationButtons = new Button[] { navBuilder, navExfat, navPfs, navFfpkg, navPkg, navExtractor, navFileEditor, navReader, navBackport, navTrophy, navAdvanced, navCredits };
            ConfigureNavButton(navBuilder, "▣", "PKG Builder");
            ConfigureNavButton(navExfat, "↗", "exFAT → PKG");
            ConfigureNavButton(navPfs, "◆", "FFPFS / FFPFSC → PKG");
            ConfigureNavButton(navFfpkg, "◈", "FFPKG → PKG");
            ConfigureNavButton(navPkg, "⇄", "PKG → PKG");
            ConfigureNavButton(navExtractor, "⇩", "PKG Extractor to Folder");
            ConfigureNavButton(navFileEditor, "▤", "Extract file from PKG");
            ConfigureNavButton(navReader, "◎", "Reader");
            ConfigureNavButton(navBackport, "↻", "Backport Update");
            ConfigureNavButton(navTrophy, "★", "Trophy Unlocker");
            ConfigureNavButton(navAdvanced, "⚙", "Advanced");
            ConfigureNavButton(navCredits, "✦", "Credits");
            ConfigureNavButton(checkUpdates, "↻", "Check updates");

            // The build workspace is shared by the build/conversion sections and is attached
            // before the first paint, eliminating the previous blank-first-page behavior.
            AttachWorkspace(pkgBuilderPage);
            sourceMode = SourceMode.Folder;
            ConfigureSourceMode();

            foreach (Control page in new Control[] { pkgBuilderPage, exfatPage, pfsPage, ffpkgPage, pkgPage, extractorPage, fileEditorPage, readerPage, backportPage, trophyPage, advancedPage, creditsPage })
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                pageHost.Controls.Add(page);
            }

            // Left navigation rail.
            TableLayoutPanel sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 18, 12, 12), Margin = Padding.Empty, Tag = "sidebar" };
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            TableLayoutPanel brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            PictureBox brandLogo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 8, 4) };
            LoadPicture(brandLogo, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "zer0day-mark.png"));
            brand.Controls.Add(brandLogo, 0, 0);
            brand.SetRowSpan(brandLogo, 2);
            brand.Controls.Add(new Label { Text = "ZER0DAY", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold), Tag = "muted" }, 1, 0);
            brand.Controls.Add(new Label { Text = "PS5 FPKG", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Font = new Font("Segoe UI Semibold", 19.0f, FontStyle.Bold) }, 1, 1);
            sidebar.Controls.Add(brand, 0, 0);

            FlowLayoutPanel nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
            foreach (Button button in navigationButtons) nav.Controls.Add(button);
            checkUpdates.Margin = new Padding(0, 10, 0, 0);
            nav.Controls.Add(checkUpdates);
            sidebar.Controls.Add(nav, 0, 1);

            sidebarVersion.Text = "v" + Version + "  •  local build";
            sidebarVersion.Tag = "muted";
            sidebarVersion.Dock = DockStyle.Bottom;
            sidebar.Controls.Add(sidebarVersion, 0, 2);

            // Header + content area.
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(18, 10, 18, 6), Margin = Padding.Empty, Tag = "header" };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            header.Controls.Add(pageTitleLabel, 0, 0);
            header.Controls.Add(pageSubtitleLabel, 0, 1);
            header.Controls.Add(new Label { Text = "Language", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" }, 1, 0);
            language.Items.AddRange(new object[] { "English", "Italiano", "Español", "Português", "Русский" });
            language.SelectedIndex = 0;
            header.Controls.Add(language, 2, 0);
            header.Controls.Add(new Label { Text = "Theme", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" }, 3, 0);
            theme.Items.AddRange(new object[] { "Light", "Dark" });
            theme.SelectedIndex = 0;
            header.Controls.Add(theme, 4, 0);
            header.SetRowSpan(language, 2);
            header.SetRowSpan(theme, 2);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(header, 0, 0);
            right.Controls.Add(pageHost, 0, 1);

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.Controls.Add(sidebar, 0, 0);
            shell.Controls.Add(right, 1, 0);
            form.Controls.Add(shell);

            form.Text = "PS5 FPKG Builder " + Version + " | Zer0Day";
            form.MinimumSize = new Size(Math.Max(form.MinimumSize.Width, 1220), Math.Max(form.MinimumSize.Height, 790));
            form.Size = new Size(Math.Max(form.Width, 1360), Math.Max(form.Height, 860));
            form.ResumeLayout(true);

            SdkVersion.Populate(targetSdk);
            LoadSettings();
            themeManager = new ThemeManager(form);
            themeManager.Apply(theme.SelectedIndex == 1);

            theme.SelectedIndexChanged += delegate { themeManager.Apply(theme.SelectedIndex == 1); UpdateNavVisuals(); SaveSettings(); };
            language.SelectedIndexChanged += delegate { ApplyLanguage(); SaveSettings(); };
            importFolder.Click += async delegate { await ImportFolder(); };
            importFile.Click += async delegate { await ImportFile(); };
            clearImport.Click += delegate { ClearImport(); };
            browseOutput.Click += delegate { ChooseFolder(outputFolder); };
            browseTemporary.Click += delegate { ChooseFolder(temporary); };
            build.Click += async delegate { await StartBuild(); };
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            openOutput.Click += delegate
            {
                if (!string.IsNullOrEmpty(lastOutput))
                    Process.Start(new ProcessStartInfo(Path.GetDirectoryName(lastOutput)) { UseShellExecute = true });
            };
            extractorImport.Click += delegate { ImportExtractorPkg(); };
            extractorBrowse.Click += delegate { ChooseExtractorDestination(); };
            extractorStart.Click += async delegate { await StartExtraction(); };
            extractorCancel.Click += delegate
            {
                if (extractorCancellation != null)
                {
                    extractorStatus.Text = "Cancellation requested...";
                    extractorCancellation.Cancel();
                }
            };
            extractorOpen.Click += delegate
            {
                if (!string.IsNullOrEmpty(lastExtractorOutput) && Directory.Exists(lastExtractorOutput))
                    Process.Start(new ProcessStartInfo(lastExtractorOutput) { UseShellExecute = true });
            };
            readerImport.Click += async delegate { await ImportPkgReader(); };
            readerClear.Click += delegate { ClearReader(); };
            backportBaseBrowse.Click += async delegate { await ChooseBackportBase(); };
            backportGameBrowse.Click += delegate { ChooseBackportGameFolder(); };
            backportFolderBrowse.Click += delegate { ChooseBackportFolder(); };
            backportPublisherBrowse.Click += delegate { ChooseBackportPublisher(); };
            backportOutputBrowse.Click += delegate { ChooseFolder(backportOutputFolder); UpdateBackportOutputName(); };
            backportWorkBrowse.Click += delegate { ChooseFolder(backportWorkFolder); };
            backportAddFiles.Click += delegate { AddBackportFiles(); };
            backportAddFolder.Click += delegate { AddBackportFolder(); };
            backportReplace.Click += delegate { ReplaceBackportFile(); };
            backportExtract.Click += delegate { ExtractBackportSelection(); };
            backportRemove.Click += delegate { ExcludeBackportSelection(); };
            backportSelectAll.Click += delegate { SetBackportSelection(true); };
            backportSelectNone.Click += delegate { SetBackportSelection(false); };
            backportRefresh.Click += delegate { ReloadBackportFolder(); };
            backportSaveSelection.Click += delegate { SaveBackportSelection(); };
            backportBuild.Click += async delegate { await StartBackportBuild(); };
            backportCancel.Click += delegate
            {
                if (backportCancellation != null)
                {
                    backportStatus.Text = "Annullamento richiesto...";
                    backportCancellation.Cancel();
                }
            };
            backportGrid.CurrentCellDirtyStateChanged += delegate
            { if (backportGrid.IsCurrentCellDirty) backportGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            backportGrid.CellValueChanged += BackportGridCellValueChanged;
            backportGrid.CellMouseDown += BackportGridCellMouseDown;
            backportGrid.CellDoubleClick += delegate { ExtractBackportSelection(); };
            trophyDetect.Click += async delegate { await DetectTrophyGame(); };
            trophyDisconnect.Click += delegate { DisconnectTrophy(); };
            trophyGetList.Click += async delegate { await GetTrophyList(); };
            trophyUnlock.Click += async delegate { await UnlockSelectedTrophy(); };
            trophyUnlockAll.Click += async delegate { await UnlockAllTrophies(); };
            trophyClearLog.Click += delegate { trophyLog.Clear(); TrophyLog("Log cleared."); };
            trophyGrid.SelectionChanged += delegate { RefreshTrophyButtons(); };

            navBuilder.Click += delegate { Navigate(pkgBuilderPage, SourceMode.Folder, "PKG Builder", "Build a package from an already prepared source folder."); };
            navExfat.Click += delegate { Navigate(exfatPage, SourceMode.Exfat, "exFAT → PKG", "Mount the exFAT image read-only and build the PKG directly without copying the dump."); };
            navPfs.Click += delegate { Navigate(pfsPage, SourceMode.Pfs, "FFPFS / FFPFSC → PKG", "Open raw or compressed PFS sources and build the final package."); };
            navFfpkg.Click += delegate { Navigate(ffpkgPage, SourceMode.Ufs2, "FFPKG → PKG", "Read an FFPKG/UFS2 container and convert its source structure."); };
            navPkg.Click += delegate { Navigate(pkgPage, SourceMode.Pkg, "PKG → PKG", "Import any PS5 PKG, auto-detect its package type, and rebuild it when the source content is readable."); };
            navExtractor.Click += delegate { NavigateUtility(extractorPage, "PKG Extractor to Folder", "Extract readable PKG/FPKG contents into a normal folder without rebuilding the package."); };
            navFileEditor.Click += delegate { NavigateUtility(fileEditorPage, "Extract file from PKG", "Apri il PKG, estrai file selezionati e importa modifiche. Il nuovo PKG viene ricostruito dal pacchetto base."); };
            navReader.Click += delegate { NavigateUtility(readerPage, "Reader", "Inspect PKG metadata and artwork without modifying the package."); };
            navBackport.Click += delegate { NavigateUtility(backportPage, "Backport Update", "Seleziona e gestisci i file backport, poi crea un PKG delta sul PKG base."); };
            navTrophy.Click += delegate { NavigateUtility(trophyPage, "Trophy Unlocker", "Detect the active PS5 game, read its Trophy2 list and unlock selected or all trophies."); };
            navAdvanced.Click += delegate { NavigateUtility(advancedPage, "Advanced", "Original LibProsperoPkg builder controls and expert options."); };
            navCredits.Click += delegate { NavigateUtility(creditsPage, "Credits", "Project authors, upstream components and acknowledgements."); };
            checkUpdates.Click += delegate { UpdateManager.Check(form, Version, sidebarVersion, true); };

            timer.Tick += delegate { RefreshUi(); };
            timer.Start();
            form.FormClosing += OnClosing;
            form.FormClosed += delegate
            {
                ExfatMount.CleanupStaleOwnedMounts(Log);
                timer.Stop(); timer.Dispose(); themeManager.Dispose();
                DisposePicture(cover); DisposePicture(background);
                DisposePicture(readerCover); DisposePicture(readerBackground);
            };

            ApplyLanguage();
            // Force a deterministic first page after all controls exist.
            Navigate(pkgBuilderPage, SourceMode.Folder, "PKG Builder", "Build a package from an already prepared source folder.");
            form.Shown += delegate
            {
                form.BeginInvoke(new Action(delegate { Navigate(pkgBuilderPage, SourceMode.Folder, "PKG Builder", "Build a package from an already prepared source folder."); form.PerformLayout(); form.Invalidate(true); }));
                Task.Run(delegate { ExfatMount.CleanupStaleOwnedMounts(Log); });
                BeginUpdateCheck();
            };

            Log("FPKG Builder " + Version + " loaded. UX 2.0 shell active. Supported sources: folder, exFAT, FFPKG/UFS2, FFPFS, FFPFSC, PFS and debug PKG rebuild. Automatic mount cleanup is enabled.");
        }

        private void ConfigureNavButton(Button button, string glyph, string text)
        {
            button.Text = glyph + "    " + text;
            button.Width = 204;
            button.Height = 48;
            button.Margin = new Padding(0, 0, 0, 7);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(14, 0, 8, 0);
            button.Font = new Font("Segoe UI Semibold", 9.75f, FontStyle.Bold);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Tag = "nav";
        }

        private void Navigate(Panel page, SourceMode mode, string title, string subtitle)
        {
            if (sourceMode != mode && current != null) ClearImport();
            sourceMode = mode;
            AttachWorkspace(page);
            ConfigureSourceMode();
            ShowPage(page, title, subtitle);
        }

        private void NavigateUtility(Panel page, string title, string subtitle)
        {
            ShowPage(page, title, subtitle);
        }

        private void ShowPage(Control page, string title, string subtitle)
        {
            foreach (Control child in pageHost.Controls) child.Visible = false;
            page.Visible = true;
            page.BringToFront();
            currentPage = page;
            pageTitleLabel.Text = title;
            pageSubtitleLabel.Text = subtitle;
            UpdateNavVisuals();
            page.PerformLayout();
        }

        private void UpdateNavVisuals()
        {
            if (navigationButtons == null) return;
            Control[] pages = new Control[] { pkgBuilderPage, exfatPage, pfsPage, ffpkgPage, pkgPage, extractorPage, fileEditorPage, readerPage, backportPage, trophyPage, advancedPage, creditsPage };
            for (int i = 0; i < navigationButtons.Length; i++)
                navigationButtons[i].Tag = pages[i] == currentPage ? "nav-active" : "nav";
            if (themeManager != null) themeManager.Apply(theme.SelectedIndex == 1);
        }

        private void ClearReader()
        {
            readerPath.Clear();
            readerTitle.Text = "No PKG selected";
            UpdateReaderHero(null);
            FillMetadata(readerValues, null);
            DisposePicture(readerCover);
            DisposePicture(readerBackground);
            status.Text = "Reader · ready";
        }


        private static void MaskSensitiveInputs(Form target)
        {
            try
            {
                foreach (System.Reflection.FieldInfo field in BuilderBridge.FormType.GetFields(BuilderBridge.Members))
                {
                    string name = field.Name ?? "";
                    if (name.IndexOf("passcode", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    TextBox text = field.GetValue(target) as TextBox;
                    if (text != null) text.UseSystemPasswordChar = true;
                }
            }
            catch { }
            MaskSensitiveInputsRecursive(target);
        }

        private static void MaskSensitiveInputsRecursive(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                TextBox text = child as TextBox;
                if (text != null)
                {
                    string name = (text.Name ?? "") + " " + (text.AccessibleName ?? "");
                    if (name.IndexOf("passcode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                        text.UseSystemPasswordChar = true;
                }
                if (child.HasChildren) MaskSensitiveInputsRecursive(child);
            }
        }

        private void BuildUnifiedPage()
        {
            pkgBuilderPage.Padding = exfatPage.Padding = pfsPage.Padding = ffpkgPage.Padding = pkgPage.Padding = new Padding(18, 10, 18, 18);
            importFolder.Tag = "primary";
            importFile.Tag = "primary";
            build.Tag = "primary-large";
            clearImport.Tag = "ghost";
            openOutput.Tag = "ghost";
            cancel.Tag = "ghost";
            cover.BorderStyle = BorderStyle.None;
            background.BorderStyle = BorderStyle.None;

            TableLayoutPanel dashboard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
            dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
            dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));

            // LEFT: source + rich media preview.
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = Padding.Empty, Margin = new Padding(0, 0, 10, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard sourceCard = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel sourceGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            sourceGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label sourceHead = new Label { Text = "SOURCE", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" };
            Label sourceHint = new Label { Text = "Choose a source or drag it onto this page", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            sourceGrid.Controls.Add(sourceHead, 0, 0); sourceGrid.SetColumnSpan(sourceHead, 3);
            Panel importSwitch = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            importFolder.Dock = DockStyle.Fill; importFolder.Margin = Padding.Empty;
            importFile.Dock = DockStyle.Fill; importFile.Margin = Padding.Empty;
            importSwitch.Controls.Add(importFolder); importSwitch.Controls.Add(importFile);
            sourceGrid.Controls.Add(importSwitch, 0, 1);
            sourceGrid.Controls.Add(clearImport, 1, 1);
            importPath.Margin = new Padding(10, 3, 0, 3);
            sourceGrid.Controls.Add(importPath, 2, 1);
            sourceCard.Controls.Add(sourceGrid);
            left.Controls.Add(sourceCard, 0, 0);

            ModernCard previewCard = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = Padding.Empty };
            TableLayoutPanel preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            previewTitle.Font = new Font("Segoe UI Semibold", 16.5f, FontStyle.Bold);
            previewTitle.Dock = DockStyle.Fill;
            previewTitle.TextAlign = ContentAlignment.MiddleLeft;
            preview.Controls.Add(previewTitle, 0, 0); preview.SetColumnSpan(previewTitle, 2);

            ModernCard bgCard = new ModernCard { Dock = DockStyle.Fill, Tag = "media", Padding = new Padding(1), Margin = new Padding(0, 2, 0, 10) };
            background.Dock = DockStyle.Fill; background.SizeMode = PictureBoxSizeMode.Zoom; bgCard.Controls.Add(background);
            preview.Controls.Add(bgCard, 0, 1); preview.SetColumnSpan(bgCard, 2);

            ModernCard coverCard = new ModernCard { Dock = DockStyle.Fill, Tag = "media", Padding = new Padding(4), Margin = new Padding(0, 0, 12, 0) };
            cover.Dock = DockStyle.Fill; cover.SizeMode = PictureBoxSizeMode.Zoom; coverCard.Controls.Add(cover);
            preview.Controls.Add(coverCard, 0, 2);
            SetupMetadataTable(previewValues);
            previewValues.Margin = new Padding(8, 0, 0, 0);
            preview.Controls.Add(previewValues, 1, 2);
            Label previewFooter = new Label { Text = "Artwork and metadata are read before build whenever the source format allows it.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            preview.Controls.Add(previewFooter, 0, 3); preview.SetColumnSpan(previewFooter, 2);
            previewCard.Controls.Add(preview);
            left.Controls.Add(previewCard, 0, 1);
            dashboard.Controls.Add(left, 0, 0);

            // RIGHT: configuration + actions + live activity.
            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = Padding.Empty, Margin = new Padding(10, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 356));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard settings = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label configHead = new Label { Text = "BUILD SETTINGS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" };
            options.Controls.Add(configHead, 0, 0); options.SetColumnSpan(configHead, 2);
            options.Controls.Add(LabeledField("Output folder", outputFolder), 0, 1); options.Controls.Add(browseOutput, 1, 1);
            options.Controls.Add(LabeledField("Temporary folder", temporary), 0, 2); options.Controls.Add(browseTemporary, 1, 2);
            pkgPasscodeRow = LabeledField("PKG passcode (32 characters)", pkgPasscode);
            pkgPasscodeRow.Visible = false; options.Controls.Add(pkgPasscodeRow, 0, 3); options.SetColumnSpan(pkgPasscodeRow, 2);
            options.Controls.Add(new Label { Text = "TARGET FIRMWARE / SDK", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold), Tag = "muted" }, 0, 4); options.SetColumnSpan(options.GetControlFromPosition(0, 4), 2);
            targetSdk.Dock = DockStyle.Fill; targetSdk.Margin = new Padding(0, 4, 0, 4); options.Controls.Add(targetSdk, 0, 5); options.SetColumnSpan(targetSdk, 2);
            verifyCopy.Margin = new Padding(0, 5, 0, 0); options.Controls.Add(verifyCopy, 0, 6); options.SetColumnSpan(verifyCopy, 2);
            settings.Controls.Add(options);
            right.Controls.Add(settings, 0, 0);

            ModernCard actions = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel actionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            build.Dock = DockStyle.Fill; build.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold); build.Margin = new Padding(0, 0, 8, 6);
            cancel.Dock = DockStyle.Fill; cancel.Margin = new Padding(0, 0, 0, 6);
            openOutput.Dock = DockStyle.Fill; openOutput.Margin = new Padding(0, 0, 8, 0);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.Margin = new Padding(6, 0, 0, 0); status.Tag = "muted";
            actionGrid.Controls.Add(build, 0, 0); actionGrid.Controls.Add(cancel, 1, 0); actionGrid.Controls.Add(openOutput, 0, 1); actionGrid.Controls.Add(status, 1, 1);
            actions.Controls.Add(actionGrid); right.Controls.Add(actions, 0, 1);

            progress.Dock = DockStyle.Fill; progress.Height = 8; right.Controls.Add(progress, 0, 2);

            ModernCard activity = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = new Padding(0, 10, 0, 0) };
            TableLayoutPanel activityGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            activityGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); activityGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            activityGrid.Controls.Add(new Label { Text = "ACTIVITY", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            log.Font = new Font("Cascadia Mono", 8.75f, FontStyle.Regular); log.BorderStyle = BorderStyle.None; log.Margin = Padding.Empty;
            activityGrid.Controls.Add(log, 0, 1); activity.Controls.Add(activityGrid); right.Controls.Add(activity, 0, 3);
            dashboard.Controls.Add(right, 1, 0);

            dashboard.AllowDrop = true;
            dashboard.DragEnter += BuildDragEnter;
            dashboard.DragDrop += async delegate(object sender, DragEventArgs args) { await BuildDragDrop(args); };
            buildWorkspace = dashboard;
        }

        private Control LabeledField(string caption, TextBox field)
        {
            TableLayoutPanel box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 8, 2), Padding = Padding.Empty };
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            box.Controls.Add(new Label { Text = caption.ToUpperInvariant(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 8.0f, FontStyle.Bold), Tag = "muted" }, 0, 0);
            field.Dock = DockStyle.Fill; field.Margin = new Padding(0, 2, 0, 0); box.Controls.Add(field, 0, 1); return box;
        }

        private void BuildDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private async Task BuildDragDrop(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            string path = paths[0];
            if (sourceMode == SourceMode.Folder)
            {
                if (!Directory.Exists(path)) { MessageBox.Show(form, "Drop a prepared source folder on this page."); return; }
                try
                {
                    LoadSource(SourceInspector.InspectFolder(path));
                    status.Text = "Source ready - configure options and click Build PKG";
                    Log("Prepared folder loaded. Configure the build options, then click Build PKG. Dump copy remains disabled.");
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Import source", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            if (!File.Exists(path)) return;
            try
            {
                if (sourceMode == SourceMode.Pkg)
                {
                    if (!Path.GetExtension(path).Equals(".pkg", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Drop a .pkg file on this page.");
                    importPath.Text = path; status.Text = "Reading PKG preview...";
                    SourceInfo pkgInfo = await Task.Run(delegate { return SourceInspector.InspectPkg(path, pkgPasscode.Text); });
                    LoadSource(pkgInfo);
                    return;
                }
                ImageFormat detected = ImageFormats.Detect(path);
                if (detected != SelectedImageFormat()) throw new InvalidDataException("This file belongs to a different source section. Detected: " + ImageFormats.Name(detected) + ".");

                if (sourceMode == SourceMode.Exfat)
                {
                    LoadSource(CreateDirectImageSource(path, "exFAT"));
                    status.Text = "Source ready - configure options and click Build PKG";
                    Log("exFAT image loaded. Configure the build options, then click Build PKG. Preview/dump copy remains disabled.");
                    return;
                }

                importPath.Text = path; status.Text = "Reading preview...";
                SourceInfo info = await Task.Run(delegate { return PreviewImage(path); });
                LoadSource(info);
            }
            catch (Exception error) { MessageBox.Show(form, error.Message, "Import source", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void AttachWorkspace(Panel target)
        {
            if (buildWorkspace == null || target == null) return;
            if (buildWorkspace.Parent == target) return;
            if (buildWorkspace.Parent != null) buildWorkspace.Parent.Controls.Remove(buildWorkspace);
            target.Controls.Add(buildWorkspace);
            buildWorkspace.Dock = DockStyle.Fill;
            buildWorkspace.BringToFront();
            target.PerformLayout();
        }

        private void ActivateSourceTab()
        {
            // Kept for binary/source compatibility with older builds. UX 2.0 uses the custom sidebar.
        }

        private void ConfigureSourceMode()
        {
            bool folder = sourceMode == SourceMode.Folder;
            importFolder.Visible = folder;
            importFile.Visible = !folder;
            if (pkgPasscodeRow != null) pkgPasscodeRow.Visible = sourceMode == SourceMode.Pkg;
            if (folder)
            {
                importFolder.Text = "Import folder";
                build.Text = "Build PKG";
            }
            else if (sourceMode == SourceMode.Exfat)
            {
                importFile.Text = "Import exFAT";
                build.Text = "Convert exFAT → PKG";
            }
            else if (sourceMode == SourceMode.Pfs)
            {
                importFile.Text = "Import FFPFS / FFPFSC";
                build.Text = "Convert FFPFS / FFPFSC → PKG";
            }
            else if (sourceMode == SourceMode.Ufs2)
            {
                importFile.Text = "Import FFPKG";
                build.Text = "Convert FFPKG → PKG";
            }
            else
            {
                importFile.Text = "Import any PKG";
                build.Text = "Convert PKG → PKG";
            }
        }

        private ImageFormat SelectedImageFormat()
        {
            if (sourceMode == SourceMode.Exfat) return ImageFormat.Exfat;
            if (sourceMode == SourceMode.Pfs) return ImageFormat.Pfs;
            if (sourceMode == SourceMode.Ufs2) return ImageFormat.Ufs2;
            return ImageFormat.Auto;
        }

        private void BuildExtractorPage()
        {
            extractorPage.Padding = new Padding(18, 10, 18, 18);
            extractorImport.Tag = "primary-large";
            extractorBrowse.Tag = "ghost";
            extractorStart.Tag = "primary";
            extractorCancel.Tag = "ghost";
            extractorOpen.Tag = "ghost";
            extractorPasscode.UseSystemPasswordChar = true;
            extractorPasscode.PasswordChar = '●';
            extractorStatus.Tag = "muted";
            extractorLog.Font = new Font("Cascadia Mono", 8.75f, FontStyle.Regular);
            extractorLog.BorderStyle = BorderStyle.None;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Padding.Empty, Margin = Padding.Empty, AllowDrop = true };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard source = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel sourceGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sourceGrid.Controls.Add(new Label { Text = "PKG SOURCE", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            Label sourceHint = new Label { Text = "Choose a PS5 .pkg or drag it onto this page", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            sourceGrid.Controls.Add(sourceHint, 1, 0); sourceGrid.SetColumnSpan(sourceHint, 2);
            sourceGrid.Controls.Add(new Label { Text = "Package", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.0f, FontStyle.Bold) }, 0, 1);
            extractorPath.Margin = new Padding(0, 4, 10, 4); sourceGrid.Controls.Add(extractorPath, 1, 1);
            extractorImport.Dock = DockStyle.Fill; extractorImport.Margin = new Padding(0, 3, 0, 3); sourceGrid.Controls.Add(extractorImport, 2, 1);
            source.Controls.Add(sourceGrid);
            root.Controls.Add(source, 0, 0);

            TableLayoutPanel middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            ModernCard settings = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(0, 0, 10, 10) };
            TableLayoutPanel settingsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingsGrid.Controls.Add(new Label { Text = "EXTRACTION SETTINGS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0); settingsGrid.SetColumnSpan(settingsGrid.GetControlFromPosition(0, 0), 2);
            settingsGrid.Controls.Add(LabeledField("Destination folder (new)", extractorDestination), 0, 1);
            extractorBrowse.Dock = DockStyle.Fill; extractorBrowse.Margin = new Padding(0, 18, 0, 5); settingsGrid.Controls.Add(extractorBrowse, 1, 1);
            settingsGrid.Controls.Add(LabeledField("PKG passcode (32 characters)", extractorPasscode), 0, 2); settingsGrid.SetColumnSpan(settingsGrid.GetControlFromPosition(0, 2), 2);
            extractorVerify.Margin = new Padding(0, 10, 0, 0); settingsGrid.Controls.Add(extractorVerify, 0, 3); settingsGrid.SetColumnSpan(extractorVerify, 2);
            settings.Controls.Add(settingsGrid);
            middle.Controls.Add(settings, 0, 0);

            ModernCard actions = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(10, 0, 0, 10) };
            TableLayoutPanel actionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actionGrid.Controls.Add(new Label { Text = "EXTRACT", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0); actionGrid.SetColumnSpan(actionGrid.GetControlFromPosition(0, 0), 2);
            extractorStart.Dock = DockStyle.Fill; extractorStart.Font = new Font("Segoe UI Semibold", 10.25f, FontStyle.Bold); extractorStart.Margin = new Padding(0, 0, 8, 5); actionGrid.Controls.Add(extractorStart, 0, 1);
            extractorCancel.Dock = DockStyle.Fill; extractorCancel.Margin = new Padding(0, 0, 0, 5); actionGrid.Controls.Add(extractorCancel, 1, 1);
            extractorOpen.Dock = DockStyle.Fill; extractorOpen.Margin = new Padding(0, 2, 8, 2); actionGrid.Controls.Add(extractorOpen, 0, 2);
            extractorStatus.Dock = DockStyle.Fill; extractorStatus.Margin = new Padding(6, 0, 0, 0); actionGrid.Controls.Add(extractorStatus, 1, 2);
            extractorProgress.Height = 8; extractorProgress.Margin = new Padding(0, 8, 0, 8); actionGrid.Controls.Add(extractorProgress, 0, 3); actionGrid.SetColumnSpan(extractorProgress, 2);
            Label note = new Label { Text = "The original PKG is never modified. Extraction works only when the package contents are readable with the available credentials.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Tag = "muted", Font = new Font("Segoe UI", 8.75f) };
            actionGrid.Controls.Add(note, 0, 4); actionGrid.SetColumnSpan(note, 2);
            actions.Controls.Add(actionGrid);
            middle.Controls.Add(actions, 1, 0);
            root.Controls.Add(middle, 0, 1);

            ModernCard activity = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = Padding.Empty };
            TableLayoutPanel activityGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            activityGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); activityGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            activityGrid.Controls.Add(new Label { Text = "EXTRACTION ACTIVITY", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            activityGrid.Controls.Add(extractorLog, 0, 1);
            activity.Controls.Add(activityGrid);
            root.Controls.Add(activity, 0, 2);

            root.DragEnter += ExtractorDragEnter;
            root.DragDrop += delegate(object sender, DragEventArgs args) { ExtractorDragDrop(args); };
            extractorPage.Controls.Add(root);
        }

        private void BuildReaderPage()
        {
            readerPage.Padding = new Padding(18, 10, 18, 18);
            readerCover.BorderStyle = BorderStyle.None;
            readerBackground.BorderStyle = BorderStyle.None;
            readerCover.SizeMode = PictureBoxSizeMode.Zoom;
            readerBackground.SizeMode = PictureBoxSizeMode.Zoom;
            readerImport.Tag = "primary-large";
            readerClear.Tag = "ghost";
            readerPasscode.UseSystemPasswordChar = true;
            readerPasscode.PasswordChar = '●';

            // Original reader layout: a compact command bar on top, then a large artwork stage
            // and an independent details panel. This intentionally does not mirror the reference app.
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = Padding.Empty, Margin = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard command = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel commandGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            commandGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            commandGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            commandGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            commandGrid.Controls.Add(new Label { Text = "PKG READER", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            Label hint = new Label { Text = "Import a package to preview its artwork and metadata", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            commandGrid.Controls.Add(hint, 1, 0); commandGrid.SetColumnSpan(hint, 5);
            commandGrid.Controls.Add(new Label { Text = "Package", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.0f, FontStyle.Bold) }, 0, 1);
            readerPath.Dock = DockStyle.Fill; readerPath.Margin = new Padding(0, 3, 8, 3); commandGrid.Controls.Add(readerPath, 1, 1);
            readerImport.Dock = DockStyle.Fill; readerImport.Margin = new Padding(0, 2, 8, 2); commandGrid.Controls.Add(readerImport, 2, 1);
            readerClear.Dock = DockStyle.Fill; readerClear.Margin = new Padding(0, 2, 10, 2); commandGrid.Controls.Add(readerClear, 3, 1);
            readerPasscode.Dock = DockStyle.Fill; readerPasscode.Margin = new Padding(0, 3, 8, 3); readerPasscode.AccessibleName = "Reader passcode"; commandGrid.Controls.Add(readerPasscode, 4, 1);
            Label passLabel = new Label { Text = "Passcode\n(masked)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Font = new Font("Segoe UI", 8.25f) };
            commandGrid.Controls.Add(passLabel, 5, 1);
            command.Controls.Add(commandGrid);
            root.Controls.Add(command, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

            // Large visual stage. Cover is intentionally prominent and the background gets most of the page.
            ModernCard stage = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = new Padding(0, 0, 10, 0) };
            TableLayoutPanel stageGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            stageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            stageGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel identity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty, Padding = new Padding(2, 2, 2, 10) };
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard coverCard = new ModernCard { Dock = DockStyle.Fill, Tag = "media", Padding = new Padding(3), Margin = new Padding(0, 0, 14, 0) };
            coverCard.Controls.Add(readerCover); readerCover.Dock = DockStyle.Fill;
            identity.Controls.Add(coverCard, 0, 0); identity.SetRowSpan(coverCard, 3);

            readerTitle.Dock = DockStyle.Fill; readerTitle.AutoSize = false; readerTitle.Font = new Font("Segoe UI Semibold", 18.0f, FontStyle.Bold); readerTitle.TextAlign = ContentAlignment.BottomLeft;
            identity.Controls.Add(readerTitle, 1, 0);
            readerHeroContent.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular); readerHeroContent.Tag = "muted"; readerHeroContent.TextAlign = ContentAlignment.MiddleLeft;
            identity.Controls.Add(readerHeroContent, 1, 1);
            readerHeroFacts.Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold); readerHeroFacts.Tag = "muted"; readerHeroFacts.TextAlign = ContentAlignment.TopLeft;
            identity.Controls.Add(readerHeroFacts, 1, 2);
            readerHeroType.Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold); readerHeroType.Tag = "accent-text"; readerHeroType.Padding = new Padding(8, 6, 8, 6); readerHeroType.Margin = new Padding(6, 8, 0, 0);
            identity.Controls.Add(readerHeroType, 2, 0); identity.SetRowSpan(readerHeroType, 2);
            stageGrid.Controls.Add(identity, 0, 0);

            ModernCard backgroundCard = new ModernCard { Dock = DockStyle.Fill, Tag = "media", Padding = new Padding(2), Margin = Padding.Empty };
            backgroundCard.Controls.Add(readerBackground); readerBackground.Dock = DockStyle.Fill;
            stageGrid.Controls.Add(backgroundCard, 0, 1);
            stage.Controls.Add(stageGrid);
            body.Controls.Add(stage, 0, 0);

            ModernCard details = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(10, 0, 0, 0) };
            TableLayoutPanel detailsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
            detailsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            detailsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            detailsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            detailsGrid.Controls.Add(new Label { Text = "PACKAGE DETAILS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            SetupMetadataTable(readerValues); readerValues.Margin = new Padding(0, 4, 0, 8); detailsGrid.Controls.Add(readerValues, 0, 1);
            Label artworkNote = new Label { Text = "Artwork\nLarge cover + background preview from package resources", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Font = new Font("Segoe UI", 8.75f) };
            detailsGrid.Controls.Add(artworkNote, 0, 2);
            Label readerNote = new Label { Text = "READ-ONLY\nThe selected PKG is inspected without modifying its contents.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Font = new Font("Segoe UI", 8.75f) };
            detailsGrid.Controls.Add(readerNote, 0, 3);
            details.Controls.Add(detailsGrid);
            body.Controls.Add(details, 1, 0);

            root.Controls.Add(body, 0, 1);
            readerPage.Controls.Add(root);
        }

        private void BuildBackportPage()
        {
            backportPage.Padding = new Padding(18, 10, 18, 18);
            foreach (Button button in new Button[] { backportBaseBrowse, backportGameBrowse, backportFolderBrowse,
                backportPublisherBrowse, backportOutputBrowse, backportWorkBrowse, backportAddFiles, backportAddFolder,
                backportReplace, backportExtract, backportRemove, backportSelectAll, backportSelectNone,
                backportRefresh, backportSaveSelection, backportCancel }) button.Tag = "ghost";
            backportBuild.Tag = "primary";
            backportLog.Font = new Font("Cascadia Mono", 8.5f, FontStyle.Regular);
            backportLog.BorderStyle = BorderStyle.None;
            backportPasscode.UseSystemPasswordChar = true;
            backportPasscode.PasswordChar = '●';
            backportVersion.Text = "01.001.005";

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));

            ModernCard sources = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel sourceGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Margin = Padding.Empty, Padding = Padding.Empty };
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            for (int i = 1; i < 6; i++) sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            sourceGrid.Controls.Add(new Label { Text = "FILE DI PARTENZA", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            sourceGrid.Controls.Add(backportBaseInfo, 1, 0); sourceGrid.SetColumnSpan(backportBaseInfo, 2);
            AddBackportSourceRow(sourceGrid, 1, "PKG base", backportBasePath, backportBaseBrowse);
            AddBackportSourceRow(sourceGrid, 2, "Cartella gioco", backportGamePath, backportGameBrowse);
            AddBackportSourceRow(sourceGrid, 3, "Cartella backport", backportFolderPath, backportFolderBrowse);
            AddBackportSourceRow(sourceGrid, 4, "Tool Sony SDK", backportPublisherPath, backportPublisherBrowse);
            sourceGrid.Controls.Add(new Label { Text = "Passcode PKG", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) }, 0, 5);
            backportPasscode.Margin = new Padding(0, 3, 8, 3); sourceGrid.Controls.Add(backportPasscode, 1, 5);
            Label sourceNote = new Label { Text = "Scegli la cartella gioco dalla stessa versione del PKG base.", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Font = new Font("Segoe UI", 8.25f) };
            sourceGrid.Controls.Add(sourceNote, 2, 5);
            sources.Controls.Add(sourceGrid);
            root.Controls.Add(sources, 0, 0);

            TableLayoutPanel middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            ModernCard files = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(10), Margin = new Padding(0, 0, 10, 10) };
            TableLayoutPanel filesGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
            filesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            filesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            filesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel fileActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true, AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty };
            foreach (Button button in new Button[] { backportAddFiles, backportAddFolder, backportReplace, backportExtract,
                backportRemove, backportSelectAll, backportSelectNone, backportRefresh })
            { button.Height = 28; button.Margin = new Padding(0, 0, 5, 3); fileActions.Controls.Add(button); }
            filesGrid.Controls.Add(fileActions, 0, 0);
            filesGrid.Controls.Add(backportFileSummary, 0, 1);
            backportGrid.Columns.Clear();
            DataGridViewCheckBoxColumn includeColumn = new DataGridViewCheckBoxColumn { Name = "include", HeaderText = "Usa", Width = 52, ThreeState = false };
            DataGridViewTextBoxColumn pathColumn = new DataGridViewTextBoxColumn { Name = "path", HeaderText = "Percorso nel gioco", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
            DataGridViewTextBoxColumn sizeColumn = new DataGridViewTextBoxColumn { Name = "size", HeaderText = "Dimensione", ReadOnly = true, Width = 90 };
            DataGridViewTextBoxColumn stateColumn = new DataGridViewTextBoxColumn { Name = "state", HeaderText = "Stato", ReadOnly = true, Width = 112 };
            backportGrid.Columns.AddRange(new DataGridViewColumn[] { includeColumn, pathColumn, sizeColumn, stateColumn });
            backportGrid.AllowUserToResizeColumns = true;
            backportGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 83, 135);
            backportGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            backportGrid.RowTemplate.Height = 26;
            backportGrid.ContextMenuStrip = CreateBackportContextMenu();
            filesGrid.Controls.Add(backportGrid, 0, 2);
            files.Controls.Add(filesGrid);
            middle.Controls.Add(files, 0, 0);

            ModernCard settings = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = new Padding(10, 0, 0, 10) };
            TableLayoutPanel settingGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Margin = Padding.Empty, Padding = Padding.Empty };
            settingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            settingGrid.Controls.Add(new Label { Text = "VERSIONE E SALVATAGGIO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0); settingGrid.SetColumnSpan(settingGrid.GetControlFromPosition(0, 0), 2);
            settingGrid.Controls.Add(LabeledField("Versione nuova (es. 01.001.005)", backportVersion), 0, 1); settingGrid.SetColumnSpan(settingGrid.GetControlFromPosition(0, 1), 2);
            settingGrid.Controls.Add(LabeledField("Cartella output", backportOutputFolder), 0, 2);
            backportOutputBrowse.Dock = DockStyle.Fill; backportOutputBrowse.Margin = new Padding(0, 18, 0, 2); settingGrid.Controls.Add(backportOutputBrowse, 1, 2);
            settingGrid.Controls.Add(LabeledField("Cartella temporanea", backportWorkFolder), 0, 3);
            backportWorkBrowse.Dock = DockStyle.Fill; backportWorkBrowse.Margin = new Padding(0, 18, 0, 2); settingGrid.Controls.Add(backportWorkBrowse, 1, 3);
            FlowLayoutPanel compressionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
            compressionRow.Controls.Add(new Label { Text = "Compressione SDK", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Margin = new Padding(0, 8, 8, 0) });
            compressionRow.Controls.Add(backportCompression);
            settingGrid.Controls.Add(compressionRow, 0, 4); settingGrid.SetColumnSpan(compressionRow, 2);
            Label note = new Label { Text = "Salva file selezionati crea una cartella con il solo overlay.\r\nCrea PKG usa il Sony SDK: può leggere a lungo il gioco e richiedere spazio temporaneo vicino alla dimensione del base.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Tag = "muted", Font = new Font("Segoe UI", 8.5f) };
            settingGrid.Controls.Add(note, 0, 5); settingGrid.SetColumnSpan(note, 2);
            backportSaveSelection.Dock = DockStyle.Fill; backportSaveSelection.Margin = new Padding(0, 2, 8, 4); settingGrid.Controls.Add(backportSaveSelection, 0, 6);
            backportBuild.Dock = DockStyle.Fill; backportBuild.Margin = new Padding(0, 2, 0, 4); settingGrid.Controls.Add(backportBuild, 0, 7);
            backportCancel.Dock = DockStyle.Fill; backportCancel.Margin = new Padding(4, 2, 0, 4); settingGrid.Controls.Add(backportCancel, 1, 7);
            settings.Controls.Add(settingGrid);
            middle.Controls.Add(settings, 1, 0);
            root.Controls.Add(middle, 0, 1);

            ModernCard activity = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(10), Margin = Padding.Empty };
            TableLayoutPanel activityGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            activityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            activityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            activityGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            activityGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            activityGrid.Controls.Add(new Label { Text = "STATO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            activityGrid.Controls.Add(backportStatus, 1, 0);
            backportProgress.Height = 7; backportProgress.Margin = new Padding(0, 8, 8, 4); activityGrid.Controls.Add(backportProgress, 0, 1);
            activityGrid.Controls.Add(backportLog, 1, 1);
            activity.Controls.Add(activityGrid);
            root.Controls.Add(activity, 0, 2);
            backportPage.Controls.Add(root);
            string publisher = FindBackportPublisher();
            if (!string.IsNullOrEmpty(publisher)) backportPublisherPath.Text = publisher;
            backportWorkFolder.Text = Path.GetTempPath();
        }

        private static void AddBackportSourceRow(TableLayoutPanel table, int row, string label, TextBox path, Button browse)
        {
            table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) }, 0, row);
            path.Margin = new Padding(0, 3, 8, 3); table.Controls.Add(path, 1, row);
            browse.Dock = DockStyle.Fill; browse.Margin = new Padding(0, 1, 0, 1); table.Controls.Add(browse, 2, row);
        }

        private ContextMenuStrip CreateBackportContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Estrai in...", null, delegate { ExtractBackportSelection(); });
            menu.Items.Add("Sostituisci file...", null, delegate { ReplaceBackportFile(); });
            menu.Items.Add("Escludi / includi nell’aggiornamento", null, delegate { ToggleBackportSelection(); });
            menu.Items.Add("Apri posizione del file", null, delegate { OpenBackportFileLocation(); });
            return menu;
        }

        private async Task ChooseBackportBase()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "PS5 PKG (*.pkg)|*.pkg|All files (*.*)|*.*";
                dialog.Title = "Scegli il PKG base installato sulla console";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                backportBasePath.Text = dialog.FileName;
                backportSnapshot = null;
                backportBaseInfo.Text = "Leggo metadati del PKG...";
                string passcode = backportPasscode.Text;
                backportInspecting = true; RefreshUi();
                try
                {
                    PackageSnapshot snapshot = await Task.Run(delegate { return PackageSdk.InspectForBackport(dialog.FileName, passcode); });
                    backportSnapshot = snapshot;
                    backportBaseInfo.Text = snapshot.ContentId + " · versione " + snapshot.ContentVersion + " · " + FileWorkspace.Size(snapshot.Length);
                    try { backportVersion.Text = BackportVersion.Next(snapshot.ContentVersion); }
                    catch { backportVersion.Text = ""; }
                    string parent = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName));
                    backportOutputFolder.Text = parent;
                    backportWorkFolder.Text = parent;
                    UpdateBackportOutputName();
                    BackportLog("PKG base selezionato: " + dialog.FileName);
                    BackportLog("Il programma ha letto i metadati; non ha estratto il contenuto del PKG.");
                }
                catch (Exception error)
                {
                    backportSnapshot = null;
                    backportBaseInfo.Text = "PKG non leggibile in modalità rapida";
                    BackportLog("Errore lettura PKG base: " + error.Message);
                    MessageBox.Show(form, error.Message, "PKG base", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { backportInspecting = false; RefreshUi(); }
            }
        }

        private void ChooseBackportGameFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli la cartella completa del gioco (deve contenere sce_sys/param.json e sce_sys/keystone).";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    string root = Path.GetFullPath(dialog.SelectedPath);
                    string paramPath = Path.Combine(root, "sce_sys", "param.json");
                    if (!File.Exists(paramPath)) throw new FileNotFoundException("Nella cartella scelta non trovo sce_sys/param.json.", paramPath);
                    FileWorkspace.NoReparse(root);
                    FileWorkspace.NoReparse(paramPath);
                    PackageParam param = new PackageParam(File.ReadAllBytes(paramPath));
                    if (backportSnapshot != null) BackportUpdateJob.ValidateGameFolderMatch(backportSnapshot, param);
                    backportGamePath.Text = root;
                    string keystone = Path.Combine(root, "sce_sys", "keystone");
                    if (File.Exists(keystone) && new FileInfo(keystone).Length == 96)
                        BackportLog("Cartella gioco selezionata: " + root);
                    else BackportLog("La cartella gioco non contiene ancora sce_sys/keystone da 96 byte.");
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Cartella gioco", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ChooseBackportFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli la cartella che contiene i file backport, con eboot.bin / sce_module / fakelib al suo interno.";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                LoadBackportFolder(dialog.SelectedPath);
            }
        }

        private void ChooseBackportPublisher()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Sony Publishing Tools (prospero-pub-cmd.exe)|prospero-pub-cmd.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*";
                dialog.Title = "Scegli prospero-pub-cmd.exe del SDK PS5";
                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    if (!Path.GetFileName(dialog.FileName).Equals("prospero-pub-cmd.exe", StringComparison.OrdinalIgnoreCase))
                    { MessageBox.Show(form, "Seleziona il file prospero-pub-cmd.exe.", "Sony SDK", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    backportPublisherPath.Text = dialog.FileName;
                    BackportLog("Tool SDK: " + dialog.FileName);
                }
            }
        }

        private void LoadBackportFolder(string path)
        {
            try
            {
                BackportFileSet set = BackportFileSet.Load(path);
                backportFolderPath.Text = Path.GetFullPath(path);
                backportFiles = set;
                RefreshBackportGrid();
                BackportLog("Cartella backport caricata: " + path);
            }
            catch (Exception error) { MessageBox.Show(form, error.Message, "Cartella backport", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RefreshBackportGrid()
        {
            backportGrid.Rows.Clear();
            if (backportFiles == null)
            {
                backportFileSummary.Text = "Nessuna cartella backport caricata";
                return;
            }
            int ready = 0, reserved = 0;
            foreach (BackportFileItem item in backportFiles.Items)
            {
                if (string.IsNullOrEmpty(item.SkipReason)) ready++; else reserved++;
                string size = File.Exists(item.EffectiveSource) ? FileWorkspace.Size(new FileInfo(item.EffectiveSource).Length) : "—";
                string state = !string.IsNullOrEmpty(item.SkipReason) ? "Riservato SDK" :
                    (item.Added ? "Nuovo" : (!string.IsNullOrEmpty(item.ReplacementSourcePath) ? "Sostituito" : "Originale"));
                int index = backportGrid.Rows.Add(item.Include, item.RelativePath, size, state);
                DataGridViewRow row = backportGrid.Rows[index]; row.Tag = item;
                if (!string.IsNullOrEmpty(item.SkipReason))
                {
                    row.Cells[0].ReadOnly = true;
                    row.Cells[0].ToolTipText = item.SkipReason;
                    row.Cells[1].ToolTipText = item.SkipReason;
                    row.DefaultCellStyle.ForeColor = Color.Gray;
                }
                else if (!string.IsNullOrEmpty(item.ReplacementSourcePath)) row.Cells[1].ToolTipText = "Sostituzione: " + item.ReplacementSourcePath;
            }
            backportFileSummary.Text = ready + " file utilizzabili · " + reserved + " non spediti · " + FileWorkspace.Size(backportFiles.IncludedBytes()) + " selezionati";
        }

        private void BackportGridCellValueChanged(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex < 0 || args.ColumnIndex != 0 || args.RowIndex >= backportGrid.Rows.Count) return;
            BackportFileItem item = backportGrid.Rows[args.RowIndex].Tag as BackportFileItem;
            if (item == null || !string.IsNullOrEmpty(item.SkipReason)) return;
            bool included = false;
            try { included = Convert.ToBoolean(backportGrid.Rows[args.RowIndex].Cells[0].Value, CultureInfo.InvariantCulture); } catch { }
            item.Include = included;
            UpdateBackportSummary();
        }

        private void BackportGridCellMouseDown(object sender, DataGridViewCellMouseEventArgs args)
        {
            if (args.Button == MouseButtons.Right && args.RowIndex >= 0 && !backportGrid.Rows[args.RowIndex].Selected)
            {
                backportGrid.ClearSelection();
                backportGrid.Rows[args.RowIndex].Selected = true;
                backportGrid.CurrentCell = backportGrid.Rows[args.RowIndex].Cells[Math.Max(0, args.ColumnIndex)];
            }
        }

        private List<BackportFileItem> SelectedBackportItems()
        {
            List<BackportFileItem> result = new List<BackportFileItem>();
            foreach (DataGridViewRow row in backportGrid.SelectedRows)
            {
                BackportFileItem item = row.Tag as BackportFileItem;
                if (item != null && !result.Contains(item)) result.Add(item);
            }
            if (result.Count == 0 && backportGrid.CurrentRow != null)
            {
                BackportFileItem item = backportGrid.CurrentRow.Tag as BackportFileItem;
                if (item != null) result.Add(item);
            }
            return result;
        }

        private void AddBackportFiles()
        {
            if (backportFiles == null) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true; dialog.Title = "Scegli file da aggiungere al backport";
                dialog.Filter = "All files (*.*)|*.*";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    foreach (string file in dialog.FileNames)
                    {
                        string relative = PromptBackportPath("Percorso del file dentro il gioco", Path.GetFileName(file));
                        if (relative == null) continue;
                        BackportFileItem existing = backportFiles.Find(relative);
                        if (existing != null)
                        {
                            DialogResult replace = MessageBox.Show(form, "Esiste già " + relative + ". Vuoi sostituirlo con il file scelto?", "Sostituisci file", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                            if (replace == DialogResult.Yes) backportFiles.Replace(relative, file);
                        }
                        else backportFiles.Add(file, relative);
                    }
                    RefreshBackportGrid(); BackportLog("File aggiunti alla lista di lavoro.");
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Aggiungi file", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void AddBackportFolder()
        {
            if (backportFiles == null) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli una cartella di file da aggiungere mantenendo la struttura al suo interno.";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                string prefix = PromptBackportPath("Cartella di destinazione nel gioco (lascia vuoto per la radice)", "");
                if (prefix == null) return;
                try { backportFiles.AddFolder(dialog.SelectedPath, prefix); RefreshBackportGrid(); BackportLog("Cartella aggiunta alla lista: " + dialog.SelectedPath); }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Aggiungi cartella", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ReplaceBackportFile()
        {
            List<BackportFileItem> selected = SelectedBackportItems();
            if (selected.Count != 1) { MessageBox.Show(form, "Seleziona una riga da sostituire.", "Sostituisci", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            BackportFileItem item = selected[0];
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "All files (*.*)|*.*"; dialog.Title = "Scegli il nuovo file per " + item.RelativePath;
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try { backportFiles.Replace(item.RelativePath, dialog.FileName); RefreshBackportGrid(); BackportLog("Sostituzione impostata: " + item.RelativePath); }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Sostituisci", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExtractBackportSelection()
        {
            List<BackportFileItem> selected = SelectedBackportItems();
            if (selected.Count == 0) { MessageBox.Show(form, "Seleziona almeno un file da estrarre.", "Estrai", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli una cartella esistente. I percorsi originali verranno mantenuti.";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try { backportFiles.Extract(selected, dialog.SelectedPath, CancellationToken.None); BackportLog("File estratti in: " + dialog.SelectedPath); }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Estrai", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExcludeBackportSelection()
        {
            List<BackportFileItem> selected = SelectedBackportItems();
            if (selected.Count == 0) return;
            foreach (BackportFileItem item in selected) if (string.IsNullOrEmpty(item.SkipReason)) item.Include = false;
            RefreshBackportGrid();
        }

        private void ToggleBackportSelection()
        {
            List<BackportFileItem> selected = SelectedBackportItems();
            if (selected.Count == 0) return;
            bool include = false;
            foreach (BackportFileItem item in selected) if (string.IsNullOrEmpty(item.SkipReason) && !item.Include) include = true;
            foreach (BackportFileItem item in selected) if (string.IsNullOrEmpty(item.SkipReason)) item.Include = include;
            RefreshBackportGrid();
        }

        private void SetBackportSelection(bool include)
        {
            if (backportFiles == null) return;
            foreach (BackportFileItem item in backportFiles.Items) if (string.IsNullOrEmpty(item.SkipReason)) item.Include = include;
            RefreshBackportGrid();
        }

        private void UpdateBackportSummary()
        {
            if (backportFiles == null) return;
            int ready = 0, reserved = 0;
            foreach (BackportFileItem item in backportFiles.Items) { if (string.IsNullOrEmpty(item.SkipReason)) ready++; else reserved++; }
            backportFileSummary.Text = ready + " file utilizzabili · " + reserved + " non spediti · " + FileWorkspace.Size(backportFiles.IncludedBytes()) + " selezionati";
        }

        private void ReloadBackportFolder()
        {
            if (string.IsNullOrWhiteSpace(backportFolderPath.Text)) return;
            DialogResult answer = MessageBox.Show(form, "Ricaricando la cartella si perdono sostituzioni e selezioni fatte in questa schermata. Continuare?", "Ricarica", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) LoadBackportFolder(backportFolderPath.Text);
        }

        private void SaveBackportSelection()
        {
            if (backportFiles == null) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli una cartella in cui creare la cartella backport selezionata.";
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                string output = Path.Combine(dialog.SelectedPath, "backport-selected");
                try
                {
                    int count = backportFiles.Stage(output, BackportLog, CancellationToken.None);
                    BackportLog("Overlay selezionato salvato: " + output + " (" + count + " file).");
                    MessageBox.Show(form, "Ho salvato " + count + " file in:\r\n" + output, "File selezionati", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Salva file", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void OpenBackportFileLocation()
        {
            List<BackportFileItem> selected = SelectedBackportItems();
            if (selected.Count == 0) return;
            string source = selected[0].EffectiveSource;
            if (File.Exists(source))
                try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(source)) { UseShellExecute = true }); } catch { }
        }

        private string PromptBackportPath(string title, string initial)
        {
            using (Form prompt = new Form())
            using (TextBox path = new TextBox())
            using (Button ok = new Button())
            using (Button cancelPrompt = new Button())
            {
                prompt.Text = title; prompt.FormBorderStyle = FormBorderStyle.FixedDialog; prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MinimizeBox = false; prompt.MaximizeBox = false; prompt.ShowInTaskbar = false; prompt.ClientSize = new Size(520, 112);
                Label label = new Label { Text = "Percorso relativo al gioco, ad esempio fakelib/nuova.prx", Left = 12, Top = 12, Width = 496, Height = 22 };
                path.Text = initial ?? string.Empty; path.Left = 12; path.Top = 38; path.Width = 496;
                ok.Text = "OK"; ok.Left = 350; ok.Top = 72; ok.Width = 76; ok.DialogResult = DialogResult.OK;
                cancelPrompt.Text = "Annulla"; cancelPrompt.Left = 432; cancelPrompt.Top = 72; cancelPrompt.Width = 76; cancelPrompt.DialogResult = DialogResult.Cancel;
                prompt.Controls.AddRange(new Control[] { label, path, ok, cancelPrompt }); prompt.AcceptButton = ok; prompt.CancelButton = cancelPrompt;
                return prompt.ShowDialog(form) == DialogResult.OK ? path.Text : null;
            }
        }

        private string BackportOutputPath()
        {
            if (string.IsNullOrWhiteSpace(backportBasePath.Text) || string.IsNullOrWhiteSpace(backportOutputFolder.Text)) return string.Empty;
            string stem = Path.GetFileNameWithoutExtension(backportBasePath.Text);
            return Path.Combine(backportOutputFolder.Text, stem + "-backport.pkg");
        }

        private void UpdateBackportOutputName()
        {
            string output = BackportOutputPath();
            if (!string.IsNullOrEmpty(output)) BackportLog("PKG aggiornamento previsto: " + output);
        }

        private async Task StartBackportBuild()
        {
            if (backportBusy || backportInspecting) return;
            string output = BackportOutputPath();
            if (backportSnapshot == null || backportFiles == null || string.IsNullOrWhiteSpace(backportGamePath.Text))
            { MessageBox.Show(form, "Scegli il PKG base, la cartella gioco e la cartella backport.", "Crea aggiornamento", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (string.IsNullOrWhiteSpace(backportPublisherPath.Text) || !File.Exists(backportPublisherPath.Text))
            { MessageBox.Show(form, "Seleziona prospero-pub-cmd.exe del Sony SDK.", "Sony SDK", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (File.Exists(output) || Directory.Exists(output))
            { MessageBox.Show(form, "Il file esiste già:\r\n" + output + "\r\nScegli un’altra cartella di output per non sovrascriverlo.", "Output già presente", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(form, "Verrà creato un PKG aggiornamento separato. Il PKG base resterà invariato.\r\n\r\nIl Sony SDK può leggere a lungo la cartella gioco e usare molto spazio temporaneo. Vuoi continuare?", "Crea aggiornamento", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            BackportUpdateRequest request = new BackportUpdateRequest();
            request.BasePackage = backportBasePath.Text; request.GameFolder = backportGamePath.Text;
            request.BackportFolder = backportFolderPath.Text; request.OutputPackage = output;
            request.WorkParent = string.IsNullOrWhiteSpace(backportWorkFolder.Text) ? Path.GetDirectoryName(backportBasePath.Text) : backportWorkFolder.Text;
            request.PublisherTool = backportPublisherPath.Text; request.Passcode = backportPasscode.Text;
            request.ContentVersion = backportVersion.Text; request.CompressionLevel = (int)backportCompression.Value;
            request.Files = backportFiles;
            backportBusy = true; backportCancellation = new CancellationTokenSource(); backportLog.Clear(); backportLatest = null;
            RefreshUi(); BackportLog("Avvio preparazione aggiornamento per " + backportSnapshot.ContentId + ".");
            try
            {
                string created = await Task.Run(delegate
                {
                    return BackportUpdateJob.Run(request, backportCancellation.Token,
                        delegate(ConversionProgress value) { Interlocked.Exchange(ref backportLatest, value); }, BackportLog);
                });
                backportStatus.Text = "Aggiornamento creato";
                MessageBox.Show(form, "PKG aggiornamento creato:\r\n" + created + "\r\n\r\nInstalla solo sul PKG base usato come riferimento.",
                    "Fatto", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { backportStatus.Text = "Annullato"; BackportLog("Creazione annullata."); }
            catch (Exception error)
            {
                backportStatus.Text = "Errore"; BackportLog("ERRORE: " + error.Message);
                MessageBox.Show(form, error.Message, "Crea aggiornamento", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { if (backportCancellation != null) backportCancellation.Dispose(); backportCancellation = null; backportBusy = false; RefreshUi(); }
        }

        private static string FindBackportPublisher()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[] { Path.Combine(baseDirectory, "toolchain", "prospero-pub-cmd.exe"),
                Path.Combine(baseDirectory, "prospero-pub-cmd.exe") };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(directory.Trim(), "prospero-pub-cmd.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return string.Empty;
        }

        private void BuildTrophyUnlockerPage()
        {
            trophyPage.Padding = new Padding(24);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

            ModernCard connection = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel connectGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
            connectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            connectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            connectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
            connectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            connectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            connectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            connectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            connectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            connectGrid.Controls.Add(new Label { Text = "PS5 TROPHY2", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            Label info = new Label { Text = "Connect to the console and detect the currently running PS5 game", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            connectGrid.Controls.Add(info, 1, 0); connectGrid.SetColumnSpan(info, 4);
            connectGrid.Controls.Add(new Label { Text = "PS5 IP Address", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.0f, FontStyle.Bold) }, 0, 1);
            trophyIp.Margin = new Padding(0, 4, 10, 4); connectGrid.Controls.Add(trophyIp, 1, 1);
            trophyDetect.Dock = DockStyle.Fill; trophyDetect.Margin = new Padding(0, 2, 10, 2); connectGrid.Controls.Add(trophyDetect, 2, 1);
            trophyDisconnect.Dock = DockStyle.Fill; trophyDisconnect.Margin = new Padding(0, 2, 10, 2); connectGrid.Controls.Add(trophyDisconnect, 3, 1);
            trophyConnection.Margin = new Padding(8, 0, 0, 0); connectGrid.Controls.Add(trophyConnection, 4, 1);
            connectGrid.Controls.Add(new Label { Text = "Active Game", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" }, 0, 2);
            connectGrid.Controls.Add(trophyActiveGame, 1, 2); connectGrid.SetColumnSpan(trophyActiveGame, 4);
            connection.Controls.Add(connectGrid);
            root.Controls.Add(connection, 0, 0);

            ModernCard toolbar = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel toolGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            toolGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            toolGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolGrid.Controls.Add(new Label { Text = "TROPHY LIST", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            toolGrid.Controls.Add(trophySummary, 1, 0);
            toolbar.Controls.Add(toolGrid);
            root.Controls.Add(toolbar, 0, 1);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            ModernCard listCard = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(10), Margin = new Padding(0, 0, 10, 10) };
            trophyGrid.Columns.Clear();
            trophyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrophyId", HeaderText = "ID", Width = 54, SortMode = DataGridViewColumnSortMode.NotSortable });
            trophyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrophyName", HeaderText = "Trophy", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220, SortMode = DataGridViewColumnSortMode.NotSortable });
            trophyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrophyGrade", HeaderText = "Grade", Width = 92, SortMode = DataGridViewColumnSortMode.NotSortable });
            trophyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrophyStatus", HeaderText = "Status", Width = 104, SortMode = DataGridViewColumnSortMode.NotSortable });
            trophyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrophyGroup", HeaderText = "Group", Width = 116, SortMode = DataGridViewColumnSortMode.NotSortable });
            trophyGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            trophyGrid.RowTemplate.Height = 30;
            trophyGrid.ColumnHeadersHeight = 34;
            listCard.Controls.Add(trophyGrid);
            body.Controls.Add(listCard, 0, 0);

            ModernCard actions = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(16), Margin = new Padding(10, 0, 0, 10) };
            TableLayoutPanel actionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty, Padding = Padding.Empty };
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            actionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actionGrid.Controls.Add(new Label { Text = "ACTIONS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            trophyGetList.Dock = DockStyle.Fill; trophyGetList.Margin = new Padding(0, 2, 0, 4); actionGrid.Controls.Add(trophyGetList, 0, 1);
            trophyUnlock.Margin = new Padding(0, 4, 0, 4); actionGrid.Controls.Add(trophyUnlock, 0, 2);
            trophyUnlockAll.Margin = new Padding(0, 4, 0, 4); actionGrid.Controls.Add(trophyUnlockAll, 0, 4);
            actionGrid.Controls.Add(new Label { Text = "Refresh updates Trophy2 status.\r\nUnlock uses exact trophy IDs. Unlock All processes real catalog IDs, non-Platinum first and Platinum last. UDS titles may require the game to exit once so Trophy2 can commit.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Tag = "muted", Font = new Font("Segoe UI", 8.5f) }, 0, 5);
            actions.Controls.Add(actionGrid);
            body.Controls.Add(actions, 1, 0);
            root.Controls.Add(body, 0, 2);

            ModernCard logCard = new ModernCard { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(12), Margin = Padding.Empty };
            TableLayoutPanel logGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            logGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            logGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            logGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logGrid.Controls.Add(new Label { Text = "TROPHY LOG", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), Tag = "accent-text" }, 0, 0);
            trophyClearLog.Dock = DockStyle.Fill; trophyClearLog.Margin = new Padding(4, 0, 0, 4);
            logGrid.Controls.Add(trophyClearLog, 1, 0);
            trophyLog.Text = "Ready.\r\n";
            logGrid.Controls.Add(trophyLog, 0, 1); logGrid.SetColumnSpan(trophyLog, 2);
            logCard.Controls.Add(logGrid);
            root.Controls.Add(logCard, 0, 3);

            trophyPage.Controls.Add(root);
        }

        private async Task DetectTrophyGame()
        {
            if (trophyBusy) return;
            string host = trophyIp.Text.Trim();
            SetTrophyBusy(true, "Connecting...");
            TrophyLog("Connecting to " + host + "...");
            try
            {
                string ppsa = await Task.Run(delegate { return trophyEngine.DetectActiveGame(host, TrophyLogThreadSafe); });
                trophyDetectedPpsa = ppsa;
                trophyConnected = true;
                currentTrophyList = null;
                trophyGrid.Rows.Clear();
                trophyActiveGame.Text = ppsa;
                trophyConnection.Text = "Connected";
                trophyConnection.Tag = "accent-text";
                trophySummary.Text = "Reading trophy list...";
                TrophyLog("Connection ready for " + ppsa + ". Loading trophy list automatically...");

                try
                {
                    TrophyListResult result = await Task.Run(delegate { return trophyEngine.GetTrophyList(host, ppsa, TrophyLogThreadSafe); });
                    ApplyTrophyList(result);
                    TrophyLog("Trophy list loaded automatically: " + result.Total + " trophies.");
                }
                catch (Exception listError)
                {
                    currentTrophyList = null;
                    trophyGrid.Rows.Clear();
                    trophySummary.Text = "Game detected · trophy list unavailable · use Refresh Trophy List";
                    TrophyLog("TROPHY LIST ERROR: " + listError.Message);
                    MessageBox.Show(form, "Active game detected (" + ppsa + "), but the trophy list could not be loaded automatically.\r\n\r\n" + listError.Message + "\r\n\r\nYou can retry with Refresh Trophy List.", "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception error)
            {
                trophyConnected = false;
                trophyDetectedPpsa = string.Empty;
                currentTrophyList = null;
                trophyGrid.Rows.Clear();
                trophyActiveGame.Text = "—";
                trophyConnection.Text = "Disconnected";
                trophyConnection.Tag = "muted";
                trophySummary.Text = "No trophy list loaded";
                TrophyLog("ERROR: " + error.Message);
                MessageBox.Show(form, error.Message, "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetTrophyBusy(false, string.Empty);
                if (themeManager != null) themeManager.Apply(theme.SelectedIndex == 1);
            }
        }

        private void DisconnectTrophy()
        {
            if (trophyBusy) return;
            trophyEngine.ResetSession();
            trophyConnected = false;
            trophyDetectedPpsa = string.Empty;
            currentTrophyList = null;
            trophyGrid.Rows.Clear();
            trophyActiveGame.Text = "—";
            trophyConnection.Text = "Disconnected";
            trophyConnection.Tag = "muted";
            trophySummary.Text = "Disconnected · detect an active game to continue";
            TrophyLog("Disconnected.");
            RefreshTrophyButtons();
            if (themeManager != null) themeManager.Apply(theme.SelectedIndex == 1);
        }

        private async Task GetTrophyList()
        {
            if (trophyBusy || !trophyConnected || string.IsNullOrEmpty(trophyDetectedPpsa)) return;
            string host = trophyIp.Text.Trim();
            SetTrophyBusy(true, "Reading trophy list...");
            try
            {
                TrophyListResult result;
                if (currentTrophyList != null)
                {
                    TrophyLog("Refreshing Trophy2 state...");
                    result = await Task.Run(delegate { return trophyEngine.RefreshTrophyState(host, currentTrophyList, TrophyLogThreadSafe); });
                }
                else
                {
                    result = await Task.Run(delegate { return trophyEngine.GetTrophyList(host, trophyDetectedPpsa, TrophyLogThreadSafe); });
                }
                ApplyTrophyList(result);
            }
            catch (Exception error)
            {
                TrophyLog("ERROR: " + error.Message);
                MessageBox.Show(form, error.Message, "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetTrophyBusy(false, string.Empty); }
        }

        private async Task UnlockSelectedTrophy()
        {
            if (trophyBusy || currentTrophyList == null || trophyGrid.SelectedRows.Count == 0) return;
            TrophyItem selected = trophyGrid.SelectedRows[0].Tag as TrophyItem;
            if (selected == null || selected.Unlocked) return;
            string host = trophyIp.Text.Trim();
            SetTrophyBusy(true, "Unlocking trophy " + selected.Id + "...");
            try
            {
                TrophyListResult result = await Task.Run(delegate { return trophyEngine.UnlockSingle(host, currentTrophyList, selected.Id, TrophyLogThreadSafe); });
                ApplyTrophyList(result, selected.Id);
                if (result.CommitPending)
                    result = await CompleteTrophyCommitIfNeeded(host, result, selected.Id);
                TrophyItem verified = FindTrophy(result, selected.Id);
                if (verified != null && verified.Unlocked)
                    TrophyLog("Unlocked: " + selected.Name);
                else
                    TrophyLog("Unlock command completed, but the trophy is still reported as locked. See TROPHY LOG for the payload diagnostic.");
            }
            catch (Exception error)
            {
                TrophyLog("ERROR: " + error.Message);
                MessageBox.Show(form, error.Message, "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetTrophyBusy(false, string.Empty); }
        }

        private async Task UnlockAllTrophies()
        {
            if (trophyBusy || currentTrophyList == null) return;
            string host = trophyIp.Text.Trim();
            SetTrophyBusy(true, "Unlocking all trophies...");
            try
            {
                TrophyListResult result = await Task.Run(delegate { return trophyEngine.UnlockAll(host, currentTrophyList, TrophyLogThreadSafe); });
                ApplyTrophyList(result);
                if (result.CommitPending)
                    result = await CompleteTrophyCommitIfNeeded(host, result, -1);
                int locked = 0;
                foreach (TrophyItem item in result.Trophies) if (!item.Unlocked) locked++;
                if (locked == 0)
                    MessageBox.Show(form, "All listed trophies are unlocked.", "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(form, locked + " listed trophy/trophies are still locked. See the log for details.", "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception error)
            {
                TrophyLog("ERROR: " + error.Message);
                MessageBox.Show(form, error.Message, "Trophy Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetTrophyBusy(false, string.Empty); }
        }

        private async Task<TrophyListResult> CompleteTrophyCommitIfNeeded(string host, TrophyListResult result, int selectId)
        {
            if (result == null || !result.CommitPending) return result;
            DialogResult answer = MessageBox.Show(form,
                "This title is using the UDS Trophy2 route. Some games only commit the new trophy state when the active game exits.\r\n\r\nClose the game on the PS5, then click OK. The tool will verify the Trophy2 state automatically.\r\n\r\nChoose Cancel if you want to do this later and use Refresh Trophy List.",
                "Trophy2 commit required", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer != DialogResult.OK)
            {
                TrophyLog("UDS commit verification postponed. Close the game and use Refresh Trophy List.");
                return result;
            }

            trophySummary.Text = "Waiting for game exit / Trophy2 commit...";
            TrophyListResult committed = await Task.Run(delegate { return trophyEngine.CompletePendingCommit(host, result, 30000, TrophyLogThreadSafe); });
            ApplyTrophyList(committed, selectId);
            return committed;
        }

        private void ApplyTrophyList(TrophyListResult result) { ApplyTrophyList(result, -1); }

        private void ApplyTrophyList(TrophyListResult result, int selectId)
        {
            currentTrophyList = result;
            trophyGrid.Rows.Clear();
            foreach (TrophyItem item in result.Trophies)
            {
                int index = trophyGrid.Rows.Add(item.Id, item.Name, item.GradeName, item.StatusName, item.GroupName);
                trophyGrid.Rows[index].Tag = item;
                if (item.Id == selectId) trophyGrid.Rows[index].Selected = true;
            }
            string state = result.StateKnown ? result.UnlockedCount + "/" + result.Total + " unlocked" : result.Total + " trophies · state unavailable";
            trophySummary.Text = result.GameTitle + " · " + state + " · " + result.Npwr;
            trophyActiveGame.Text = result.Ppsa + (string.IsNullOrWhiteSpace(result.GameTitle) ? string.Empty : " · " + result.GameTitle);
            RefreshTrophyButtons();
        }

        private static TrophyItem FindTrophy(TrophyListResult list, int id)
        {
            if (list == null) return null;
            foreach (TrophyItem item in list.Trophies) if (item.Id == id) return item;
            return null;
        }

        private void SetTrophyBusy(bool value, string message)
        {
            trophyBusy = value;
            if (!string.IsNullOrEmpty(message)) trophySummary.Text = message;
            RefreshTrophyButtons();
        }

        private void RefreshTrophyButtons()
        {
            trophyIp.ReadOnly = trophyBusy || trophyConnected;
            trophyDetect.Enabled = !trophyBusy && !trophyConnected;
            trophyDisconnect.Enabled = !trophyBusy && trophyConnected;
            trophyClearLog.Enabled = !trophyBusy;
            trophyGetList.Enabled = !trophyBusy && trophyConnected && !string.IsNullOrEmpty(trophyDetectedPpsa);
            bool hasList = currentTrophyList != null && currentTrophyList.Trophies.Count > 0;
            bool anyLocked = false;
            if (hasList) foreach (TrophyItem item in currentTrophyList.Trophies) if (!item.Unlocked) { anyLocked = true; break; }
            trophyUnlockAll.Enabled = !trophyBusy && trophyConnected && hasList && anyLocked;
            TrophyItem selected = null;
            if (trophyGrid.SelectedRows.Count > 0) selected = trophyGrid.SelectedRows[0].Tag as TrophyItem;
            trophyUnlock.Enabled = !trophyBusy && trophyConnected && selected != null && !selected.Unlocked;
        }

        private void TrophyLogThreadSafe(string text)
        {
            if (form.IsDisposed) return;
            if (form.InvokeRequired)
            {
                try { form.BeginInvoke(new Action<string>(TrophyLog), text); } catch { }
                return;
            }
            TrophyLog(text);
        }

        private void TrophyLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            trophyLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\r\n");
            trophyLog.SelectionStart = trophyLog.TextLength;
            trophyLog.ScrollToCaret();
        }

        private void BuildCreditsPage()
        {
            creditsPage.Padding = new Padding(28);
            creditsPage.AutoScroll = true;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label title = new Label
            {
                Text = "Credits",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 18.0f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 18)
            };

            Label body = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(900, 0),
                Font = new Font("Segoe UI", 11.0f),
                Text = "Tool developed by Zer0Day.\r\n\r\n" +
                       "Thanks to Drakmor for the original foundations / ShadowMountPlus.\r\n" +
                       "Thanks to svenGDK for LibProsperoPKG and UFS2Tool.\r\n" +
                       "Thanks to EchoStretch and cragson for the A53 exploit.\r\n\r\n" +
                       "exFAT workflow reference: kerrdec97 / ps5-exfat-builder\r\n" +
                       "FFPFS / FFPFSC / PFS reader: MkPFS / Lazy_MkPFS\r\n" +
                       "UFS2 / FFPKG reader: SvenGDK / UFS2Tool (BSD-2-Clause).",
                Margin = new Padding(0, 0, 0, 22)
            };

            ModernCard socialCard = new ModernCard
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Tag = "card",
                Padding = new Padding(18),
                Margin = new Padding(0, 0, 0, 10)
            };

            TableLayoutPanel social = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            social.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label socialTitle = new Label
            {
                Text = "ZER0DAY ONLINE",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Tag = "accent-text",
                Margin = new Padding(0, 0, 0, 10)
            };

            LinkLabel xLink = new LinkLabel
            {
                Text = "Follow Zer0Day on X  ↗",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 8),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            xLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://x.com/_zer0day__") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(form, "Could not open X.\r\n\r\n" + ex.Message, "Open link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            LinkLabel kofiLink = new LinkLabel
            {
                Text = "Support Zer0Day on Ko-fi  ↗",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            kofiLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://ko-fi.com/zzer0dayyy") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(form, "Could not open Ko-fi.\r\n\r\n" + ex.Message, "Open link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            social.Controls.Add(socialTitle, 0, 0);
            social.Controls.Add(xLink, 0, 1);
            social.Controls.Add(kofiLink, 0, 2);
            socialCard.Controls.Add(social);

            Label privacy = new Label
            {
                Text = "Links open in your default browser.",
                AutoSize = true,
                Tag = "muted",
                Font = new Font("Segoe UI", 9.0f),
                Margin = new Padding(0, 2, 0, 0)
            };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(body, 0, 1);
            layout.Controls.Add(socialCard, 0, 2);
            layout.Controls.Add(privacy, 0, 3);
            creditsPage.Controls.Add(layout);
        }

        private static void SetupMetadataTable(TableLayoutPanel table)
        {
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 2;
            table.RowCount = 10;
            table.AutoScroll = true;
            table.Margin = new Padding(8, 0, 0, 0);
            table.ColumnStyles.Clear();
            table.RowStyles.Clear();
            table.Controls.Clear();
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            string[] names = { "Content ID", "Title ID", "Region", "Version", "PKG type", "SDK", "Minimum system", "Size", "Entries", "Source" };
            for (int i = 0; i < names.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
                table.Controls.Add(new Label
                {
                    Text = names[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI", 9.0f, FontStyle.Bold)
                }, 0, i);
                table.Controls.Add(new Label
                {
                    Text = "—",
                    Name = "value" + i,
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI", 9.0f, FontStyle.Regular)
                }, 1, i);
            }
        }

        private static void AddOptionRow(TableLayoutPanel table, int row, string name, TextBox box, Button button)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.Controls.Add(new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold)
            }, 0, row);
            table.Controls.Add(box, 1, row);
            table.Controls.Add(button, 2, row);
        }

        private async Task ImportFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Select prepared PS5 source folder" })
            {
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    LoadSource(SourceInspector.InspectFolder(dialog.SelectedPath));
                    status.Text = "Source ready - configure options and click Build PKG";
                    Log("Prepared folder loaded. Configure the build options, then click Build PKG. Dump copy remains disabled.");
                }
                catch (Exception error)
                {
                    current = null;
                    status.Text = "Import failed";
                    Log("Import error: " + error.Message);
                    MessageBox.Show(form, error.Message, "Import source", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task ImportFile()
        {
            string filter;
            if (sourceMode == SourceMode.Exfat) filter = "exFAT images (*.exfat)|*.exfat|All files (*.*)|*.*";
            else if (sourceMode == SourceMode.Pfs) filter = "FFPFS / FFPFSC / PFS (*.ffpfs;*.ffpfsc;*.pfs)|*.ffpfs;*.ffpfsc;*.pfs|All files (*.*)|*.*";
            else if (sourceMode == SourceMode.Ufs2) filter = "FFPKG / UFS2 (*.ffpkg)|*.ffpkg|All files (*.*)|*.*";
            else if (sourceMode == SourceMode.Pkg) filter = "PS5 PKG (*.pkg)|*.pkg|All files (*.*)|*.*";
            else return;

            using (OpenFileDialog dialog = new OpenFileDialog { Filter = filter })
            {
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                importPath.Text = dialog.FileName; status.Text = "Reading preview...";
                try
                {
                    if (sourceMode == SourceMode.Pkg)
                    {
                        SourceInfo pkgInfo = await Task.Run(delegate { return SourceInspector.InspectPkg(dialog.FileName, pkgPasscode.Text); });
                        LoadSource(pkgInfo);
                        return;
                    }
                    ImageFormat detected = ImageFormats.Detect(dialog.FileName);
                    ImageFormat expected = SelectedImageFormat();
                    if (detected != expected)
                        throw new InvalidDataException("The selected file does not match this tab. Detected: " + ImageFormats.Name(detected) + ".");

                    if (sourceMode == SourceMode.Exfat)
                    {
                        // Do not mount/copy a preview first. The real build mounts the image once,
                        // keeps it read-only, and feeds the mounted game folder straight to the PKG builder.
                        LoadSource(CreateDirectImageSource(dialog.FileName, "exFAT"));
                        status.Text = "Source ready - configure options and click Build PKG";
                        Log("exFAT image loaded. Configure the build options, then click Build PKG. Preview/dump copy remains disabled.");
                        return;
                    }

                    SourceInfo info = await Task.Run(delegate { return PreviewImage(dialog.FileName); }); LoadSource(info);
                }
                catch (Exception error)
                {
                    current = null;
                    FillMetadata(previewValues, null); DisposePicture(cover); DisposePicture(background);
                    previewTitle.Text = "Unable to read source"; status.Text = "Import failed";
                    Log("Import error: " + error.Message);
                    MessageBox.Show(form, error.Message, "Import source", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private SourceInfo CreateDirectImageSource(string path, string sourceType)
        {
            path = Path.GetFullPath(path);
            SourceInfo info = new SourceInfo();
            info.SourcePath = path;
            info.SourceType = sourceType;
            info.Title = Path.GetFileNameWithoutExtension(path);
            info.Size = new FileInfo(path).Length;
            return info;
        }

        private SourceInfo PreviewImage(string path)
        {
            ImageFormat kind = ImageFormats.Detect(path); string work = FileWorkspace.CreateOwned(Path.GetTempPath());
            ExfatMount mount = null;
            try
            {
                string root;
                if (kind == ImageFormat.Ufs2) root = UfsExtraction.ExtractPreview(path, Path.Combine(work,"preview"), CancellationToken.None, Log);
                else if (kind == ImageFormat.Exfat)
                {
                    if (!Administrator()) throw new InvalidOperationException("Run START.cmd as Administrator to preview exFAT images.");
                    mount = new ExfatMount(ExfatMount.FindTool(), path, Log); mount.Attach(CancellationToken.None); root = FileWorkspace.GameRoot(mount.Root);
                    string dest = Path.Combine(work,"preview"); Directory.CreateDirectory(dest); Directory.CreateDirectory(Path.Combine(dest,"sce_sys"));
                    string sys = Path.Combine(root,"sce_sys"); foreach (string name in new string[] { "param.json","icon0.png","pic0.png","pic1.png","pic2.png" }) { string src=Path.Combine(sys,name); if (File.Exists(src)) File.Copy(src,Path.Combine(dest,"sce_sys",name),true); } root=dest;
                }
                else root = PfsExtraction.ExtractPreview(path, work, CancellationToken.None, Log);
                SourceInfo info = SourceInspector.InspectPreviewFolder(root, ImageFormats.Name(kind), new FileInfo(path).Length); info.SourcePath = path;
                string cache = Path.Combine(stateDirectory, "preview-cache"); Directory.CreateDirectory(cache);
                foreach (string oldFile in Directory.GetFiles(cache)) { try { File.Delete(oldFile); } catch { } }
                if (!string.IsNullOrEmpty(info.CoverPath) && File.Exists(info.CoverPath)) { string c = Path.Combine(cache, "cover" + Path.GetExtension(info.CoverPath)); File.Copy(info.CoverPath, c, true); info.CoverPath = c; }
                if (!string.IsNullOrEmpty(info.BackgroundPath) && File.Exists(info.BackgroundPath)) { string b = Path.Combine(cache, "background" + Path.GetExtension(info.BackgroundPath)); File.Copy(info.BackgroundPath, b, true); info.BackgroundPath = b; }
                return info;
            }
            finally { if (mount != null) mount.Dispose(); FileWorkspace.DeleteOwned(work, Log); }
        }

        private void LoadSource(SourceInfo info)
        {
            current = info; importPath.Text = info.SourcePath; previewTitle.Text = string.IsNullOrEmpty(info.Title) ? Path.GetFileNameWithoutExtension(info.SourcePath) : info.Title; FillMetadata(previewValues, info);
            LoadPicture(cover, info.CoverPath); LoadPicture(background, info.BackgroundPath); status.Text = "Ready";
        }

        private void ClearImport()
        { current=null; importPath.Clear(); previewTitle.Text="No source selected"; FillMetadata(previewValues,null); DisposePicture(cover); DisposePicture(background); status.Text="Ready"; }

        private async Task StartBuild()
        {
            if (busy || current == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(outputFolder.Text) || string.IsNullOrWhiteSpace(temporary.Text)) throw new ArgumentException("Choose both Output Folder and Temporary Folder before building.");
                Directory.CreateDirectory(outputFolder.Text); Directory.CreateDirectory(temporary.Text);
                ulong sdk = SdkVersion.ForBuild(targetSdk.Text);
                cancellation = new CancellationTokenSource(); busy=true; elapsed.Restart(); log.Clear(); latest=new ConversionProgress("Preparing","Checking source",0,0); lastOutput=null; lastCleanupWarning=null;
                activeLogFile = Path.Combine(stateDirectory,"logs",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Version+".txt");
                if (Directory.Exists(current.SourcePath))
                    lastOutput = await Task.Run(delegate { return BuildFolder(current.SourcePath, outputFolder.Text, temporary.Text, sdk, cancellation.Token); });
                else if (sourceMode == SourceMode.Pkg)
                {
                    string baseName = !string.IsNullOrEmpty(current.TitleId) ? current.TitleId : Path.GetFileNameWithoutExtension(current.SourcePath);
                    string final = Path.Combine(outputFolder.Text, baseName + "-rebuilt.pkg");
                    PkgRebuildRequest request = new PkgRebuildRequest { Package=current.SourcePath, Output=final, Temporary=temporary.Text, Passcode=pkgPasscode.Text, Verify=verifyCopy.Checked, TargetSdk=sdk };
                    PkgRebuildResult result = await Task.Run(delegate { return PkgRebuildJob.Run(request, cancellation.Token, delegate(string dump,string buildOutput,string scratch,CancellationToken token){ return form.Invoke(new Func<object>(delegate { return BuilderBridge.PrepareOptions(form,dump,buildOutput,scratch,token); })); }, Report, Log); });
                    lastOutput = result.Output;
                    lastCleanupWarning = result.Warning;
                }
                else
                {
                    string baseName = !string.IsNullOrEmpty(current.TitleId) ? current.TitleId : Path.GetFileNameWithoutExtension(current.SourcePath);
                    string final = Path.Combine(outputFolder.Text, baseName + ".pkg");
                    ConversionRequest request = new ConversionRequest { Image=current.SourcePath, Output=final, Temporary=temporary.Text, Tool=ExfatMount.FindTool(), VerifyCopy=verifyCopy.Checked, TargetSdk=sdk, Format=SelectedImageFormat() };
                    ConversionResult result = await Task.Run(delegate { return ConversionJob.Run(request, cancellation.Token, delegate(string dump,string buildOutput,string scratch,CancellationToken token){ return form.Invoke(new Func<object>(delegate { return BuilderBridge.PrepareOptions(form,dump,buildOutput,scratch,token); })); }, Report, Log); });
                    lastOutput=result.Output;
                    lastCleanupWarning=result.Warning;
                }
                latest=new ConversionProgress("Completed",string.IsNullOrEmpty(lastCleanupWarning)?lastOutput:"PKG ready · mount cleanup warning",1,1);
                if(string.IsNullOrEmpty(lastCleanupWarning))
                {
                    string completed = sourceMode == SourceMode.Pkg
                        ? "PKG rebuilt and verified.\r\n\r\nPackage type was auto-detected. The original source PKG was not modified.\r\n\r\n" + lastOutput
                        : "PKG created and verified.\r\n\r\nAny temporary mounted source has been released automatically.\r\n\r\n" + lastOutput;
                    MessageBox.Show(form, completed, "Build PKG", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                    MessageBox.Show(form,"PKG created and verified:\r\n"+lastOutput+"\r\n\r\n"+lastCleanupWarning+"\r\nThe tool will retry cleanup again when it closes or at the next launch.","Build PKG",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { latest=new ConversionProgress("Cancelled","Operation stopped",0,0); Log("Build cancelled."); }
            catch (Exception error) { latest=new ConversionProgress("Failed",error.Message,0,0); Log(error.ToString()); MessageBox.Show(form,error.Message,"Build failed",MessageBoxButtons.OK,MessageBoxIcon.Error); }
            finally { busy=false; elapsed.Stop(); if (cancellation!=null) { cancellation.Dispose(); cancellation=null; } RefreshUi(); }
        }

        private string BuildFolder(string source, string outputRoot, string temporaryRoot, ulong sdk, CancellationToken token)
        {
            string outputWork=null, scratch=null;
            try
            {
                outputWork=FileWorkspace.CreateOwned(outputRoot); scratch=FileWorkspace.CreateOwned(temporaryRoot); Report(new ConversionProgress("Reading metadata","Preparing builder options",0,0));
                object options=(object)form.Invoke(new Func<object>(delegate { return BuilderBridge.PrepareOptions(form,source,outputWork,scratch,token); })); BuilderBridge.Set(options,"SdkVersionOverride",sdk);
                Report(new ConversionProgress("Building PKG","Processing source folder",0,0)); string package=Path.GetFullPath(BuilderBridge.Build(options,Log));
                Report(new ConversionProgress("Verifying PKG","Checking package",0,0)); BuilderBridge.Verify(package,options,token,Log);
                string final=Path.Combine(outputRoot,Path.GetFileName(package)); if (File.Exists(final)) throw new IOException("Output PKG already exists: "+final); FileWorkspace.Commit(package,final); return final;
            }
            finally { FileWorkspace.DeleteOwned(outputWork,Log); FileWorkspace.DeleteOwned(scratch,Log); }
        }

        private void ImportExtractorPkg()
        {
            if (extractorBusy || busy) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "PS5 PKG (*.pkg)|*.pkg|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                SetExtractorPackage(dialog.FileName);
            }
        }

        private void SetExtractorPackage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !Path.GetExtension(path).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(form, "Select a valid .pkg file.", "PKG Extractor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            extractorPath.Text = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(extractorPath.Text);
            string name = Path.GetFileNameWithoutExtension(extractorPath.Text) + "_extracted";
            extractorDestination.Text = Path.Combine(parent, name);
            lastExtractorOutput = null;
            extractorStatus.Text = "Ready to extract · " + FileWorkspace.Size(new FileInfo(extractorPath.Text).Length);
            LogExtractor("Selected PKG: " + extractorPath.Text);
        }

        private void ChooseExtractorDestination()
        {
            if (extractorBusy || busy) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Select the parent folder for the extracted PKG" })
            {
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                string name = File.Exists(extractorPath.Text) ? Path.GetFileNameWithoutExtension(extractorPath.Text) + "_extracted" : "PKG_extracted";
                extractorDestination.Text = Path.Combine(dialog.SelectedPath, name);
            }
        }

        private void ExtractorDragEnter(object sender, DragEventArgs e)
        {
            if (!extractorBusy && !busy && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void ExtractorDragDrop(DragEventArgs e)
        {
            if (extractorBusy || busy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            SetExtractorPackage(paths[0]);
        }

        private async Task StartExtraction()
        {
            if (extractorBusy || busy) return;
            PkgExtractionRequest request = new PkgExtractionRequest();
            request.Package = extractorPath.Text;
            request.Destination = extractorDestination.Text;
            request.Temporary = Path.Combine(Path.GetTempPath(), "PS5-FPKG-Builder");
            request.Passcode = extractorPasscode.Text;
            request.Verify = extractorVerify.Checked;

            extractorCancellation = new CancellationTokenSource();
            extractorBusy = true;
            lastExtractorOutput = null;
            extractorLatest = new ConversionProgress("Preparing extraction", "Validating paths and package", 0, 0);
            extractorLog.Clear();
            LogExtractor("PKG Extractor started.");
            Stopwatch operation = Stopwatch.StartNew();

            try
            {
                string output = await Task.Run(delegate
                {
                    return PkgExtractionJob.Run(request, extractorCancellation.Token, ReportExtractor, LogExtractor);
                });
                lastExtractorOutput = output;
                extractorLatest = new ConversionProgress("Extraction completed", output, 1, 1);
                extractorStatus.Text = "Completed · " + operation.Elapsed.ToString(@"hh\:mm\:ss");
                LogExtractor("Extracted folder ready: " + output);
            }
            catch (OperationCanceledException)
            {
                extractorLatest = new ConversionProgress("Cancelled", "Operation stopped", 0, 0);
                extractorStatus.Text = "Cancelled";
                LogExtractor("Extraction cancelled.");
            }
            catch (Exception error)
            {
                extractorLatest = new ConversionProgress("Failed", error.Message, 0, 0);
                extractorStatus.Text = "Failed · " + error.Message;
                LogExtractor(error.ToString());
                MessageBox.Show(form, error.Message, "PKG extraction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                extractorBusy = false;
                if (extractorCancellation != null) { extractorCancellation.Dispose(); extractorCancellation = null; }
                operation.Stop();
            }
        }

        private void ReportExtractor(ConversionProgress update) { Interlocked.Exchange(ref extractorLatest, update); }
        private void LogExtractor(string message) { extractorMessages.Enqueue("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message); }

        private async Task ImportPkgReader()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "PS5 PKG (*.pkg)|*.pkg|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                readerPath.Text = dialog.FileName;
                readerTitle.Text = "Reading package...";
                UpdateReaderHero(null);
                FillMetadata(readerValues, null);
                DisposePicture(readerCover); DisposePicture(readerBackground);
                status.Text = "Reader · inspecting metadata and artwork...";

                try
                {
                    string passcode = readerPasscode.Text;
                    SourceInfo info = await Task.Run(delegate { return SourceInspector.InspectPkg(dialog.FileName, passcode); });
                    readerTitle.Text = string.IsNullOrEmpty(info.Title) ? Path.GetFileName(dialog.FileName) : info.Title;
                    UpdateReaderHero(info);
                    FillMetadata(readerValues, info);
                    LoadPicture(readerCover, info.CoverPath);
                    LoadPicture(readerBackground, info.BackgroundPath);
                    status.Text = "Reader · ready";
                }
                catch (Exception error)
                {
                    readerTitle.Text = "Reader error";
                    UpdateReaderHero(null);
                    FillMetadata(readerValues, null);
                    DisposePicture(readerCover); DisposePicture(readerBackground);
                    status.Text = "Reader · failed";
                    MessageBox.Show(form, error.Message, "PKG Reader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateReaderHero(SourceInfo info)
        {
            if (info == null)
            {
                readerHeroContent.Text = "—";
                readerHeroFacts.Text = "Title ID  —    •    Region  —    •    Version  —";
                readerHeroType.Text = "PKG";
                return;
            }
            readerHeroContent.Text = string.IsNullOrEmpty(info.ContentId) ? "—" : info.ContentId;
            string titleId = string.IsNullOrEmpty(info.TitleId) ? "—" : info.TitleId;
            string region = string.IsNullOrEmpty(info.Region) ? "—" : info.Region;
            string version = string.IsNullOrEmpty(info.Version) ? "—" : info.Version;
            readerHeroFacts.Text = "Title ID  " + titleId + "    •    Region  " + region + "    •    Version  " + version;
            readerHeroType.Text = string.IsNullOrEmpty(info.PackageType) ? "PKG" : info.PackageType;
        }

        private static void FillMetadata(TableLayoutPanel table, SourceInfo info)
        {
            string[] values = info==null ? new string[10] : new string[] { info.ContentId,info.TitleId,info.Region,info.Version,info.PackageType,info.Sdk,info.RequiredSystem,FileWorkspace.Size(info.Size),info.Entries.ToString(),info.SourceType };
            for(int i=0;i<10;i++) { Control c=table.Controls["value"+i]; if(c!=null) c.Text=string.IsNullOrEmpty(values[i])?"—":values[i]; }
        }

        private static void LoadPicture(PictureBox box,string path)
        {
            DisposePicture(box); if(string.IsNullOrEmpty(path)||!File.Exists(path)) return;
            using(Image image=Image.FromFile(path)) box.Image=new Bitmap(image);
        }
        private static void DisposePicture(PictureBox box) { Image image=box.Image; box.Image=null; if(image!=null) image.Dispose(); }
        private static void ChooseFolder(TextBox target) { using(FolderBrowserDialog d=new FolderBrowserDialog()) if(d.ShowDialog()==DialogResult.OK) target.Text=d.SelectedPath; }

        private void Report(ConversionProgress update) { Interlocked.Exchange(ref latest,update); }
        private void Log(string message) { messages.Enqueue("["+DateTime.Now.ToString("HH:mm:ss")+"] "+message); }
        private void BackportLog(string message) { backportMessages.Enqueue("["+DateTime.Now.ToString("HH:mm:ss")+"] "+message); }
        private void RefreshUi()
        {
            bool anyBusy=busy||extractorBusy||backportBusy||backportInspecting||(fileEditor!=null&&fileEditor.Busy);
            build.Enabled=!anyBusy && current!=null; cancel.Enabled=busy; importFolder.Enabled=importFile.Enabled=clearImport.Enabled=!anyBusy; readerImport.Enabled=!anyBusy; readerClear.Enabled=!anyBusy; openOutput.Enabled=!anyBusy&&!string.IsNullOrEmpty(lastOutput)&&File.Exists(lastOutput);
            extractorImport.Enabled=extractorBrowse.Enabled=!anyBusy; extractorStart.Enabled=!anyBusy&&File.Exists(extractorPath.Text)&&!string.IsNullOrWhiteSpace(extractorDestination.Text)&&!Directory.Exists(extractorDestination.Text)&&!File.Exists(extractorDestination.Text); extractorCancel.Enabled=extractorBusy; extractorOpen.Enabled=!anyBusy&&!string.IsNullOrEmpty(lastExtractorOutput)&&Directory.Exists(lastExtractorOutput); extractorPasscode.Enabled=extractorVerify.Enabled=!anyBusy; extractorDestination.ReadOnly=anyBusy;
            bool backportInputsReady = backportSnapshot!=null&&File.Exists(backportBasePath.Text)&&backportFiles!=null&&
                Directory.Exists(backportGamePath.Text)&&Directory.Exists(backportFolderPath.Text)&&File.Exists(backportPublisherPath.Text)&&
                Directory.Exists(backportOutputFolder.Text)&&Directory.Exists(backportWorkFolder.Text)&&
                BackportVersion.IsValid(backportVersion.Text)&&BackportVersion.IsValid(backportSnapshot.ContentVersion)&&
                BackportVersion.Compare(backportVersion.Text,backportSnapshot.ContentVersion)>0;
            backportBaseBrowse.Enabled=backportGameBrowse.Enabled=backportFolderBrowse.Enabled=backportPublisherBrowse.Enabled=backportOutputBrowse.Enabled=backportWorkBrowse.Enabled=!anyBusy;
            backportAddFiles.Enabled=backportAddFolder.Enabled=backportReplace.Enabled=backportExtract.Enabled=backportRemove.Enabled=backportSelectAll.Enabled=backportSelectNone.Enabled=backportRefresh.Enabled=backportSaveSelection.Enabled=!anyBusy&&backportFiles!=null;
            backportGrid.ReadOnly=anyBusy||backportFiles==null; backportBuild.Enabled=!anyBusy&&backportInputsReady; backportCancel.Enabled=backportBusy; backportPasscode.Enabled=!anyBusy; backportVersion.ReadOnly=anyBusy; backportCompression.Enabled=!anyBusy;
            ConversionProgress update=Interlocked.CompareExchange(ref latest,null,null); if(update!=null) { bool determinate=update.Total>0; progress.Style=busy&&!determinate?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous; progress.Value=determinate?(int)Math.Min(1000,Math.Max(0,update.Done*1000.0/update.Total)):0; status.Text=update.Stage+(busy?" · "+elapsed.Elapsed.ToString(@"hh\:mm\:ss"):"")+(string.IsNullOrEmpty(update.Detail)?"":" · "+update.Detail); }
            ConversionProgress extractUpdate=Interlocked.CompareExchange(ref extractorLatest,null,null); if(extractUpdate!=null) { bool determinate=extractUpdate.Total>0; extractorProgress.Style=extractorBusy&&!determinate?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous; extractorProgress.Value=determinate?(int)Math.Min(1000,Math.Max(0,extractUpdate.Done*1000.0/extractUpdate.Total)):0; if(extractorBusy) extractorStatus.Text=extractUpdate.Stage+(string.IsNullOrEmpty(extractUpdate.Detail)?"":" · "+extractUpdate.Detail); }
            ConversionProgress backportUpdate=Interlocked.CompareExchange(ref backportLatest,null,null); if(backportUpdate!=null) { bool determinate=backportUpdate.Total>0; backportProgress.Style=backportBusy&&!determinate?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous; backportProgress.Value=determinate?(int)Math.Min(1000,Math.Max(0,backportUpdate.Done*1000.0/backportUpdate.Total)):0; if(backportBusy) backportStatus.Text=backportUpdate.Stage+(string.IsNullOrEmpty(backportUpdate.Detail)?"":" · "+backportUpdate.Detail); }
            StringBuilder batch=new StringBuilder(); string msg; int count=0; while(count++<500&&messages.TryDequeue(out msg)) batch.AppendLine(msg); if(batch.Length>0) { log.AppendText(batch.ToString()); log.SelectionStart=log.TextLength; log.ScrollToCaret(); try { if(activeLogFile!=null){ Directory.CreateDirectory(Path.GetDirectoryName(activeLogFile)); File.AppendAllText(activeLogFile,batch.ToString(),Encoding.UTF8); } } catch{} }
            StringBuilder extractBatch=new StringBuilder(); count=0; while(count++<500&&extractorMessages.TryDequeue(out msg)) extractBatch.AppendLine(msg); if(extractBatch.Length>0) { extractorLog.AppendText(extractBatch.ToString()); extractorLog.SelectionStart=extractorLog.TextLength; extractorLog.ScrollToCaret(); }
            StringBuilder backportBatch=new StringBuilder(); count=0; while(count++<500&&backportMessages.TryDequeue(out msg)) backportBatch.AppendLine(msg); if(backportBatch.Length>0) { backportLog.AppendText(backportBatch.ToString()); backportLog.SelectionStart=backportLog.TextLength; backportLog.ScrollToCaret(); }
        }

        private void ApplyLanguage()
        {
            string l=language.SelectedItem as string ?? "English";
            string reader="Reader", backport="Backport Update", advanced="Advanced", credits="Credits", updates="Check updates";
            if (l == "Italiano") { reader="Lettore"; backport="Backport"; advanced="Avanzate"; credits="Crediti"; updates="Controlla aggiornamenti"; }
            else if (l == "Español") { reader="Lector"; backport="Backport"; advanced="Avanzado"; credits="Créditos"; updates="Buscar actualizaciones"; }
            else if (l == "Português") { reader="Leitor"; backport="Backport"; advanced="Avançado"; credits="Créditos"; updates="Verificar atualizações"; }
            else if (l == "Русский") { reader="Чтение"; backport="Бэкпорт"; advanced="Дополнительно"; credits="Авторы"; updates="Проверить обновления"; }
            navBuilder.Text="▣    PKG Builder";
            navExfat.Text="↗    exFAT → PKG";
            navPfs.Text="◆    FFPFS / FFPFSC → PKG";
            navFfpkg.Text="◈    FFPKG → PKG";
            navPkg.Text="⇄    PKG → PKG";
            navExtractor.Text="⇩    PKG Extractor to Folder";
            navFileEditor.Text="▤    Extract file from PKG";
            navReader.Text="◎    "+reader;
            navBackport.Text="↻    "+backport;
            navTrophy.Text="★    Trophy Unlocker";
            navAdvanced.Text="⚙    "+advanced;
            navCredits.Text="✦    "+credits;
            checkUpdates.Text="↻    "+updates;
            ConfigureSourceMode();
        }

        private void BeginUpdateCheck()
        {
            // Never block startup: GitHub is checked only after the UI exists, on a worker thread.
            UpdateManager.Check(form, Version, sidebarVersion);
        }

        private static bool Administrator() { using(WindowsIdentity id=WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        private void LoadSettings()
        {
            string path=Path.Combine(stateDirectory,"settings.xml"); if(!File.Exists(path)) return; try { XmlDocument d=new XmlDocument{XmlResolver=null}; using(XmlReader r=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) d.Load(r); XmlNode t=d.SelectSingleNode("/settings/theme"); theme.SelectedIndex=t!=null&&t.InnerText=="Dark"?1:0; XmlNode l=d.SelectSingleNode("/settings/language"); if(l!=null){int i=language.Items.IndexOf(l.InnerText); if(i>=0)language.SelectedIndex=i;} XmlNode s=d.SelectSingleNode("/settings/sdk"); if(s!=null)targetSdk.Text=s.InnerText; } catch{} }
        private void SaveSettings()
        {
            try { Directory.CreateDirectory(stateDirectory); using(XmlWriter w=XmlWriter.Create(Path.Combine(stateDirectory,"settings.xml"),new XmlWriterSettings{Indent=true})){w.WriteStartElement("settings");w.WriteElementString("theme",theme.SelectedIndex==1?"Dark":"Light");w.WriteElementString("language",language.SelectedItem as string??"English");w.WriteElementString("sdk",targetSdk.Text);w.WriteEndElement();} } catch{} }
        private void OnClosing(object sender,FormClosingEventArgs e)
        {
            if(busy||extractorBusy||backportBusy||backportInspecting||(fileEditor!=null&&fileEditor.Busy))
            {
                e.Cancel=true;
                MessageBox.Show(form,"An operation is running. Cancel it and wait for cleanup before closing.");
                return;
            }
            ExfatMount.CleanupStaleOwnedMounts(Log);
            SaveSettings();
        }
    }
}
