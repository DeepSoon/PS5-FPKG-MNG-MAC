using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
                if (args.Length > 0 && args[0] == "--tool-probe")
                {
                    if (args.Length > 1 && args[1] == "wait") Thread.Sleep(10000);
                    for (int i = 1; i < args.Length; i++) Console.WriteLine(args[i]);
                    return args.Length > 1 && args[1] == "fail" ? 9 : 0;
                }
                BuilderBridge.Initialize();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run();
                if (args.Length > 0 && args[0] == "--pkg-self-test") return PackageSdkTests.RunIntegration();
                Form form = (Form)Activator.CreateInstance(BuilderBridge.FormType, true);
                ComboBox upstreamLanguage = (ComboBox)BuilderBridge.Get(form, "languageCombo");
                upstreamLanguage.Enabled = true;
                ExfatIntegration integration = new ExfatIntegration(form);
                Application.Run(form);
                GC.KeepAlive(integration);
                return 0;
            }
            catch (Exception error)
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpkgGuiExfat");
                string report = Path.Combine(directory, "startup-error.txt");
                try { Directory.CreateDirectory(directory); File.WriteAllText(report, error.ToString()); } catch { }
                if (args.Length > 0 && args[0] == "--self-test") return 1;
                MessageBox.Show(error.Message + "\r\n\r\nDetails: " + report, "FPKG GUI - startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
