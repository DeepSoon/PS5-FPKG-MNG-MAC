using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FpkgExfat
{
    // Mac-compatible replacement for the Windows-only BuilderBridge.
    // Same public surface used by the portable sources (Library, Members,
    // Get/Set/Invoke) but without any WinForms dependency.
    // Build/Verify require Windows + Sony SDK and throw a clear error on macOS.
    internal static class BuilderBridge
    {
        internal const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        internal static System.Reflection.Assembly Library;

        internal static void Initialize()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                try
                {
                    string name = new AssemblyName(args.Name).Name;
                    string path = System.IO.Path.Combine(baseDir, name + ".dll");
                    if (System.IO.File.Exists(path))
                    {
                        try { return Assembly.LoadFrom(path); } catch { }
                    }
                }
                catch { }
                return null;
            };
            string[] candidates = new string[]
            {
                System.IO.Path.Combine(baseDir, "LibProsperoPkg.dll"),
                System.IO.Path.Combine(baseDir, "..", "Resources", "LibProsperoPkg.dll"),
                System.IO.Path.Combine(baseDir, "..", "..", "Resources", "LibProsperoPkg.dll"),
            };
            Exception last = null;
            foreach (string c in candidates)
            {
                try
                {
                    string full = System.IO.Path.GetFullPath(c);
                    if (System.IO.File.Exists(full))
                    {
                        Library = Assembly.LoadFrom(full);
                        return;
                    }
                }
                catch (Exception e) { last = e; }
            }
            throw new System.IO.FileNotFoundException(
                "LibProsperoPkg.dll not found next to FpkgMac. Copy it from the Windows release folder.", last);
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

        internal static string Build(object options, Action<string> log)
        {
            throw new PlatformNotSupportedException(
                "PKG building (LibProsperoPkg.ProsperoPackageBuilder + prospero-pub-cmd.exe do Sony SDK + libScePubTools) exige Windows. " +
                "No Mac, use Leitor / Extrator / Editor de arquivos (listar e extrair).");
        }

        internal static void Verify(string package, object options, System.Threading.CancellationToken token, Action<string> log)
        {
            throw new PlatformNotSupportedException(
                "Verificacao de build exige Windows + Sony SDK. No Mac, a extracao ja valida leitura do PKG.");
        }
    }
}
