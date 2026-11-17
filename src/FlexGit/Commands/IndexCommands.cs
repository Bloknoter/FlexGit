#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Staging, unstaging and discarding changes. Access through <see cref="GitRepository.Index"/>.
    /// Paths may be repository-relative or absolute; they are matched literally (no globs).
    /// </summary>
    public sealed partial class IndexCommands
    {
        private readonly GitRepository _repository;

        internal IndexCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Stages additions, modifications and deletions of the given paths.</summary>
        public async Task StageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return;

            var request = Context.Request("add", "--all");
            GitContext.AddPathspecsViaStdin(request, repositoryPaths);
            await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Stages every change in the repository (<c>git add --all</c>).</summary>
        public Task StageAllAsync(CancellationToken cancellationToken = default)
            => Context.RunLockedCheckedAsync(Context.Request("add", "--all"), cancellationToken);

        /// <summary>Removes the given paths from the index; working tree files are untouched.</summary>
        public async Task UnstageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return;

            var request = Context.Request("reset", "--quiet");
            GitContext.AddPathspecsViaStdin(request, repositoryPaths);
            await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Unstages everything; working tree files are untouched.</summary>
        public Task UnstageAllAsync(CancellationToken cancellationToken = default)
            => Context.RunLockedCheckedAsync(Context.Request("reset", "--quiet"), cancellationToken);

        /// <summary>
        /// Reverts the given paths to their committed state (index and working tree). Destructive.
        /// Untracked paths are deleted only when <paramref name="deleteUntracked"/> is true.
        /// </summary>
        public async Task DiscardAsync(IEnumerable<string> paths, bool deleteUntracked = false, CancellationToken cancellationToken = default)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return;

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { Paths = repositoryPaths }, cancellationToken).ConfigureAwait(false);
                var untracked = new HashSet<string>(status.Untracked.Select(e => e.Path), StringComparer.Ordinal);
                var tracked = repositoryPaths.Where(p => !untracked.Contains(p) && !untracked.Any(u => u.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal))).ToList();
                var untrackedTargets = repositoryPaths.Except(tracked).ToList();

                if (tracked.Count > 0)
                {
                    if (status.IsUnborn)
                    {
                        // No commit to restore from: just unstage; the files become untracked.
                        var unstage = Context.Request("reset", "--quiet");
                        GitContext.AddPathspecsViaStdin(unstage, tracked);
                        await Context.RunCheckedAsync(unstage, cancellationToken).ConfigureAwait(false);
                        untrackedTargets.AddRange(tracked);
                    }
                    else
                    {
                        var restore = Context.Request("restore", "--source=HEAD", "--staged", "--worktree");
                        GitContext.AddPathspecsViaStdin(restore, tracked);
                        await Context.RunCheckedAsync(restore, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (deleteUntracked && untrackedTargets.Count > 0)
                {
                    foreach (var chunk in Chunk(untrackedTargets, 100))
                    {
                        var clean = Context.Request("clean", "--force", "-d", "--quiet", "--");
                        clean.Arguments.AddRange(chunk.Select(GitArguments.LiteralPathspec));
                        await Context.RunCheckedAsync(clean, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Discards every change (<c>git reset --hard</c>) and optionally deletes untracked files and folders. Destructive.
        /// Ignored files (e.g. build output) are never deleted.
        /// </summary>
        public async Task DiscardAllAsync(bool deleteUntracked = false, CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                await Context.RunCheckedAsync(Context.Request("reset", "--hard", "--quiet"), cancellationToken).ConfigureAwait(false);
                if (deleteUntracked)
                    await Context.RunCheckedAsync(Context.Request("clean", "--force", "-d", "--quiet"), cancellationToken).ConfigureAwait(false);
            }
        }

        private static IEnumerable<List<string>> Chunk(List<string> items, int size)
        {
            for (var i = 0; i < items.Count; i += size)
                yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }
    }
}
