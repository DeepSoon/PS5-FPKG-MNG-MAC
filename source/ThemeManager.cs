using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal sealed class ThemeManager : IDisposable
    {
        private sealed class Original
        {
            internal Color Back, Fore;
            internal FlatStyle Flat;
            internal bool Visual;
        }

        private readonly Dictionary<Control, Original> originals = new Dictionary<Control, Original>();
        private readonly HashSet<Form> windows = new HashSet<Form>();
        private bool dark;

        private readonly Color darkBackground = Color.FromArgb(10, 13, 19);
        private readonly Color darkSurface = Color.FromArgb(18, 23, 32);
        private readonly Color darkInput = Color.FromArgb(25, 31, 43);
        private readonly Color darkBorder = Color.FromArgb(42, 51, 68);
        private readonly Color darkForeground = Color.FromArgb(235, 240, 248);
        private readonly Color lightBackground = Color.FromArgb(244, 247, 251);
        private readonly Color lightSurface = Color.White;
        private readonly Color lightInput = Color.White;
        private readonly Color lightBorder = Color.FromArgb(209, 216, 226);
        private readonly Color lightForeground = Color.FromArgb(30, 36, 46);
        private readonly Color accent = Color.FromArgb(88, 116, 255);
        private readonly Color accentHover = Color.FromArgb(107, 134, 255);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal ThemeManager(Form main)
        {
            RegisterWindow(main);
            Application.Idle += OnIdle;
        }

        internal void Apply(bool useDark)
        {
            dark = useDark;
            foreach (Control control in new List<Control>(originals.Keys)) if (!control.IsDisposed) PaintControl(control);
            foreach (Form window in new List<Form>(windows)) if (!window.IsDisposed) PaintTitle(window);
        }

        private void RegisterWindow(Form window)
        {
            if (!windows.Add(window)) return;
            Register(window);
            window.HandleCreated += WindowHandleCreated;
            window.FormClosed += WindowClosed;
            PaintTitle(window);
        }

        private void WindowHandleCreated(object sender, EventArgs args) { PaintTitle((Form)sender); }
        private void WindowClosed(object sender, FormClosedEventArgs args)
        {
            Form window = (Form)sender;
            windows.Remove(window);
            window.HandleCreated -= WindowHandleCreated;
            window.FormClosed -= WindowClosed;
            foreach (Control control in new List<Control>(originals.Keys)) if (control.IsDisposed) originals.Remove(control);
        }

        private void OnIdle(object sender, EventArgs args)
        {
            foreach (Form window in Application.OpenForms) RegisterWindow(window);
        }

        private void Register(Control control)
        {
            if (originals.ContainsKey(control)) return;
            Original original = new Original { Back = control.BackColor, Fore = control.ForeColor };
            Original parent;
            if (control.Parent != null && originals.TryGetValue(control.Parent, out parent))
            {
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(control);
                if (!properties["BackColor"].ShouldSerializeValue(control)) original.Back = parent.Back;
                if (!properties["ForeColor"].ShouldSerializeValue(control)) original.Fore = parent.Fore;
            }
            ButtonBase button = control as ButtonBase;
            if (button != null) { original.Flat = button.FlatStyle; original.Visual = button.UseVisualStyleBackColor; }
            originals.Add(control, original);
            control.ControlAdded += ControlAdded;
            TabControl tabs = control as TabControl;
            if (tabs != null)
            {
                tabs.DrawItem += DrawTab;
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.Appearance = TabAppearance.FlatButtons;
                tabs.SizeMode = TabSizeMode.Fixed;
                tabs.ItemSize = new Size(145, 34);
                tabs.Padding = new Point(10, 4);
            }
            foreach (Control child in control.Controls) Register(child);
            PaintControl(control);
        }

        private void ControlAdded(object sender, ControlEventArgs args) { Register(args.Control); }

        private void PaintTitle(Form window)
        {
            if (!window.IsHandleCreated) return;
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(window.Handle, 20, ref value, sizeof(int)) != 0) DwmSetWindowAttribute(window.Handle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private void PaintControl(Control control)
        {
            Color background = dark ? darkBackground : lightBackground;
            Color surface = dark ? darkSurface : lightSurface;
            Color input = dark ? darkInput : lightInput;
            Color border = dark ? darkBorder : lightBorder;
            Color foreground = dark ? darkForeground : lightForeground;
            Color muted = dark ? Color.FromArgb(151, 163, 184) : Color.FromArgb(100, 111, 129);
            Color sidebar = dark ? Color.FromArgb(13, 17, 24) : Color.FromArgb(234, 239, 247);
            Color header = dark ? Color.FromArgb(10, 13, 19) : Color.FromArgb(249, 251, 254);
            string tag = Convert.ToString(control.Tag) ?? "";

            bool field = control is TextBoxBase || control is ComboBox || control is ListBox || control is ListView || control is TreeView || control is NumericUpDown;
            bool card = control is ModernCard || string.Equals(tag, "card", StringComparison.OrdinalIgnoreCase);
            bool media = string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(tag, "sidebar", StringComparison.OrdinalIgnoreCase)) control.BackColor = sidebar;
            else if (string.Equals(tag, "header", StringComparison.OrdinalIgnoreCase)) control.BackColor = header;
            else if (field) control.BackColor = input;
            else if (media) control.BackColor = dark ? Color.FromArgb(8, 10, 14) : Color.FromArgb(241, 244, 249);
            else control.BackColor = card ? surface : background;
            control.ForeColor = string.Equals(tag, "muted", StringComparison.OrdinalIgnoreCase) ? muted : (string.Equals(tag, "accent-text", StringComparison.OrdinalIgnoreCase) ? accent : foreground);

            ModernCard modernCard = control as ModernCard;
            if (modernCard != null)
            {
                modernCard.BorderColor = media ? (dark ? Color.FromArgb(30, 36, 48) : lightBorder) : border;
                modernCard.BorderWidth = 1;
                modernCard.Radius = media ? 12 : 14;
                modernCard.Invalidate();
            }

            Button button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.Cursor = Cursors.Hand;
                button.FlatAppearance.BorderSize = 0;
                bool primary = string.Equals(tag, "primary", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "primary-large", StringComparison.OrdinalIgnoreCase);
                bool activeNav = string.Equals(tag, "nav-active", StringComparison.OrdinalIgnoreCase);
                bool nav = string.Equals(tag, "nav", StringComparison.OrdinalIgnoreCase) || activeNav;
                bool ghost = string.Equals(tag, "ghost", StringComparison.OrdinalIgnoreCase);
                if (primary)
                {
                    button.BackColor = accent;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = accentHover;
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(68, 91, 214);
                    button.Padding = new Padding(12, 4, 12, 4);
                }
                else if (nav)
                {
                    button.BackColor = activeNav ? (dark ? Color.FromArgb(35, 43, 71) : Color.FromArgb(222, 229, 255)) : sidebar;
                    button.ForeColor = activeNav ? (dark ? Color.White : Color.FromArgb(42, 62, 150)) : (dark ? Color.FromArgb(194, 203, 219) : Color.FromArgb(54, 65, 83));
                    button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(29, 35, 48) : Color.FromArgb(222, 229, 239);
                    button.FlatAppearance.MouseDownBackColor = activeNav ? button.BackColor : (dark ? Color.FromArgb(36, 43, 58) : Color.FromArgb(214, 222, 234));
                }
                else
                {
                    button.BackColor = ghost ? (dark ? Color.FromArgb(24, 29, 39) : Color.FromArgb(242, 245, 250)) : surface;
                    button.ForeColor = foreground;
                    button.FlatAppearance.BorderSize = ghost ? 0 : 1;
                    button.FlatAppearance.BorderColor = border;
                    button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(34, 42, 57) : Color.FromArgb(232, 237, 246);
                    button.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(43, 52, 69) : Color.FromArgb(219, 227, 240);
                    button.Padding = new Padding(8, 2, 8, 2);
                }
            }

            TextBoxBase textBox = control as TextBoxBase;
            if (textBox != null) textBox.BorderStyle = BorderStyle.FixedSingle;

            ComboBox combo = control as ComboBox;
            if (combo != null) combo.FlatStyle = FlatStyle.Flat;

            CheckBox check = control as CheckBox;
            if (check != null) check.UseVisualStyleBackColor = false;

            TabPage page = control as TabPage;
            if (page != null) { page.UseVisualStyleBackColor = false; page.BackColor = background; }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = accent;
                link.ActiveLinkColor = dark ? Color.White : Color.FromArgb(22, 94, 202);
                link.VisitedLinkColor = dark ? Color.FromArgb(183, 150, 255) : Color.FromArgb(103, 74, 170);
            }

            RichTextBox rich = control as RichTextBox;
            if (rich != null) { rich.BorderStyle = BorderStyle.None; rich.BackColor = dark ? Color.FromArgb(13, 17, 24) : Color.FromArgb(248, 250, 253); }

            ProgressBar bar = control as ProgressBar;
            if (bar != null) { bar.BackColor = input; }

            DataGridView grid = control as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = surface;
                grid.EnableHeadersVisualStyles = false;
                grid.GridColor = border;
                grid.DefaultCellStyle.BackColor = surface;
                grid.DefaultCellStyle.ForeColor = foreground;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 91, 185);
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.ColumnHeadersDefaultCellStyle.BackColor = background;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
                grid.RowHeadersDefaultCellStyle.BackColor = background;
                grid.RowHeadersDefaultCellStyle.ForeColor = foreground;
            }
            control.Invalidate();
        }

        private void DrawTab(object sender, DrawItemEventArgs args)
        {
            TabControl tabs = (TabControl)sender;
            if (args.Index < 0 || args.Index >= tabs.TabPages.Count) return;
            bool selected = args.Index == tabs.SelectedIndex;
            Color background = dark ? darkBackground : lightBackground;
            Color surface = dark ? darkSurface : lightSurface;
            Color foreground = dark ? darkForeground : lightForeground;
            Rectangle bounds = args.Bounds;
            using (SolidBrush brush = new SolidBrush(selected ? surface : background)) args.Graphics.FillRectangle(brush, bounds);
            if (selected)
            {
                using (SolidBrush line = new SolidBrush(accent)) args.Graphics.FillRectangle(line, new Rectangle(bounds.Left + 8, bounds.Bottom - 3, Math.Max(0, bounds.Width - 16), 3));
            }
            using (Font font = new Font(tabs.Font.FontFamily, tabs.Font.Size, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, font, bounds,
                    selected ? accent : foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public void Dispose()
        {
            Application.Idle -= OnIdle;
            foreach (Form window in windows) { window.HandleCreated -= WindowHandleCreated; window.FormClosed -= WindowClosed; }
            foreach (Control control in originals.Keys)
            {
                control.ControlAdded -= ControlAdded;
                TabControl tabs = control as TabControl;
                if (tabs != null) tabs.DrawItem -= DrawTab;
            }
            windows.Clear(); originals.Clear();
        }
    }
}
