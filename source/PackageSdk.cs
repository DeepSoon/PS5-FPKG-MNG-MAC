using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class PackageSnapshot
    {
        internal string Path, ContentId, ContentVersion, Sdk, Required, ParamEntryName;
        internal int EntryCount;
        internal long Length, CntOffset;
        internal uint ParamEntryId;
        internal DateTime Modified;
        internal byte[] ParamBytes, MetadataHash, IdentityHash;
        internal bool Plaintext;
    }

    internal static class PackageSdk
    {
        internal static object Call(string typeName, string methodName, Type[] types, params object[] args)
        {
            Type type = BuilderBridge.Library.GetType(typeName, true);
            MethodInfo method = type.GetMethod(methodName, BuilderBridge.Members, null, types, null);
            if (method == null) throw new MissingMethodException(typeName, methodName);
            return BuilderBridge.Invoke(method, null, args);
        }
        internal static PackageSnapshot Inspect(string path, string passcode)
        {
            path = System.IO.Path.GetFullPath(path); FileWorkspace.NoReparse(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return CntMetadata.Read(stream, path, passcode).Snapshot;
        }
        internal static PackageSnapshot InspectForBackport(string path, string passcode)
        {
            path = System.IO.Path.GetFullPath(path); FileWorkspace.NoReparse(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return CntMetadata.ReadSmall(stream, path, passcode).Snapshot;
        }
        internal static void EnsureUnchanged(PackageSnapshot expected, PackageSnapshot actual)
        {
            if (!expected.Path.Equals(actual.Path, StringComparison.OrdinalIgnoreCase) || expected.Length != actual.Length ||
                expected.Modified != actual.Modified || expected.CntOffset != actual.CntOffset ||
                !FileWorkspace.Equal(expected.MetadataHash, actual.MetadataHash) || !FileWorkspace.Equal(expected.IdentityHash, actual.IdentityHash))
                throw new IOException("The PKG changed after it was opened. Open it again before saving.");
        }
        internal static bool NeedsMetadataRepair(PackageSnapshot expected, string passcode, CancellationToken token, Action<string> log)
        {
            FileWorkspace.NoReparse(expected.Path);
            using (FileStream stream = new FileStream(expected.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                CntMetadata doc = CntMetadata.Read(stream, expected.Path, passcode); EnsureUnchanged(expected, doc.Snapshot);
                return !FileWorkspace.Equal(doc.Tail, doc.RepairMetadata(token, log));
            }
        }
        internal static byte[] ReadRange(Stream input, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > input.Length || count > input.Length - offset)
                throw new InvalidDataException("PKG range is outside the file.");
            byte[] data = new byte[count]; input.Position = offset; int done = 0;
            while (done < count) { int n = input.Read(data, done, count - done); if (n == 0) throw new EndOfStreamException(); done += n; }
            return data;
        }
        internal static byte[] Sha256(byte[] data) { using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(data); }
        internal static byte[] Sha3(byte[] data, int start, int size)
        {
            BinaryData.Range(data, start, size);
            using (MemoryStream stream = new MemoryStream(data, false))
                return (byte[])Call("LibProsperoPkg.Util.Crypto", "Sha3_256", new Type[] { typeof(Stream), typeof(long), typeof(long) }, stream, (long)start, (long)size);
        }
        // Adapt the library's public byte-span API to a C# 5 byte[] delegate.
        private static readonly Lazy<Func<byte[], byte[]>> Wrap = new Lazy<Func<byte[], byte[]>>(delegate
        {
            Type type = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoPublisherRsa", true);
            MethodInfo method = type.GetMethod("BuildCntHeaderWrap", BindingFlags.Public | BindingFlags.Static);
            Type span = method.GetParameters()[0].ParameterType;
            ConstructorInfo ctor = span.GetConstructor(new Type[] { typeof(byte[]) });
            if (ctor == null) throw new MissingMethodException("ReadOnlySpan<byte>(byte[])");
            DynamicMethod adapter = new DynamicMethod("WrapDebugCnt", typeof(byte[]), new Type[] { typeof(byte[]) }, typeof(PackageSdk).Module);
            ILGenerator il = adapter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
            return (Func<byte[], byte[]>)adapter.CreateDelegate(typeof(Func<byte[], byte[]>));
        });
        internal static byte[] HeaderWrap(byte[] header) { return Wrap.Value(header); }

        // Verify the source wrap instead of requiring a freshly-generated PKCS#1 block
        // to be byte-identical. The supplied library exposes the publisher verifier as
        // VerifyCntHeaderWrap(ReadOnlySpan<byte>, ReadOnlySpan<byte>); DynamicMethod lets
        // this C# 5 extension call the span API without boxing ref structs.
        private static readonly Lazy<Func<byte[], byte[], bool>> VerifyWrap = new Lazy<Func<byte[], byte[], bool>>(delegate
        {
            Type type = BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoPublisherRsa", true);
            MethodInfo method = type.GetMethod("VerifyCntHeaderWrap", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(type.FullName, "VerifyCntHeaderWrap");
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2) throw new MissingMethodException(type.FullName, "VerifyCntHeaderWrap");
            Type span0 = parameters[0].ParameterType, span1 = parameters[1].ParameterType;
            ConstructorInfo ctor0 = span0.GetConstructor(new Type[] { typeof(byte[]) });
            ConstructorInfo ctor1 = span1.GetConstructor(new Type[] { typeof(byte[]) });
            if (ctor0 == null || ctor1 == null) throw new MissingMethodException("ReadOnlySpan<byte>(byte[])");
            DynamicMethod adapter = new DynamicMethod("VerifyDebugCntWrap", typeof(bool), new Type[] { typeof(byte[]), typeof(byte[]) }, typeof(PackageSdk).Module);
            ILGenerator il = adapter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, ctor0);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Newobj, ctor1);
            il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
            return (Func<byte[], byte[], bool>)adapter.CreateDelegate(typeof(Func<byte[], byte[], bool>));
        });
        internal static bool VerifyHeaderWrap(byte[] header, byte[] wrap)
        {
            if (header == null || header.Length != 0x1000 || wrap == null || wrap.Length != 0x180) return false;
            return VerifyWrap.Value(header, wrap);
        }
    }

    internal static class BinaryData
    {
        internal static void Range(byte[] data, int offset, int count)
        { if (offset < 0 || count < 0 || offset > data.Length || count > data.Length - offset) throw new InvalidDataException("Invalid metadata range."); }
        internal static byte[] Slice(byte[] data, int offset, int count)
        { Range(data, offset, count); byte[] result = new byte[count]; Buffer.BlockCopy(data, offset, result, 0, count); return result; }
        internal static void Put(byte[] data, int offset, byte[] value)
        { Range(data, offset, value.Length); Buffer.BlockCopy(value, 0, data, offset, value.Length); }
        internal static uint U32(byte[] data, int p, bool little)
        {
            Range(data, p, 4);
            return little ? (uint)(data[p] | data[p + 1] << 8 | data[p + 2] << 16 | data[p + 3] << 24)
                : (uint)(data[p] << 24 | data[p + 1] << 16 | data[p + 2] << 8 | data[p + 3]);
        }
        internal static int U16(byte[] data, int p, bool little)
        { Range(data, p, 2); return little ? data[p] | data[p + 1] << 8 : data[p] << 8 | data[p + 1]; }
        internal static long I64(byte[] data, int p, bool little)
        {
            ulong value = little ? (ulong)U32(data, p, true) | (ulong)U32(data, p + 4, true) << 32
                : (ulong)U32(data, p, false) << 32 | U32(data, p + 4, false);
            if (value > long.MaxValue) throw new InvalidDataException("Metadata offset exceeds Int64."); return (long)value;
        }
        internal static void Put32(byte[] data, int p, uint value, bool little)
        { Range(data, p, 4); for (int i = 0; i < 4; i++) data[p + i] = (byte)(value >> ((little ? i : 3 - i) * 8)); }
        internal static uint Crc(byte[] data, int start, int count, bool castagnoli)
        {
            Range(data, start, count); uint crc = uint.MaxValue; uint[] table = castagnoli ? CrcC : CrcZip;
            for (int i = start; i < start + count; i++) crc = table[(crc ^ data[i]) & 255] ^ (crc >> 8); return ~crc;
        }
        private static readonly uint[] CrcZip = Table(0xEDB88320U), CrcC = Table(0x82F63B78U);
        private static uint[] Table(uint polynomial)
        { uint[] result = new uint[256]; for (int i = 0; i < 256; i++) { uint c = (uint)i; for (int n = 0; n < 8; n++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : polynomial); result[i] = c; } return result; }
    }
}
