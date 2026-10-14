using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace FpkgExfat
{
    internal static class TrophyFtp
    {
        internal const int Port = 2121;

        internal static string[] ListNames(string host, string remotePath)
        {
            try
            {
                return ListNamesCore(host, remotePath, WebRequestMethods.Ftp.ListDirectory, false);
            }
            catch
            {
                return ListNamesCore(host, remotePath, WebRequestMethods.Ftp.ListDirectoryDetails, true);
            }
        }

        private static string[] ListNamesCore(string host, string remotePath, string method, bool details)
        {
            FtpWebRequest request = Create(host, remotePath, method);
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                List<string> result = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    string value = line;
                    if (details)
                    {
                        string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) value = parts[parts.Length - 1];
                    }
                    value = value.Trim().TrimEnd('/');
                    int slash = value.LastIndexOf('/');
                    if (slash >= 0) value = value.Substring(slash + 1);
                    if (value.Length > 0 && value != "." && value != "..") result.Add(value);
                }
                return result.ToArray();
            }
        }

        internal static byte[] Download(string host, string remotePath)
        {
            FtpWebRequest request = Create(host, remotePath, WebRequestMethods.Ftp.DownloadFile);
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[65536];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    buffer.Write(chunk, 0, read);
                return buffer.ToArray();
            }
        }

        internal static void UploadText(string host, string remotePath, string text)
        {
            byte[] data = Encoding.ASCII.GetBytes(text ?? string.Empty);
            FtpWebRequest request = Create(host, remotePath, WebRequestMethods.Ftp.UploadFile);
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
                stream.Write(data, 0, data.Length);
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse()) { }
        }

        internal static string TryDownloadText(string host, string remotePath)
        {
            try
            {
                byte[] data = Download(host, remotePath);
                return Encoding.UTF8.GetString(data ?? new byte[0]).Trim();
            }
            catch { return string.Empty; }
        }

        internal static void TryDelete(string host, string remotePath)
        {
            try
            {
                FtpWebRequest request = Create(host, remotePath, WebRequestMethods.Ftp.DeleteFile);
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse()) { }
            }
            catch { }
        }

        private static FtpWebRequest Create(string host, string remotePath, string method)
        {
            string clean = remotePath.StartsWith("/", StringComparison.Ordinal) ? remotePath : "/" + remotePath;
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create("ftp://" + host.Trim() + ":" + Port + clean);
            request.Method = method;
            request.Credentials = new NetworkCredential("anonymous", string.Empty);
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;
            request.Timeout = 8000;
            request.ReadWriteTimeout = 10000;
            return request;
        }
    }
}
