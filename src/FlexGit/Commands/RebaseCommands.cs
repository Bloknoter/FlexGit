#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Rebasing the current branch. Access through <see cref="GitRepository.Rebase"/>.
    /// On <see cref="RebaseStatus.Conflicts"/>: resolve with <see cref="GitRepository.Conflicts"/>, then
    /// <see cref="ContinueAsync"/> (or <see cref="SkipAsync"/> / <see cref="AbortAsync"/>).
    /// </summary>
    public sealed partial class RebaseCommands
    {
        private readonly GitRepository _repository;

        internal RebaseCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>True while a rebase is in progress.</summary>
        public bool IsInProgress => RepositoryStateDetector.Detect(_repository.GitDirectory) == RepositoryState.Rebasing;

        /// <summary>Replays the current branch's commits on top of <paramref name="upstream"/>.</summary>
        /// <param name="upstream">Branch/revision to rebase onto, e.g. "main" or "origin/main".</param>
        /// <param name="onto">Optional new base (<c>--onto</c>); commits after <paramref name="upstream"/> are replayed onto it.</param>
        /// <param name="autoStash">Stash local changes before and re-apply them after the rebase.</param>
        public async Task<RebaseResult> StartAsync(string upstream, string? onto = null, bool autoStash = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(upstream, nameof(upstream));
            var request = Context.Request("rebase");
            request.AddIf(autoStash, "--autostash");
            if (onto != null)
                request.Add("--onto").Add(GitArguments.EnsureSafeRef(onto, nameof(onto)));
            request.Add(upstream);

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var before = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                var after = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return before == after
                        ? new RebaseResult(RebaseStatus.UpToDate, "Current branch is up to date.", after, null)
                        : new RebaseResult(RebaseStatus.Completed, $"Rebased; HEAD is {GitRepository.Short(after)}.", after, null);
                }
                return await InterpretFailureAsync(result, after, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Continues after the conflicts of the current step were resolved (and staged).</summary>
        public Task<RebaseResult> ContinueAsync(CancellationToken cancellationToken = default) => StepAsync("--continue", true, cancellationToken);

        /// <summary>Drops the commit of the current step and continues.</summary>
        public Task<RebaseResult> SkipAsync(CancellationToken cancellationToken = default) => StepAsync("--skip", false, cancellationToken);

        /// <summary>Stops the rebase and restores the branch as it was before.</summary>
        public async Task<RebaseResult> AbortAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsInProgress)
                    return new RebaseResult(RebaseStatus.NoRebaseInProgress, "No rebase in progress.", null, null);
                var result = await Context.RunAsync(Context.Request("rebase", "--abort"), cancellationToken).ConfigureAwait(false);
                var head = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    return new RebaseResult(RebaseStatus.Aborted, "Rebase aborted.", head, null);
                var error = result.ToError();
                return new RebaseResult(RebaseStatus.Failed, error.Message, head, error);
            }
        }

        private async Task<RebaseResult> StepAsync(string argument, bool requireResolved, CancellationToken cancellationToken)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsInProgress)
                    return new RebaseResult(RebaseStatus.NoRebaseInProgress, "No rebase in progress.", null, null);

                if (requireResolved)
                {
                    var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                    if (status.HasConflicts)
                        return new RebaseResult(RebaseStatus.Conflicts, "Resolve all conflicts before continuing.", status.HeadSha, null);
                }

                var result = await Context.RunAsync(Context.Request("rebase", argument), cancellationToken).ConfigureAwait(false);
                var head = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                if (result.Succeeded && !IsInProgress)
                    return new RebaseResult(RebaseStatus.Completed, $"Rebase completed; HEAD is {GitRepository.Short(head)}.", head, null);
                return await InterpretFailureAsync(result, head, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<RebaseResult> InterpretFailureAsync(GitResult result, string? head, CancellationToken cancellationToken)
        {
            var error = result.ToError();
            if (IsInProgress)
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                if (status.HasConflicts)
                    return new RebaseResult(RebaseStatus.Conflicts, "The rebase stopped because of conflicts. Resolve them, then continue.", head, error);
            }

            return error.Kind == GitErrorKind.LocalChangesWouldBeOverwritten
                ? new RebaseResult(RebaseStatus.LocalChangesWouldBeOverwritten, error.Message, head, error)
                : new RebaseResult(RebaseStatus.Failed, error.Message, head, error);
        }
    }
}
