using System;
using System.IO;
using System.Text;

namespace FpkgExfat
{
    internal enum ImageFormat { Auto, Exfat, Pfs, Ufs2 }

    internal static class ImageFormats
    {
        internal static ImageFormat Detect(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ffpkg") return ImageFormat.Ufs2;
            byte[] header = new byte[16];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (stream.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("The source image is truncated.");
            if (Encoding.ASCII.GetString(header, 3, 8) == "EXFAT   ") return ImageFormat.Exfat;
            if (BitConverter.ToInt64(header, 8) == 20130315L &&
                (BitConverter.ToInt64(header, 0) == 1 || BitConverter.ToInt64(header, 0) == 2)) return ImageFormat.Pfs;
            if (ext == ".ffpfs" || ext == ".ffpfsc" || ext == ".pfs") return ImageFormat.Pfs;
            throw new InvalidDataException("Unrecognized image header. Supported: exFAT, FFPFS/FFPFSC/PFS, and FFPKG/UFS2.");
        }

        internal static string Name(ImageFormat format)
        {
            if (format == ImageFormat.Pfs) return "FFPFS / FFPFSC / PFS";
            if (format == ImageFormat.Ufs2) return "FFPKG / UFS2";
            return "exFAT";
        }
    }
}
