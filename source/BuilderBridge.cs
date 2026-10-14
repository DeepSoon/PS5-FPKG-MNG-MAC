using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace FpkgExfat
{
    // The GUI in the supplied archive has no sources. Keep its assemblies intact
    // and bind the extension to the actual 0.5 signatures, checked in the PDB/DLL.
    internal static class BuilderBridge
    {
        internal const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        internal static Assembly Library;
        internal static Type FormType;

        internal static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                string name = new AssemblyName(args.Name).Name;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Library = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibProsperoPkg.dll"));
            Assembly gui = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibProsperoPkg.Gui.dll"));
            FormType = gui.GetType("LibProsperoPkg.Gui.MainForm", true);
            Type options = Library.GetType("LibProsperoPkg.ProsperoBuildOptions", true);
            RequireMethod(FormType, "LoadSourceMetadata", new Type[] { typeof(bool), typeof(bool) });
            RequireMethod(FormType, "TryCreateBuildOptions", new Type[] { options.MakeByRefType(), typeof(string).MakeByRefType() });
            RequireMethod(FormType, "VerifyOutput", new Type[] { typeof(string),
                Library.GetType("LibProsperoPkg.ProsperoPublisherImageMode", true), typeof(bool), typeof(bool), typeof(CancellationToken) });
            RequireMethod(Library.GetType("LibProsperoPkg.ProsperoPackageBuilder", true), "Build",
                new Type[] { options, typeof(Action<string>) });
            foreach (string field in new string[] { "sourceText", "outputText", "temporaryText", "contentIdText",
                "titleText", "versionText", "buildInProgress", "playGoChunks", "metadataLabel", "playGoLabel", "sdkVersionCombo", "overrideSdkVersionCheck" })
                if (FormType.GetField(field, Members) == null) throw new MissingFieldException(FormType.FullName, field);
            foreach (string property in new string[] { "CancellationToken", "SourceFolder", "OutputFolder", "TemporaryDirectory", "PublisherImageMode", "SdkVersionOverride" })
                if (options.GetProperty(property) == null) throw new MissingMemberException(options.FullName, property);
        }

        private static MethodInfo RequireMethod(Type type, string name, Type[] arguments)
        {
            MethodInfo method = type.GetMethod(name, Members, null, arguments, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        internal static object Get(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field == null) throw new MissingMemberException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        internal static void Set(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
            property.SetValue(target, value, null);
        }

        internal static object Invoke(MethodInfo method, object target, params object[] arguments)
        {
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                throw;
            }
        }

        internal static bool IsBusy(Form form) { return (bool)Get(form, "buildInProgress"); }

        // Called only on the UI thread. Metadata belongs to this image; compression,
        // package mode, image mode, passcode and PlayGo preferences come from the GUI.
        internal static object PrepareOptions(Form form, string source, string output, string temporary, CancellationToken token)
        {
            List<Action> restore = new List<Action>();
            foreach (FieldInfo field in FormType.GetFields(Members))
            {
                if (field.IsStatic) continue;
                object value = field.GetValue(form);
                TextBox text = value as TextBox;
                Label label = value as Label;
                NumericUpDown number = value as NumericUpDown;
                ComboBox combo = value as ComboBox;
                if (text != null) { string previous = text.Text; TextBox captured = text; restore.Add(delegate { captured.Text = previous; }); }
                else if (label != null) { string previous = label.Text; Label captured = label; restore.Add(delegate { captured.Text = previous; }); }
                else if (number != null) { decimal previous = number.Value; NumericUpDown captured = number; restore.Add(delegate { captured.Value = previous; }); }
                else if (combo != null) { int previous = combo.SelectedIndex; ComboBox captured = combo; restore.Add(delegate { captured.SelectedIndex = previous; }); }
            }
            try
            {
                ((TextBox)Get(form, "sourceText")).Text = source;
                // LoadSourceMetadata deliberately swallows JSON errors. Clear old
                // metadata so a damaged image cannot inherit the previous game's ID.
                ((TextBox)Get(form, "contentIdText")).Clear();
                ((TextBox)Get(form, "titleText")).Clear();
                ((TextBox)Get(form, "versionText")).Clear();
                Invoke(FormType.GetMethod("LoadSourceMetadata", Members), form, false, false);
                ((TextBox)Get(form, "outputText")).Text = output;
                ((TextBox)Get(form, "temporaryText")).Text = temporary;
                object[] args = new object[] { null, null };
                bool valid = (bool)Invoke(FormType.GetMethod("TryCreateBuildOptions", Members), form, args);
                if (!valid) throw new InvalidDataException("Invalid metadata or builder options: " + args[1]);
                Set(args[0], "CancellationToken", token);
                return args[0];
            }
            finally { foreach (Action action in restore) action(); }
        }

        internal static string Build(object options, Action<string> log)
        {
            Type type = Library.GetType("LibProsperoPkg.ProsperoPackageBuilder", true);
            MethodInfo build = RequireMethod(type, "Build", new Type[] { options.GetType(), typeof(Action<string>) });
            object result = Invoke(build, null, options, log);
            IEnumerable warnings = Get(result, "Warnings") as IEnumerable;
            if (warnings != null) foreach (object warning in warnings) log("Builder: " + warning);
            return (string)Get(result, "OutputPath");
        }

        internal static void Verify(string package, object options, CancellationToken token, Action<string> log)
        {
            object result = Invoke(FormType.GetMethod("VerifyOutput", Members), null,
                package, Get(options, "PublisherImageMode"), true, true, token);
            log("Container: " + Get(result, "ContainerType") + "; bytes: " + Get(result, "Length"));
            log("SHA-256 PKG: " + Get(result, "Sha256"));
        }
    }
}
