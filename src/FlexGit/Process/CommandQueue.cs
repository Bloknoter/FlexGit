#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Serializes FlexGit operations per repository (keyed by git directory), across all
    /// <see cref="GitRepository"/> instances, so FlexGit never races itself on index.lock.
    /// </summary>
    internal static class CommandQueue
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_locks = new ConcurrentDictionary<string, SemaphoreSlim>(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public static async Task<IDisposable> AcquireAsync(string gitDirectory, CancellationToken cancellationToken)
        {
            var key = Path.GetFullPath(gitDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var semaphore = s_locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(semaphore);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _semaphore;

            public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

            public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
