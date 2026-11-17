#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    public sealed partial class TagCommands
    {
        /// <summary>Pushes one tag to a remote.</summary>
        public async Task<PushResult> PushAsync(string name, string remote = "origin", CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            GitArguments.EnsureSafeRef(remote, nameof(remote));
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await _repository.RunPushCoreAsync(new[] { remote, "refs/tags/" + name }, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pushes all local tags to a remote.</summary>
        public async Task<PushResult> PushAllAsync(string remote = "origin", CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(remote, nameof(remote));
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await _repository.RunPushCoreAsync(new[] { "--tags", remote }, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Deletes a tag on a remote (the local tag is kept).</summary>
        public async Task<PushResult> DeleteRemoteAsync(string name, string remote = "origin", CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            GitArguments.EnsureSafeRef(remote, nameof(remote));
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await _repository.RunPushCoreAsync(new[] { remote, "--delete", "refs/tags/" + name }, null, cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed partial class BranchCommands
    {
        /// <summary>Deletes a branch on a remote (the local branch is kept).</summary>
        public async Task<PushResult> DeleteRemoteAsync(string name, string remote = "origin", CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            GitArguments.EnsureSafeRef(remote, nameof(remote));
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await _repository.RunPushCoreAsync(new[] { remote, "--delete", "refs/heads/" + name }, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
