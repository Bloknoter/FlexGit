#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Git LFS. Access through <see cref="GitRepository.Lfs"/>.
    /// Regular operations (clone, pull, switch, commit, push) handle LFS automatically through git's filters and
    /// hooks; call <see cref="EnsureInstalledAsync"/> once per repository to make sure those are set up.
    /// </summary>
    public sealed partial class LfsCommands
    {
        private readonly GitRepository _repository;

        internal LfsCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>True when git-lfs is installed on this machine.</summary>
        public bool IsInstalled => _repository.Git.IsLfsAvailable;

        /// <summary>True when any .gitattributes file in the repository routes files through the LFS filter.</summary>
        public async Task<bool> IsUsedInRepositoryAsync(CancellationToken cancellationToken = default)
        {
            var request = Context.Request("ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.gitattributes");
            request.ReadOnly = true;
            var result = await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);

            foreach (var path in result.StandardOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).Distinct())
            {
                var fullPath = _repository.GetFullPath(path);
                if (!File.Exists(fullPath))
                    continue;
                var content = await Task.Run(() => File.ReadAllText(fullPath), cancellationToken).ConfigureAwait(false);
                if (content.IndexOf("filter=lfs", StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Makes sure the LFS filters are configured and the LFS hooks (pre-push uploads LFS objects!) are installed in
        /// this repository, running <c>git lfs install --local</c> when needed. Returns true when something was installed.
        /// </summary>
        /// <exception cref="GitCommandException">git-lfs is not installed (<see cref="GitErrorKind.LfsNotInstalled"/>), or existing non-LFS hooks block the installation.</exception>
        public async Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default)
        {
            if (!IsInstalled)
                throw new GitCommandException(new GitError(GitErrorKind.LfsNotInstalled, "git-lfs is not installed. Install it from https://git-lfs.com (it ships with Git for Windows)."));

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var filter = await Context.ReadConfigAsync("filter.lfs.process", cancellationToken).ConfigureAwait(false);
                var hooksReady = await IsPrePushHookInstalledAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(filter) && hooksReady)
                    return false;

                await Context.RunCheckedAsync(Context.Request("lfs", "install", "--local"), cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        /// <summary>Starts tracking files matching <paramref name="pattern"/> (e.g. "*.psd") with LFS. Commit the updated .gitattributes.</summary>
        public Task TrackAsync(string pattern, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(pattern, nameof(pattern));
            return Context.RunLockedCheckedAsync(Context.Request("lfs", "track", pattern), cancellationToken);
        }

        /// <summary>Stops tracking <paramref name="pattern"/> with LFS. Commit the updated .gitattributes.</summary>
        public Task UntrackAsync(string pattern, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(pattern, nameof(pattern));
            return Context.RunLockedCheckedAsync(Context.Request("lfs", "untrack", pattern), cancellationToken);
        }

        /// <summary>
        /// Lists LFS locks on the server. With <paramref name="verifyOwnership"/>, each lock reports whether the current user owns it.
        /// Requires an LFS server with the locking API (GitHub, GitLab, ...).
        /// </summary>
        public async Task<IReadOnlyList<LfsLock>> ListLocksAsync(bool verifyOwnership = false, string? remote = null, CancellationToken cancellationToken = default)
        {
            var request = Context.Request("lfs", "locks", "--json");
            request.AddIf(verifyOwnership, "--verify");
            if (remote != null)
                request.Add("--remote=" + GitArguments.EnsureSafeRef(remote, nameof(remote)));
            var result = await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return LfsLockParser.ParseList(result.StandardOutput.Trim());
        }

        /// <summary>Locks a file on the LFS server so teammates can't push changes to it.</summary>
        public async Task<LfsLockResult> LockAsync(string path, string? remote = null, CancellationToken cancellationToken = default)
        {
            var request = Context.Request("lfs", "lock", "--json");
            if (remote != null)
                request.Add("--remote=" + GitArguments.EnsureSafeRef(remote, nameof(remote)));
            request.Add(EnsureSafePath(path));

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                var lockInfo = LfsLockParser.ParseSingle(result.StandardOutput.Trim());
                return new LfsLockResult(LfsLockStatus.Locked, $"Locked '{lockInfo?.Path ?? path}'.", lockInfo, null);
            }

            var error = result.ToError();
            var lockedByOther = (result.StandardError + result.StandardOutput).IndexOf("already locked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (result.StandardError + result.StandardOutput).IndexOf("Lock exists", StringComparison.OrdinalIgnoreCase) >= 0;
            return new LfsLockResult(lockedByOther ? LfsLockStatus.LockedByOtherUser : LfsLockStatus.Failed, error.Message, null, error);
        }

        /// <summary>Releases a lock. <paramref name="force"/> breaks a lock owned by someone else (needs permission).</summary>
        public Task<LfsLockResult> UnlockAsync(string path, bool force = false, string? remote = null, CancellationToken cancellationToken = default)
            => UnlockCoreAsync(EnsureSafePath(path), null, force, remote, cancellationToken);

        /// <summary>Releases a lock by its id.</summary>
        public Task<LfsLockResult> UnlockByIdAsync(string id, bool force = false, string? remote = null, CancellationToken cancellationToken = default)
            => UnlockCoreAsync(null, GitArguments.EnsureSafeRef(id, nameof(id)), force, remote, cancellationToken);

        /// <summary>Downloads LFS objects for the current checkout and replaces pointer files (normally automatic).</summary>
        public async Task<GitResult> PullAsync(string? remote = null, IProgress<GitProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var request = Context.Request("lfs", "pull");
            request.Progress = progress;
            if (remote != null)
                request.Add(GitArguments.EnsureSafeRef(remote, nameof(remote)));
            return await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Downloads LFS objects without touching the working tree.</summary>
        public async Task<GitResult> FetchAsync(string? remote = null, IProgress<GitProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var request = Context.Request("lfs", "fetch");
            request.Progress = progress;
            if (remote != null)
                request.Add(GitArguments.EnsureSafeRef(remote, nameof(remote)));
            return await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private async Task<LfsLockResult> UnlockCoreAsync(string? path, string? id, bool force, string? remote, CancellationToken cancellationToken)
        {
            var request = Context.Request("lfs", "unlock", "--json");
            request.AddIf(force, "--force");
            if (remote != null)
                request.Add("--remote=" + GitArguments.EnsureSafeRef(remote, nameof(remote)));
            if (id != null)
                request.Add("--id=" + id);
            else
                request.Add(path!);

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
                return new LfsLockResult(LfsLockStatus.Unlocked, "Unlocked.", null, null);
            var error = result.ToError();
            return new LfsLockResult(LfsLockStatus.Failed, error.Message, null, error);
        }

        private string EnsureSafePath(string path)
        {
            var repositoryPath = Context.ToRepositoryPath(path);
            if (repositoryPath.StartsWith("-", StringComparison.Ordinal))
                repositoryPath = "./" + repositoryPath;
            return repositoryPath;
        }

        private async Task<bool> IsPrePushHookInstalledAsync(CancellationToken cancellationToken)
        {
            var request = Context.Request("rev-parse", "--git-path", "hooks/pre-push");
            request.ReadOnly = true;
            var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return false;

            var hookPath = result.StandardOutput.Trim();
            if (!Path.IsPathRooted(hookPath))
                hookPath = Path.Combine(_repository.WorkingDirectory, hookPath);
            if (!File.Exists(hookPath))
                return false;
            var content = File.ReadAllText(hookPath);
            return content.IndexOf("git lfs", StringComparison.Ordinal) >= 0 || content.IndexOf("git-lfs", StringComparison.Ordinal) >= 0;
        }
    }
}
