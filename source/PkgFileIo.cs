using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FpkgExfat
{
    internal static class BoundedCnt
    {
        internal static long Length(Stream input, long offset, int maximum)
        {
            byte[] header=Read(input,offset,0x40);
            if(header[0]!=0x7f||header[1]!='C'||header[2]!='N'||header[3]!='T')throw new InvalidDataException("Invalid CNT magic.");
            int count=checked((int)Number(header,0x10,4));
            long table=checked((long)Number(header,0x18,4));
            long body=checked((long)Number(header,0x20,8)),size=checked((long)Number(header,0x28,8));
            if(count<1||count>65536||table<0x40||body<0x40)throw new InvalidDataException("Invalid CNT layout.");
            long length=Math.Max(0x1180,Math.Max(checked(table+count*32L),checked(body+size)));
            Require(input,offset,length,maximum);
            byte[] entries=Read(input,checked(offset+table),checked(count*32));
            for(int i=0;i<count;i++)
            {
                int start=i*32;
                long at=checked((long)Number(entries,start+16,4)),stored=checked((long)Number(entries,start+20,4));
                // Padding encrypted entries to 16 bytes is safe for both encrypted and plain entries.
                stored=checked((stored+15)&~15L);
                length=Math.Max(length,checked(at+stored));
            }
            length=checked((length+15)&~15L);
            Require(input,offset,length,maximum);
            return length;
        }
        private static void Require(Stream input,long offset,long size,int maximum)
        { if(offset<0||size<0||size>maximum||offset>input.Length-size)throw new InvalidDataException("Declared CNT metadata is invalid or exceeds 256 MiB."); }
        private static ulong Number(byte[] data,int offset,int width)
        { ulong result=0;for(int i=0;i<width;i++)result=(result<<8)|data[offset+i];return result; }
        private static byte[] Read(Stream input,long offset,int count)
        {
            if(offset<0||count<0||offset>input.Length-count)throw new EndOfStreamException();
            byte[] bytes=new byte[count];input.Position=offset;
            int read=0;while(read<count){int n=input.Read(bytes,read,count-read);if(n==0)throw new EndOfStreamException();read+=n;}return bytes;
        }
    }

    // Seekable logical archive view. Only touched blocks are decoded and retained.
    internal sealed class CachedArchiveStream : Stream
    {
        private readonly long length;
        private readonly Func<long,int,byte[]> decode;
        private readonly CancellationToken token;
        private readonly int blockSize, capacity;
        private readonly Dictionary<long,byte[]> cache=new Dictionary<long,byte[]>();
        private readonly LinkedList<long> order=new LinkedList<long>();
        private long position;
        internal CachedArchiveStream(long size,Func<long,int,byte[]> decoder,CancellationToken cancellation,int chunkSize,int slots)
        {
            if(size<0||decoder==null||chunkSize<=0||slots<1)throw new ArgumentException("Invalid archive cache.");
            length=size;decode=decoder;token=cancellation;blockSize=chunkSize;capacity=slots;
        }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return length; } }
        public override long Position { get { return position; } set { if(value<0)throw new ArgumentOutOfRangeException("value");position=value; } }
        public override int Read(byte[] buffer,int offset,int count)
        {
            if(buffer==null||offset<0||count<0||offset>buffer.Length-count)throw new ArgumentException("Invalid read range.");
            token.ThrowIfCancellationRequested();
            int remaining=(int)Math.Min(count,Math.Max(0,length-position)),total=remaining;
            while(remaining>0)
            {
                token.ThrowIfCancellationRequested();
                long start=position/blockSize*blockSize;
                byte[] bytes;
                if(!cache.TryGetValue(start,out bytes))
                {
                    int wanted=(int)Math.Min(blockSize,length-start);
                    bytes=decode(start,wanted);
                    if(bytes==null||bytes.Length!=wanted)throw new EndOfStreamException("Incomplete logical archive block.");
                    if(cache.Count==capacity){long oldest=order.First.Value;order.RemoveFirst();cache.Remove(oldest);}
                    cache.Add(start,bytes);
                }
                else order.Remove(start);
                order.AddLast(start);
                int within=(int)(position-start),take=Math.Min(remaining,bytes.Length-within);
                Buffer.BlockCopy(bytes,within,buffer,offset,take);
                position+=take;offset+=take;remaining-=take;
            }
            return total;
        }
        public override long Seek(long offset,SeekOrigin origin)
        {
            long next=checked((origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length)+offset);
            Position=next;return position;
        }
        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer,int offset,int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if(disposing){cache.Clear();order.Clear();}base.Dispose(disposing); }
    }

    internal static class AtomicFileExport
    {
        internal static void Write(string target,Action<Stream> copy,CancellationToken token)
        {
            target=Path.GetFullPath(target);
            if(File.Exists(target)||Directory.Exists(target))throw new IOException("Il file esiste già: "+target);
            string parent=Path.GetDirectoryName(target);
            RequireOrdinaryPath(parent);
            Directory.CreateDirectory(parent);
            RequireOrdinaryPath(parent);
            string partial=Path.Combine(parent,Path.GetFileName(target)+".partial-"+Guid.NewGuid().ToString("N"));
            try
            {
                token.ThrowIfCancellationRequested();
                using(FileStream output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { copy(output);token.ThrowIfCancellationRequested();output.Flush(true); }
                token.ThrowIfCancellationRequested();File.Move(partial,target);
            }
            finally { if(File.Exists(partial))File.Delete(partial); }
        }
        internal static void RequireOrdinaryPath(string path)
        {
            string current=Path.GetFullPath(path);
            while(!string.IsNullOrEmpty(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Il percorso contiene un collegamento/reparse point: "+current);
                current=Path.GetDirectoryName(current);
            }
        }
    }
}
