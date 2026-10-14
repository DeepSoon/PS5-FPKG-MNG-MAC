using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal static class SdkVersion
    {
        // Package declarations use packed BCD major/minor, as in the supplied 0.5.
        // Dotted input additionally supports the minor byte. Raw input retains all bits.
        internal static ulong Parse(string text)
        {
            text = (text ?? "").Trim();
            ulong value;
            if (Regex.IsMatch(text, "^0[xX][0-9a-fA-F]{16}$") &&
                ulong.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) return value;
            // A few param.json variants serialize the packed 64-bit SDK as a JSON
            // decimal number instead of the usual 0x................ string. Keep
            // short values (1..99) using the normal human-readable major-version path.
            if (text.Length > 2 && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return value;
            Match match = Regex.Match(text, @"^(\d{1,2})(?:\.(\d{2}))?$");
            if (!match.Success) throw new ArgumentException("Choose an SDK or enter a version such as 9.00, 10.01, or 0x1001000000000000.");
            int major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return ((ulong)((major / 10 << 4) | major % 10) << 56) | ((ulong)((minor / 10 << 4) | minor % 10) << 48);
        }

        internal static string Hex(ulong value) { return "0x" + value.ToString("X16", CultureInfo.InvariantCulture); }

        internal static ulong PackageVersion(ulong executable)
        {
            return (ulong)PackageSdk.Call("LibProsperoPkg.Content.ProsperoSdkVersions", "ToPackageVersion", new Type[] { typeof(ulong) }, executable);
        }

        // Builds need the complete .sceversion tuple, not a guessed SDK build number.
        internal static ulong ForBuild(string text)
        {
            text = (text ?? "").Trim();
            if (Regex.IsMatch(text, "^0[xX][0-9a-fA-F]{16}$")) return Parse(text);
            ulong declared = Parse(text);
            int packed = (int)(declared >> 56), major = (packed >> 4) * 10 + (packed & 15);
            if ((declared & 0x00FFFFFFFFFFFFFFUL) != 0 || major < 1 || major > 11)
                throw new ArgumentException("For building, choose a supplied SDK preset (1.00–11.00), or enter the exact full 16-digit hexadecimal executable SDK. A firmware minor version does not identify an SDK build.");
            object release = PackageSdk.Call("LibProsperoPkg.Content.ProsperoSdkVersions", "GetByMajor", new Type[] { typeof(int) }, major);
            return (ulong)BuilderBridge.Get(release, "ExecutableVersion");
        }

        internal static string Describe(string text)
        {
            ulong value;
            try { value = Parse(text); } catch { return string.IsNullOrEmpty(text) ? "Not declared" : text; }
            int major = (int)(value >> 56), minor = (int)((value >> 48) & 255);
            if ((major & 15) > 9 || (major >> 4) > 9 || (minor & 15) > 9 || (minor >> 4) > 9) return Hex(value);
            return ((major >> 4) * 10 + (major & 15)).ToString(CultureInfo.InvariantCulture) + "." +
                ((minor >> 4) * 10 + (minor & 15)).ToString("D2", CultureInfo.InvariantCulture) + " (" + Hex(value) + ")";
        }

        internal static void Populate(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDown;
            for (int major = 1; major <= 11; major++) box.Items.Add(major.ToString(CultureInfo.InvariantCulture) + ".00");
            box.SelectedIndex = -1;
        }
    }

    // Access the JSON implementation already shipped with the .NET 9 runtime.
    // This keeps the launcher compilable with the Windows C# 5 compiler.
    internal sealed class PackageParam
    {
        private static readonly Type NodeType = Type.GetType("System.Text.Json.Nodes.JsonNode, System.Text.Json", true);
        private static readonly PropertyInfo Item = NodeType.GetProperty("Item", new Type[] { typeof(string) });
        private readonly object root;

        internal PackageParam(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("param.json exceeds the supported size.");
            string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            root = ParseNode(text);
            if (root == null || root.GetType().FullName != "System.Text.Json.Nodes.JsonObject")
                throw new InvalidDataException("param.json must contain a JSON object.");
        }

        private static object ParseNode(string text)
        {
            foreach (MethodInfo method in NodeType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] args = method.GetParameters();
                if (method.Name == "Parse" && args.Length == 3 && args[0].ParameterType == typeof(string))
                    return BuilderBridge.Invoke(method, null, text, null, Activator.CreateInstance(args[2].ParameterType));
            }
            throw new MissingMethodException("JsonNode.Parse(string)");
        }

        internal string Text(string name)
        {
            object value = Item.GetValue(root, new object[] { name });
            if (value == null) return "";

            // PS5 param.json files are not fully uniform across tools/dumps.
            // Some fields that are normally strings can legally arrive as JSON numbers
            // (for example version/category/SDK values). JsonNode.GetValue<string>()
            // throws for those nodes, which used to abort FFPFS/FFPFSC preview import.
            string typeName = value.GetType().FullName ?? "";
            if (typeName == "System.Text.Json.Nodes.JsonObject" ||
                typeName == "System.Text.Json.Nodes.JsonArray") return "";

            try
            {
                MethodInfo getter = NodeType.GetMethod("GetValue", Type.EmptyTypes).MakeGenericMethod(typeof(string));
                return (string)BuilderBridge.Invoke(getter, value);
            }
            catch
            {
                // JsonValue.ToString() returns the scalar JSON representation for
                // Number/True/False without converting through the current culture.
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        internal void SetText(string name, string text)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("JSON property name is required.");
            StringBuilder encoded = new StringBuilder("\"");
            string value = text ?? string.Empty;
            foreach (char c in value)
            {
                if (c == '"') encoded.Append("\\\"");
                else if (c == '\\') encoded.Append("\\\\");
                else if (c == '\b') encoded.Append("\\b");
                else if (c == '\f') encoded.Append("\\f");
                else if (c == '\n') encoded.Append("\\n");
                else if (c == '\r') encoded.Append("\\r");
                else if (c == '\t') encoded.Append("\\t");
                else if (c < 0x20) encoded.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else encoded.Append(c);
            }
            encoded.Append('"');
            Item.SetValue(root, ParseNode(encoded.ToString()), new object[] { name });
        }

        internal void Retarget(ulong version)
        {
            string json = "\"" + SdkVersion.Hex(version) + "\"";
            Item.SetValue(root, ParseNode(json), new object[] { "sdkVersion" });
            Item.SetValue(root, ParseNode(json), new object[] { "requiredSystemSoftwareVersion" });
        }

        internal byte[] Bytes()
        {
            Type optionsType = Type.GetType("System.Text.Json.JsonSerializerOptions, System.Text.Json", true);
            object options = Activator.CreateInstance(optionsType);
            optionsType.GetProperty("WriteIndented").SetValue(options, true, null);
            string json = (string)BuilderBridge.Invoke(NodeType.GetMethod("ToJsonString", new Type[] { optionsType }), root, options);
            return new UTF8Encoding(false).GetBytes(json + "\r\n");
        }
    }
}
