using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class PkgFileItem
    {
        internal string Path, Replacement;
        internal long Size;
        internal bool Removed;
        internal bool SyntheticParam;
        internal object Node;
        internal CntEntry Metadata;
        internal bool Added { get { return Node==null&&Metadata==null&&!SyntheticParam; } }
        internal string State { get { return Replacement!=null&&!System.IO.File.Exists(Replacement)?"Sorgente mancante":Removed?"Rimosso dal nuovo PKG":Added?"Aggiunto":Replacement!=null?"Sostituito":"Nel PKG"; } }
        internal long CurrentSize { get { return Replacement==null?Size:System.IO.File.Exists(Replacement)?new FileInfo(Replacement).Length:0; } }
    }

    // A read-only handle stays open while browsing, preventing the base from changing.
    // Nested NAPS blocks are decoded on demand; no full-image temporary file is created.
    internal sealed class PkgFileSession : IDisposable
    {
        internal readonly List<PkgFileItem> Files=new List<PkgFileItem>();
        internal PackageSnapshot Snapshot;
        internal CancellationToken Token;
        private readonly List<IDisposable> resources=new List<IDisposable>();
        private FileStream input;
        private CntMetadata metadata;
        private string passcode;
        internal string Passcode { get { return passcode; } }
        private object layout;
        private Stream packed;
        private MethodInfo decodeSpan;
        private readonly List<SpanRange> spans=new List<SpanRange>();
        private long decodedIndexBytes;
        private bool indexing;
        private sealed class SpanRange { internal long Start;internal int Length;internal object Value; }

        internal static PkgFileSession Open(string path,string code,CancellationToken token,Action<string> log)
        {
            PkgFileSession session=new PkgFileSession();
            try
            {
                FileWorkspace.NoReparse(path);
                session.Token=token;session.passcode=code;
                session.input=session.Keep(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.RandomAccess));
                session.metadata=CntMetadata.ReadSmall(session.input,path,code);
                session.Snapshot=session.metadata.Snapshot;
                object map=PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPackageArchive","Inspect",new Type[]{typeof(Stream)},session.input);
                long outerOffset=Number(map,"OuterPfsOffset"),outerSize=Number(map,"OuterPfsSize");
                int outerSuperblock=checked((int)Number(map,"OuterSuperblockIndex"));
                if(outerSize<=0||outerSize%65536!=0||outerSuperblock<0)throw new NotSupportedException("Serve un PKG debug FIH/PPR leggibile.");
                Stream outer;
                if(session.Snapshot.Plaintext)
                    outer=(Stream)session.KeepObject(Create("LibProsperoPkg.Util.SubStream",session.input,outerOffset,outerSize));
                else
                {
                    byte[] seed=PackageSdk.ReadRange(session.input,checked(outerOffset+outerSuperblock*65536L+0x370),16);
                    object keys=PackageSdk.Call("LibProsperoPkg.PFS.ProsperoPfsKeys","DeriveImageEncryptionKeys",
                        new Type[]{typeof(string),typeof(string),typeof(byte[])},session.Snapshot.ContentId,code,seed);
                    object transform=session.KeepObject(Create("LibProsperoPkg.Util.XtsBlockTransform",BuilderBridge.Get(keys,"Item2"),BuilderBridge.Get(keys,"Item1")));
                    object pkg=PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPkgReader","Read",new Type[]{typeof(Stream)},session.input);
                    Array kinds=(Array)PackageSdk.Call("LibProsperoPkg.PKG.ProsperoPackageArchive","InferBlockKinds",
                        new Type[]{BuilderBridge.Get(pkg,"Fih").GetType(),typeof(int),typeof(int)},BuilderBridge.Get(pkg,"Fih"),checked((int)(outerSize/65536)),outerSuperblock);
                    MethodInfo decrypt=transform.GetType().GetMethod("DecryptSector",new Type[]{typeof(byte[]),typeof(ulong)});
                    outer=session.Keep(new CachedArchiveStream(outerSize,delegate(long offset,int count)
                    {
                        session.Token.ThrowIfCancellationRequested();
                        byte[] bytes=PackageSdk.ReadRange(session.input,checked(outerOffset+offset),count);
                        int index=checked((int)(offset/65536)),kind=Convert.ToInt32(kinds.GetValue(index));
                        if(kind!=2)
                        {
                            ulong sector=(ulong)index;if(kind==1)sector|=1UL<<47;
                            BuilderBridge.Invoke(decrypt,transform,bytes,sector);
                        }
                        return bytes;
                    },CancellationToken.None,65536,64));
                }
                object outerReader=session.KeepObject(Create("LibProsperoPkg.Util.StreamReader",outer,0L,false));
                object outerPfs=CreatePfs(outerReader,outerSuperblock*65536L);
                object image=FindOuter(outerPfs,"pfs_image.dat"),naps=FindOuter(outerPfs,"naps_pkg_layout.dat");
                long napsSize=Number(naps,"size");
                if(napsSize<16||napsSize>256L*1024*1024)throw new InvalidDataException("Indice NAPS non valido o troppo grande.");
                // ReadPlaintextNapsLayout returns a parsed object without a full-image extraction.
                // For encrypted packages use the same parser's byte[]-compatible Read stream adapter.
                byte[] layoutBytes=(byte[])Invoke(naps,"ReadAllBytes",new Type[]{typeof(bool)},false);
                session.layout=ParseNaps(layoutBytes);
                object imageView=session.KeepObject(Invoke(image,"GetView",Type.EmptyTypes));
                session.packed=(Stream)session.KeepObject(Create("LibProsperoPkg.Util.StreamWrapper",imageView,Number(image,"size")));
                object plan=PackageSdk.Call("LibProsperoPkg.PFS.Compression.ProsperoNapsImage","BuildPlan",new Type[]{session.layout.GetType()},session.layout);
                foreach(object span in (IEnumerable)BuilderBridge.Get(plan,"Spans"))
                    session.spans.Add(new SpanRange{Start=Number(span,"UncompressedOffset"),Length=checked((int)Number(span,"UncompressedLength")),Value=span});
                if(session.spans.Count==0)throw new InvalidDataException("L’indice NAPS non contiene blocchi.");
                session.decodeSpan=BuilderBridge.Library.GetType("LibProsperoPkg.PFS.Compression.ProsperoNapsImage",true).GetMethod("DecodeSpan",BuilderBridge.Members,null,
                    new Type[]{typeof(Stream),session.layout.GetType(),session.spans[0].Value.GetType()},null);
                if(session.decodeSpan==null)throw new MissingMethodException("NAPS DecodeSpan");
                long logicalLength=Number(plan,"UncompressedSize");
                session.indexing=true;
                CachedArchiveStream logical=session.Keep(new CachedArchiveStream(logicalLength,session.ReadLogical,CancellationToken.None,256*1024,16));
                long superblock=session.FindSuperblock(logical,plan);
                object innerReader=session.KeepObject(Create("LibProsperoPkg.Util.StreamReader",logical,0L,false));
                object inner=CreatePfs(innerReader,superblock);
                HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(object file in (IEnumerable)Invoke(inner,"GetAllFiles",Type.EmptyTypes))
                {
                    token.ThrowIfCancellationRequested();
                    string relative=Normalize(Convert.ToString(BuilderBridge.Get(file,"FullName"),CultureInfo.InvariantCulture));
                    if(!names.Add(relative))throw new InvalidDataException("Percorsi duplicati nel PKG: "+relative);
                    session.Files.Add(new PkgFileItem{Path=relative,Size=Number(file,"size"),Node=file});
                }
                foreach(CntEntry entry in session.metadata.Entries)
                {
                    if(entry.Id<0x400||string.IsNullOrWhiteSpace(entry.Name)||entry.Name.StartsWith("0x",StringComparison.OrdinalIgnoreCase))continue;
                    string relative=entry.Name.Replace('\\','/');
                    if(!relative.StartsWith("sce_sys/",StringComparison.OrdinalIgnoreCase))relative="sce_sys/"+relative;
                    relative=BackportPath.NormalizeRelative(relative);
                    if(names.Add(relative))session.Files.Add(new PkgFileItem{Path=relative,Size=entry.Size,Metadata=entry});
                }
                // param.json is authoritative in CNT, even when an inner duplicate exists.
                PkgFileItem param=session.Find("sce_sys/param.json");
                if(param==null)session.Files.Add(new PkgFileItem{Path="sce_sys/param.json",Size=session.Snapshot.ParamBytes.Length,SyntheticParam=true});
                session.Files.Sort(delegate(PkgFileItem a,PkgFileItem b){return StringComparer.OrdinalIgnoreCase.Compare(a.Path,b.Path);});
                session.indexing=false;session.Token=CancellationToken.None;
                if(log!=null)log(session.Files.Count+" file indicizzati; letti "+FileWorkspace.Size(session.decodedIndexBytes)+" di blocchi logici per l’indice. Il PKG non è stato estratto.");
                return session;
            }
            catch { session.Dispose();throw; }
        }

        private static object ParseNaps(byte[] bytes)
        {
            // The bundled parser only exposes ReadOnlySpan<byte>, which reflection cannot invoke.
            // A tiny generated bridge converts byte[] to the span inside the library's runtime.
            return NapsSpanBridge.Parse(bytes);
        }
        private byte[] ReadLogical(long offset,int count)
        {
            byte[] result=new byte[count];int written=0;
            int low=0,high=spans.Count-1;
            while(low<=high){int middle=(low+high)/2;if(spans[middle].Start<=offset)low=middle+1;else high=middle-1;}
            int index=high;
            while(written<count)
            {
                Token.ThrowIfCancellationRequested();
                if(index<0||index>=spans.Count)throw new EndOfStreamException("Offset NAPS fuori intervallo.");
                SpanRange span=spans[index];long within=offset+written-span.Start;
                if(within<0||within>=span.Length)throw new InvalidDataException("Blocco NAPS mancante.");
                if(span.Length>64*1024*1024)throw new InvalidDataException("Blocco NAPS troppo grande.");
                if(indexing&&decodedIndexBytes+span.Length>128L*1024*1024)
                    throw new NotSupportedException("Indice non trovato entro 128 MiB. Il viewer si ferma senza estrarre tutto il PKG.");
                byte[] decoded=(byte[])BuilderBridge.Invoke(decodeSpan,null,packed,layout,span.Value);
                if(decoded.Length!=span.Length)throw new InvalidDataException("Dimensione del blocco NAPS non valida.");
                if(indexing)decodedIndexBytes+=decoded.Length;
                int take=Math.Min(count-written,decoded.Length-(int)within);
                Buffer.BlockCopy(decoded,(int)within,result,written,take);written+=take;index++;
            }
            return result;
        }
        private long FindSuperblock(Stream logical,object plan)
        {
            List<long> candidates=new List<long>();
            foreach(object file in (IEnumerable)BuilderBridge.Get(plan,"Files"))candidates.Add(Number(file,"UncompressedOffset"));
            candidates.Sort();candidates.Reverse();candidates.Insert(0,0);
            HashSet<long> tried=new HashSet<long>();
            foreach(long candidate in candidates)
            {
                long aligned=(candidate+65535)&~65535L;
                if(IsSuperblock(logical,aligned,tried))return aligned;
            }
            // Publisher data-first layouts place filesystem metadata at the end.
            long start=Math.Max(0,logical.Length-16L*1024*1024);start=(start+65535)&~65535L;
            for(long at=start;at<=logical.Length-65536;at+=65536)if(IsSuperblock(logical,at,tried))return at;
            throw new NotSupportedException("Superblock PPR non trovato nelle regioni dell’indice. Nessuna estrazione completa è stata avviata.");
        }
        private bool IsSuperblock(Stream logical,long at,HashSet<long> tried)
        {
            Token.ThrowIfCancellationRequested();
            if(at<0||at>logical.Length-65536||!tried.Add(at))return false;
            byte[] header=PackageSdk.ReadRange(logical,at,32);
            return BinaryData.I64(header,0,true)==2&&header[8]==0x0b&&header[9]==0x2a&&header[10]==0x33&&header[11]==1;
        }
        internal PkgFileItem Find(string relative)
        { foreach(PkgFileItem item in Files)if(item.Path.Equals(relative,StringComparison.OrdinalIgnoreCase))return item;return null; }
        internal void Import(string relative,string source)
        {
            relative=BackportPath.NormalizeRelative(relative);FileWorkspace.NoReparse(source);
            if(!File.Exists(source))throw new FileNotFoundException("File da importare non trovato.",source);
            string reason=BackportPath.SkipReason(relative);
            if(reason!=null)throw new NotSupportedException("File gestito dal builder: "+relative+" ("+reason+").");
            if(Path.GetFullPath(source).Equals(Snapshot.Path,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Non puoi importare il PKG come file di sostituzione.");
            PkgFileItem item=Find(relative);
            if(item==null){item=new PkgFileItem{Path=relative};Files.Add(item);}
            item.Replacement=Path.GetFullPath(source);item.Removed=false;
        }
        internal void Remove(PkgFileItem item)
        {
            if(item.Added){Files.Remove(item);return;}
            if(item.Path.Equals("eboot.bin",StringComparison.OrdinalIgnoreCase)||item.Path.Equals("sce_sys/param.json",StringComparison.OrdinalIgnoreCase)||item.Path.Equals("sce_sys/keystone",StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Questo file è necessario per creare un PKG valido. Puoi sostituirlo.");
            item.Removed=true;
        }
        internal void Restore(PkgFileItem item)
        { if(item.Added)Files.Remove(item);else{item.Replacement=null;item.Removed=false;} }
        internal int Changes { get { int n=0;foreach(PkgFileItem file in Files)if(file.Replacement!=null||file.Removed)n++;return n; } }
        internal void Export(PkgFileItem item,string destination,bool original,CancellationToken token)
        {
            Token=token;
            try { AtomicFileExport.Write(destination,delegate(Stream output){Copy(item,output,original);},token); }
            finally { Token=CancellationToken.None; }
        }
        private void Copy(PkgFileItem item,Stream output,bool original)
        {
            Token.ThrowIfCancellationRequested();
            if(!original&&item.Replacement!=null)
            {
                FileWorkspace.NoReparse(item.Replacement);
                using(FileStream file=new FileStream(item.Replacement,FileMode.Open,FileAccess.Read,FileShare.Read))CopyStream(file,output,Token);
            }
            else if(item.Path.Equals("sce_sys/param.json",StringComparison.OrdinalIgnoreCase))output.Write(Snapshot.ParamBytes,0,Snapshot.ParamBytes.Length);
            else if(item.Metadata!=null){byte[] bytes=metadata.Plain(item.Metadata,passcode);output.Write(bytes,0,bytes.Length);}
            else if(item.Node!=null)Invoke(item.Node,"CopyTo",new Type[]{typeof(Stream),typeof(bool)},output,true);
            else throw new InvalidDataException("Il file aggiunto non ha una sorgente.");
            Token.ThrowIfCancellationRequested();
        }
        internal void Materialize(string destination,bool applyEdits,CancellationToken token,Action<ConversionProgress> report,Action<string> log)
        {
            long done=0,total=0;foreach(PkgFileItem item in Files)if(!item.Removed||!applyEdits)total+=item.CurrentSize;
            foreach(PkgFileItem item in Files)
            {
                token.ThrowIfCancellationRequested();
                if(applyEdits&&item.Removed||!applyEdits&&item.Added)continue;
                if(log!=null)log("Estrazione per ricostruzione: "+item.Path);
                Export(item,Path.Combine(destination,item.Path.Replace('/',Path.DirectorySeparatorChar)),!applyEdits,token);
                done+=item.CurrentSize;if(report!=null)report(new ConversionProgress("Preparo la ricostruzione completa",item.Path,done,total));
            }
        }
        internal static void CopyStream(Stream source,Stream target,CancellationToken token)
        { byte[] bytes=new byte[1024*1024];int count;while((count=source.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();target.Write(bytes,0,count);} }
        private static string Normalize(string path)
        { path=path.Replace('\\','/').TrimStart('/');if(path.StartsWith("uroot/",StringComparison.OrdinalIgnoreCase))path=path.Substring(6);return BackportPath.NormalizeRelative(path); }
        private static object FindOuter(object pfs,string name)
        {
            foreach(object file in (IEnumerable)Invoke(pfs,"GetAllFiles",Type.EmptyTypes))
                if(System.IO.Path.GetFileName(Convert.ToString(BuilderBridge.Get(file,"FullName")).Replace('/',System.IO.Path.DirectorySeparatorChar)).Equals(name,StringComparison.Ordinal))return file;
            throw new InvalidDataException("File del contenitore mancante: "+name);
        }
        private static object CreatePfs(object reader,long superblock)
        { return Create("LibProsperoPkg.PFS.PfsReader",reader,0UL,null,null,null,superblock,true); }
        private static object Create(string type,params object[] args)
        { return Activator.CreateInstance(BuilderBridge.Library.GetType(type,true),args); }
        private static object Invoke(object target,string method,Type[] types,params object[] args)
        { return BuilderBridge.Invoke(target.GetType().GetMethod(method,BuilderBridge.Members,null,types,null),target,args); }
        private static long Number(object target,string field)
        { return Convert.ToInt64(BuilderBridge.Get(target,field),CultureInfo.InvariantCulture); }
        private T Keep<T>(T value) where T:IDisposable { resources.Add(value);return value; }
        private object KeepObject(object value) { IDisposable disposable=value as IDisposable;if(disposable!=null)resources.Add(disposable);return value; }
        public void Dispose()
        { for(int i=resources.Count-1;i>=0;i--)try{resources[i].Dispose();}catch{}resources.Clear();Files.Clear(); }
    }

    internal static class NapsSpanBridge
    {
        private static Func<byte[],object> parse;
        internal static object Parse(byte[] bytes)
        {
            if(parse==null)
            {
                Type type=BuilderBridge.Library.GetType("LibProsperoPkg.PKG.ProsperoNapsLayout",true);
                MethodInfo method=null;
                foreach(MethodInfo candidate in type.GetMethods(BindingFlags.Public|BindingFlags.Static))
                    if(candidate.Name=="Parse"&&candidate.GetParameters().Length==1)method=candidate;
                if(method==null)throw new MissingMethodException("ProsperoNapsLayout.Parse");
                Type span=method.GetParameters()[0].ParameterType;
                System.Reflection.Emit.DynamicMethod bridge=new System.Reflection.Emit.DynamicMethod("ParseNapsBytes",typeof(object),new Type[]{typeof(byte[])},typeof(NapsSpanBridge),true);
                System.Reflection.Emit.ILGenerator il=bridge.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Newobj,span.GetConstructor(new Type[]{typeof(byte[])}));
                il.Emit(System.Reflection.Emit.OpCodes.Call,method);
                if(method.ReturnType.IsValueType)il.Emit(System.Reflection.Emit.OpCodes.Box,method.ReturnType);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                parse=(Func<byte[],object>)bridge.CreateDelegate(typeof(Func<byte[],object>));
            }
            return parse(bytes);
        }
    }
}
