using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace FpkgExfat
{
    internal sealed class ToolResult
    {
        internal int ExitCode;
        internal string Output;
    }

    internal static class ProcessRunner
    {
        // CommandLineToArgvW quoting. Never run an image path through cmd.exe.
        internal static string Quote(string argument)
        {
            if (argument == null || argument.IndexOf('\0') >= 0) throw new ArgumentException("Invalid process argument.");
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { result.Append('\\', slashes * 2 + 1); result.Append('"'); }
                else { result.Append('\\', slashes); result.Append(character); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        internal static string Arguments(params string[] arguments)
        {
            string[] quoted = new string[arguments.Length];
            for (int i = 0; i < arguments.Length; i++) quoted[i] = Quote(arguments[i]);
            return string.Join(" ", quoted);
        }

        internal static ToolResult Run(string executable, string[] arguments, int timeoutMilliseconds,
            CancellationToken token, Action<string> log, bool utf8 = false)
        {
            return RunCore(executable, arguments, timeoutMilliseconds, token, log, utf8, null, null);
        }

        internal static ToolResult RunWithEnvironment(string executable, string[] arguments, int timeoutMilliseconds,
            CancellationToken token, Action<string> log, string workingDirectory, IDictionary<string, string> environment)
        {
            return RunCore(executable, arguments, timeoutMilliseconds, token, log, true, workingDirectory, environment);
        }

        private static ToolResult RunCore(string executable, string[] arguments, int timeoutMilliseconds,
            CancellationToken token, Action<string> log, bool utf8, string workingDirectory, IDictionary<string, string> environment)
        {
            token.ThrowIfCancellationRequested();
            StringBuilder output = new StringBuilder();
            object gate = new object();
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, Arguments(arguments))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? System.IO.Path.GetDirectoryName(executable) : workingDirectory
                };
                if (environment != null)
                    foreach (KeyValuePair<string, string> item in environment)
                        process.StartInfo.EnvironmentVariables[item.Key] = item.Value;
                if (utf8)
                {
                    process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                    process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                }
                DataReceivedEventHandler receive = delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null) return;
                    lock (gate) { if (output.Length < 65536) output.AppendLine(args.Data); }
                    if (log != null) log(args.Data);
                };
                process.OutputDataReceived += receive;
                process.ErrorDataReceived += receive;
                process.Start();
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                Stopwatch clock = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        token.ThrowIfCancellationRequested();
                        if (timeoutMilliseconds > 0 && clock.ElapsedMilliseconds >= timeoutMilliseconds)
                            throw new TimeoutException("The worker did not respond within " + timeoutMilliseconds / 1000 + " seconds.");
                    }
                    // Flush async stdout/stderr after OSFMount itself has exited.
                    process.WaitForExit();
                    token.ThrowIfCancellationRequested();
                    lock (gate) return new ToolResult { ExitCode = process.ExitCode, Output = output.ToString() };
                }
                catch
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }
                        if (!process.WaitForExit(5000))
                            throw new IOExceptionWithTool("The worker process is still running. Check the operation log before retrying.");
                    }
                    throw;
                }
            }
        }
    }

    internal sealed class IOExceptionWithTool : System.IO.IOException
    {
        internal IOExceptionWithTool(string message) : base(message) { }
    }
}
