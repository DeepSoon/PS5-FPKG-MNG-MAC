using System;
using System.Collections.Generic;

namespace System.Windows.Forms
{
    // Minimal stub so SdkVersion.cs (which only uses ComboBox in Populate())
    // compiles on macOS where WinForms is unavailable. Not used by Mac CLI.
    public enum ComboBoxStyle { Simple, DropDown, DropDownList }
    public class ComboBox
    {
        public ComboBoxStyle DropDownStyle { get; set; }
        public List<object> Items { get; } = new List<object>();
        public int SelectedIndex { get; set; }
    }
}
