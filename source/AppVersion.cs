using System;
using System.IO;

namespace FpkgExfat
{
    internal static class AppVersion
    {
        internal static string Read()
        {
            try
            {
                string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION.txt");
                if (File.Exists(file))
                {
                    string value = File.ReadAllText(file).Trim();
                    if (!string.IsNullOrEmpty(value)) return value.TrimStart('v', 'V');
                }
            }
            catch { }
            return "1.4.4";
        }
    }
}
