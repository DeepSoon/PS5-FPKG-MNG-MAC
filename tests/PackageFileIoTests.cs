using System;
using System.IO;
using System.Threading;
using FpkgExfat;

internal static class PackageFileIoTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Big32(byte[] b, int at, uint value)
    { for (int i=3; i>=0; i--) { b[at+i]=(byte)value; value>>=8; } }
    private static void Big64(byte[] b, int at, ulong value)
    { for (int i=7; i>=0; i--) { b[at+i]=(byte)value; value>>=8; } }

    public static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(), "pkg-file-io-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] cnt=new byte[0x3000]; cnt[0]=0x7f; cnt[1]=(byte)'C'; cnt[2]=(byte)'N'; cnt[3]=(byte)'T';
            Big32(cnt,0x10,1); Big32(cnt,0x18,0x1180); Big64(cnt,0x20,0x1200); Big64(cnt,0x28,0x1000);
            Big32(cnt,0x1180+16,0x1200); Big32(cnt,0x1180+20,0x1000);
            string pkg=Path.Combine(root,"large-supplement.pkg");
            using(FileStream f=new FileStream(pkg,FileMode.CreateNew,FileAccess.ReadWrite))
            {
                f.Position=65536; f.Write(cnt,0,cnt.Length); f.SetLength(512L*1024*1024);
                long bounded=-1;
                try { bounded=BoundedCnt.Length(f,65536,256*1024*1024); } catch(InvalidDataException) { }
                Check(bounded==0x2200,"CNT reader must ignore the 512 MiB supplement");
                Check(f.Position<65536+0x2200,"CNT inspection must not scan the supplement");
                f.Position=65536+0x28; byte[] huge=new byte[8]; Big64(huge,0,400UL*1024*1024); f.Write(huge,0,8);
                bool refused=false; try { BoundedCnt.Length(f,65536,256*1024*1024); } catch(InvalidDataException) { refused=true; }
                Check(refused,"Truly oversized declared metadata must still be rejected");
            }

            int reads=0;
            using(CachedArchiveStream stream=new CachedArchiveStream(1024*1024,delegate(long offset,int length)
            {
                reads++; byte[] bytes=new byte[length]; for(int i=0;i<length;i++)bytes[i]=(byte)((offset+i)%251); return bytes;
            },CancellationToken.None,65536,4))
            {
                byte[] bytes=new byte[24]; stream.Position=65530; Check(stream.Read(bytes,0,24)==24,"Read across cache boundaries");
                for(int i=0;i<24;i++)Check(bytes[i]==(byte)((65530+i)%251),"Correct bytes at cache boundary");
                stream.Position=65530; stream.Read(bytes,0,24); Check(reads==2,"Repeated reads reuse decoded blocks");
                stream.Seek(-4,SeekOrigin.End); Check(stream.Read(bytes,0,24)==4,"EOF returns only available bytes");
                Check(stream.Read(bytes,0,24)==0,"EOF returns zero");
            }
            string target=Path.Combine(root,"eboot.bin");
            bool failed=false;
            try { AtomicFileExport.Write(target,delegate(Stream f) { f.WriteByte(7); throw new IOException("decode failed"); },CancellationToken.None); }
            catch(IOException) { failed=true; }
            Check(failed&&!File.Exists(target),"A failed extraction never publishes a partial file");
            Check(Directory.GetFiles(root,"*.partial-*").Length==0,"Failed extraction cleans its temporary file");
            AtomicFileExport.Write(target,delegate(Stream f) { f.Write(new byte[]{1,2,3},0,3); },CancellationToken.None);
            Check(File.ReadAllBytes(target).Length==3,"Selected-file extraction publishes complete data");
            bool collision=false; try { AtomicFileExport.Write(target,delegate(Stream f) { f.WriteByte(8); },CancellationToken.None); } catch(IOException) { collision=true; }
            Check(collision&&File.ReadAllBytes(target)[0]==1,"Extraction refuses to overwrite an existing file");
            CancellationTokenSource cancel=new CancellationTokenSource();cancel.Cancel();
            bool cancelled=false; try { AtomicFileExport.Write(Path.Combine(root,"cancelled.bin"),delegate(Stream f){f.WriteByte(1);},cancel.Token); } catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&!File.Exists(Path.Combine(root,"cancelled.bin")),"Cancellation does not publish output");
            Console.WriteLine("PASS bounded CNT metadata, cached selected reads, atomic extraction and cancellation");return 0;
        }
        finally { Directory.Delete(root,true); }
    }
}
