#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    public sealed partial class GitRepository
    {
        /// <summary>
        /// Clones <paramref name="url"/> into <paramref name="directory"/> (which must be missing or empty).
        /// Credentials come from git's credential helpers (Git Credential Manager on Windows).
        /// </summary>
        public static async Task<CloneResult> CloneAsync(string url, string directory, CloneOptions? cloneOptions = null,
            IProgress<GitProgress>? progress = null, GitOptions? options = null, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(url, nameof(url));
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory cannot be null or empty.", nameof(directory));

            cloneOptions ??= new CloneOptions();
            options ??= new GitOptions();
            var fullPath = Path.GetFullPath(directory);
            if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
                return new CloneResult(CloneStatus.DirectoryNotEmpty, $"'{fullPath}' is not empty.", null, null);

            var parent = Path.GetDirectoryName(fullPath) ?? fullPath;
            Directory.CreateDirectory(parent);

            var installation = await GitLocator.LocateAsync(options, cancellationToken).ConfigureAwait(false);
            var runner = new GitRunner(installation, options);
            var request = new GitRunRequest("clone", "--progress") { WorkingDirectory = parent, Progress = progress };
            if (cloneOptions.Branch != null)
                request.Add("--branch=" + GitArguments.EnsureSafeRef(cloneOptions.Branch, nameof(cloneOptions.Branch)));
            if (cloneOptions.Depth != null)
                request.Add("--depth=" + cloneOptions.Depth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.AddIf(cloneOptions.RecurseSubmodules, "--recurse-submodules");
            request.AddIf(cloneOptions.Bare, "--bare");
            request.Add("--").Add(url).Add(fullPath);

            var result = await runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var error = result.ToError();
                return new CloneResult(CloneStatus.Failed, error.Message, null, error);
            }

            var repository = await OpenAsync(fullPath, options, cancellationToken).ConfigureAwait(false);
            return new CloneResult(CloneStatus.Cloned, $"Cloned into '{fullPath}'.", repository, null);
        }

        /// <summary>Downloads commits and refs from a remote (or all remotes) without changing local branches.</summary>
        public async Task<FetchResult> FetchAsync(FetchOptions? options = null, IProgress<GitProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            options ??= new FetchOptions();
            var request = Context.Request("fetch", "--progress");
            request.Progress = progress;
            request.AddIf(options.Prune, "--prune");
            request.AddIf(options.Tags, "--tags");
            if (options.AllRemotes)
                request.Add("--all");
            else if (options.Remote != null)
                request.Add(GitArguments.EnsureSafeRef(options.Remote, nameof(options.Remote)));

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
                return new FetchResult(FetchStatus.Succeeded, "Fetch completed.", null);
            var error = result.ToError();
            return new FetchResult(FetchStatus.Failed, error.Message, error);
        }

        /// <summary>
        /// Fetches and integrates the upstream branch. Conflicts leave the repository merging/rebasing
        /// (<see cref="PullStatus.Conflicts"/>): resolve with <see cref="Conflicts"/>, then
        /// <see cref="ContinueMergeAsync"/> or <see cref="RebaseCommands.ContinueAsync"/>.
        /// </summary>
        public async Task<PullResult> PullAsync(PullOptions? options = null, IProgress<GitProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            options ??= new PullOptions();
            var request = Context.Request("pull", "--progress");
            request.Progress = progress;
            switch (options.Mode)
            {
                case PullMode.Rebase:
                    request.Add("--rebase");
                    break;
                case PullMode.FastForwardOnly:
                    request.Add("--ff-only");
                    break;
                default:
                    request.Add("--no-rebase").Add("--no-edit");
                    break;
            }
            request.AddIf(options.AutoStash, "--autostash");

            if (options.Remote != null)
            {
                request.Add(GitArguments.EnsureSafeRef(options.Remote, nameof(options.Remote)));
                if (options.Branch != null)
                    request.Add(GitArguments.EnsureSafeRef(options.Branch, nameof(options.Branch)));
            }
            else if (options.Branch != null)
            {
                throw new ArgumentException("PullOptions.Branch requires PullOptions.Remote.", nameof(options));
            }

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var before = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                var after = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    if (before == after)
                        return new PullResult(PullStatus.UpToDate, "Already up to date.", after, null);
                    if (await CountParentsAsync(after, cancellationToken).ConfigureAwait(false) > 1)
                        return new PullResult(PullStatus.Merged, $"Merged into {Short(after)}.", after, null);
                    if (options.Mode == PullMode.Rebase && before != null && !await IsAncestorAsync(before, after, cancellationToken).ConfigureAwait(false))
                        return new PullResult(PullStatus.Rebased, $"Rebased onto upstream, HEAD is {Short(after)}.", after, null);
                    return new PullResult(PullStatus.FastForwarded, $"Fast-forwarded to {Short(after)}.", after, null);
                }

                var error = result.ToError();
                if (await HasConflictsAsync(cancellationToken).ConfigureAwait(false))
                    return new PullResult(PullStatus.Conflicts, "The pull produced conflicts. Resolve them, then continue.", after, error);

                var status = error.Kind switch
                {
                    GitErrorKind.LocalChangesWouldBeOverwritten => PullStatus.LocalChangesWouldBeOverwritten,
                    GitErrorKind.NoUpstream => PullStatus.NoUpstream,
                    GitErrorKind.CannotFastForward => PullStatus.CannotFastForward,
                    _ => PullStatus.Failed
                };
                return new PullResult(status, error.Message, after, error);
            }
        }

        /// <summary>
        /// Pushes the current branch (or <see cref="PushOptions.RefSpec"/>). The first push of a branch sets its upstream
        /// automatically. A non-fast-forward push is reported as <see cref="PushStatus.Rejected"/>: pull first.
        /// </summary>
        public async Task<PushResult> PushAsync(PushOptions? options = null, IProgress<GitProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            options ??= new PushOptions();
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var branch = await Context.ReadCurrentBranchAsync(cancellationToken).ConfigureAwait(false);
                var remote = options.Remote ?? await ResolvePushRemoteAsync(branch, cancellationToken).ConfigureAwait(false);
                GitArguments.EnsureSafeRef(remote, nameof(options.Remote));

                var setUpstream = options.SetUpstream ?? (options.RefSpec == null && branch != null &&
                                                          await Context.ReadConfigAsync($"branch.{branch}.merge", cancellationToken).ConfigureAwait(false) == null);

                var arguments = new List<string>();
                if (setUpstream)
                    arguments.Add("--set-upstream");
                if (options.FollowTags)
                    arguments.Add("--follow-tags");
                if (options.AllTags)
                    arguments.Add("--tags");
                if (options.ForceWithLease)
                    arguments.Add("--force-with-lease");
                arguments.Add(remote);
                arguments.Add(options.RefSpec != null ? GitArguments.EnsureSafeRef(options.RefSpec, nameof(options.RefSpec)) : "HEAD");

                return await RunPushCoreAsync(arguments, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Merges <paramref name="revision"/> (e.g. "feature" or "origin/main") into the current branch.
        /// Conflicts leave the repository merging (<see cref="MergeStatus.Conflicts"/>).
        /// </summary>
        public async Task<MergeResult> MergeAsync(string revision, MergeOptions? options = null, IProgress<GitProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            options ??= new MergeOptions();

            var request = Context.Request("merge", "--no-edit");
            request.Progress = progress;
            request.AddIf(progress != null, "--progress");
            request.Add(options.FastForward switch
            {
                FastForwardMode.Only => "--ff-only",
                FastForwardMode.Never => "--no-ff",
                _ => "--ff"
            });
            request.AddIf(options.Squash, "--squash");
            if (options.Message != null)
                request.Add("-m").Add(options.Message);
            request.Add(revision);

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var before = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                var after = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    var upToDate = result.StandardOutput.IndexOf("Already up to date", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (options.Squash)
                        return upToDate
                            ? new MergeResult(MergeStatus.UpToDate, "Already up to date.", after, null)
                            : new MergeResult(MergeStatus.Squashed, "Changes are staged; commit to finish the squash merge.", after, null);
                    if (before == after)
                        return new MergeResult(MergeStatus.UpToDate, "Already up to date.", after, null);
                    if (await CountParentsAsync(after, cancellationToken).ConfigureAwait(false) > 1)
                        return new MergeResult(MergeStatus.Merged, $"Merged '{revision}' ({Short(after)}).", after, null);
                    return new MergeResult(MergeStatus.FastForwarded, $"Fast-forwarded to {Short(after)}.", after, null);
                }

                var error = result.ToError();
                if (await HasConflictsAsync(cancellationToken).ConfigureAwait(false))
                    return new MergeResult(MergeStatus.Conflicts, "The merge produced conflicts. Resolve them, then continue.", after, error);

                var status = error.Kind switch
                {
                    GitErrorKind.LocalChangesWouldBeOverwritten => MergeStatus.LocalChangesWouldBeOverwritten,
                    GitErrorKind.CannotFastForward => MergeStatus.CannotFastForward,
                    _ => MergeStatus.Failed
                };
                return new MergeResult(status, error.Message, after, error);
            }
        }

        /// <summary>Aborts a merge in progress and restores the pre-merge state.</summary>
        public async Task<MergeResult> AbortMergeAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await Context.RunAsync(Context.Request("merge", "--abort"), cancellationToken).ConfigureAwait(false);
                var head = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    return new MergeResult(MergeStatus.Aborted, "Merge aborted.", head, null);
                var error = result.ToError();
                return error.Kind == GitErrorKind.NoOperationInProgress
                    ? new MergeResult(MergeStatus.NoMergeInProgress, "No merge in progress.", head, error)
                    : new MergeResult(MergeStatus.Failed, error.Message, head, error);
            }
        }

        /// <summary>Concludes a merge after all conflicts were resolved (commits with git's prepared message).</summary>
        public async Task<MergeResult> ContinueMergeAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (RepositoryStateDetector.Detect(GitDirectory) != RepositoryState.Merging)
                    return new MergeResult(MergeStatus.NoMergeInProgress, "No merge in progress.", null, null);
                if (await HasConflictsAsync(cancellationToken).ConfigureAwait(false))
                    return new MergeResult(MergeStatus.Conflicts, "Resolve all conflicts before continuing.", null, null);

                var result = await Context.RunAsync(Context.Request("commit", "--no-edit"), cancellationToken).ConfigureAwait(false);
                var head = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    return new MergeResult(MergeStatus.Merged, $"Merge committed ({Short(head)}).", head, null);
                var error = result.ToError();
                return new MergeResult(MergeStatus.Failed, error.Message, head, error);
            }
        }

        /// <summary>Runs "git push --progress --porcelain &lt;arguments&gt;"; the caller holds the lock.</summary>
        internal async Task<PushResult> RunPushCoreAsync(IEnumerable<string> arguments, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
        {
            var request = Context.Request("push", "--progress", "--porcelain");
            request.Arguments.AddRange(arguments);
            request.Progress = progress;

            var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            var updates = PushPorcelainParser.Parse(result.StandardOutput);

            if (updates.Any(u => u.Status == PushRefStatus.Rejected))
            {
                var error = result.ToError();
                var rejected = string.Join(", ", updates.Where(u => u.Status == PushRefStatus.Rejected).Select(u => $"{u.Destination} {u.Summary}"));
                return new PushResult(PushStatus.Rejected, $"Rejected: {rejected}. Pull first, then push again.", updates,
                    new GitError(GitErrorKind.Rejected, error.Message, error.ExitCode, error.StandardError, error.CommandLine));
            }

            if (!result.Succeeded)
            {
                var error = result.ToError();
                return new PushResult(PushStatus.Failed, error.Message, updates, error);
            }

            return updates.Count == 0 || updates.All(u => u.Status == PushRefStatus.UpToDate)
                ? new PushResult(PushStatus.UpToDate, "Everything up-to-date.", updates, null)
                : new PushResult(PushStatus.Pushed, "Pushed " + string.Join(", ", updates.Where(u => u.Status != PushRefStatus.UpToDate).Select(u => u.Destination)) + ".", updates, null);
        }

        internal async Task<string> ResolvePushRemoteAsync(string? branch, CancellationToken cancellationToken)
        {
            if (branch != null)
            {
                var pushRemote = await Context.ReadConfigAsync($"branch.{branch}.pushRemote", cancellationToken).ConfigureAwait(false)
                                 ?? await Context.ReadConfigAsync($"branch.{branch}.remote", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(pushRemote) && pushRemote != ".")
                    return pushRemote!;
            }

            var pushDefault = await Context.ReadConfigAsync("remote.pushDefault", cancellationToken).ConfigureAwait(false);
            return string.IsNullOrEmpty(pushDefault) ? "origin" : pushDefault!;
        }

        private async Task<bool> HasConflictsAsync(CancellationToken cancellationToken)
        {
            var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
            return status.HasConflicts;
        }

        private async Task<int> CountParentsAsync(string? commit, CancellationToken cancellationToken)
        {
            if (commit == null)
                return 0;
            var request = Context.Request("rev-list", "--parents", "--max-count=1", commit);
            request.ReadOnly = true;
            var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim().Split(' ').Length - 1 : 0;
        }

        private async Task<bool> IsAncestorAsync(string ancestor, string? descendant, CancellationToken cancellationToken)
        {
            if (descendant == null)
                return false;
            var request = Context.Request("merge-base", "--is-ancestor", ancestor, descendant);
            request.ReadOnly = true;
            var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
    }
}
