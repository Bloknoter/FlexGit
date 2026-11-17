#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FlexGit
{
    /// <summary>
    /// Kills a process together with its children. git spawns helpers (git-remote-https, git-lfs,
    /// credential helpers, sh for aliases/hooks), and Process.Kill(entireProcessTree) does not exist
    /// on .NET Standard / Mono.
    /// </summary>
    internal static class ProcessTree
    {
        public static void Kill(Process process)
        {
            try
            {
                if (process.HasExited)
                    return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var taskkill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
                    var startInfo = new ProcessStartInfo(taskkill, $"/T /F /PID {process.Id}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var killer = Process.Start(startInfo))
                    {
                        killer?.WaitForExit(5000);
                    }
                }
                catch (Exception)
                {
                    // Fall back to killing only the git process below.
                }
            }

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // Already exited or access denied: nothing else we can do.
            }
        }
    }
}
