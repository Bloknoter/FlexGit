#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Inspecting and resolving merge/rebase/stash conflicts. Access through <see cref="GitRepository.Conflicts"/>.
    /// Note: during a rebase "ours" is the branch being rebased onto and "theirs" is your commit being replayed.
    /// </summary>
    public sealed partial class ConflictCommands
    {
        private readonly GitRepository _repository;

        internal ConflictCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Lists conflicted paths.</summary>
        public async Task<IReadOnlyList<StatusEntry>> ListAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                return status.Conflicted.ToList();
            }
        }

        /// <summary>Resolves the given conflicts by taking our version (deleting the file where our side deleted it) and stages the result.</summary>
        public Task TakeOursAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
            => TakeSideAsync(paths, ours: true, cancellationToken);

        /// <summary>Resolves the given conflicts by taking their version (deleting the file where their side deleted it) and stages the result.</summary>
        public Task TakeTheirsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
            => TakeSideAsync(paths, ours: false, cancellationToken);

        /// <summary>
        /// Stages manually resolved files. With <paramref name="rejectIfConflictMarkers"/> (default), nothing is staged
        /// while any file still contains conflict markers.
        /// </summary>
        public async Task<ConflictResolutionResult> MarkResolvedAsync(IEnumerable<string> paths, bool rejectIfConflictMarkers = true,
            CancellationToken cancellationToken = default)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return new ConflictResolutionResult(ConflictResolutionStatus.Resolved, "Nothing to resolve.", new string[0], null);

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rejectIfConflictMarkers)
                {
                    var withMarkers = new List<string>();
                    foreach (var path in repositoryPaths)
                    {
                        if (await FileHasConflictMarkersAsync(_repository.GetFullPath(path), cancellationToken).ConfigureAwait(false))
                            withMarkers.Add(path);
                    }

                    if (withMarkers.Count > 0)
                        return new ConflictResolutionResult(ConflictResolutionStatus.ConflictMarkersFound,
                            $"{withMarkers.Count} file(s) still contain conflict markers.", withMarkers, null);
                }

                var request = Context.Request("add", "--all");
                GitContext.AddPathspecsViaStdin(request, repositoryPaths);
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    var error = result.ToError();
                    return new ConflictResolutionResult(ConflictResolutionStatus.Failed, error.Message, new string[0], error);
                }

                return new ConflictResolutionResult(ConflictResolutionStatus.Resolved, $"{repositoryPaths.Count} file(s) marked as resolved.", new string[0], null);
            }
        }

        /// <summary>Marks every conflicted path as resolved (see <see cref="MarkResolvedAsync"/>).</summary>
        public async Task<ConflictResolutionResult> MarkAllResolvedAsync(bool rejectIfConflictMarkers = true, CancellationToken cancellationToken = default)
        {
            var conflicts = await ListAsync(cancellationToken).ConfigureAwait(false);
            return await MarkResolvedAsync(conflicts.Select(c => c.Path), rejectIfConflictMarkers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>True when the file contains conflict markers (binary files are never reported).</summary>
        public Task<bool> HasConflictMarkersAsync(string path, CancellationToken cancellationToken = default)
            => FileHasConflictMarkersAsync(_repository.GetFullPath(Context.ToRepositoryPath(path)), cancellationToken);

        private async Task TakeSideAsync(IEnumerable<string> paths, bool ours, CancellationToken cancellationToken)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return;

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                var conflicts = status.Conflicted.ToDictionary(e => e.Path, StringComparer.Ordinal);

                var checkout = new List<string>();
                var remove = new List<string>();
                foreach (var path in repositoryPaths)
                {
                    if (!conflicts.TryGetValue(path, out var entry))
                        throw new GitCommandException(new GitError(GitErrorKind.NotFound, $"'{path}' is not conflicted."));
                    if (SideExists(entry.Conflict, ours))
                        checkout.Add(path);
                    else
                        remove.Add(path);
                }

                if (checkout.Count > 0)
                {
                    var request = Context.Request("checkout", ours ? "--ours" : "--theirs");
                    GitContext.AddPathspecsViaStdin(request, checkout);
                    await Context.RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);

                    var add = Context.Request("add", "--all");
                    GitContext.AddPathspecsViaStdin(add, checkout);
                    await Context.RunCheckedAsync(add, cancellationToken).ConfigureAwait(false);
                }

                if (remove.Count > 0)
                {
                    var rm = Context.Request("rm", "--quiet", "--force", "--");
                    rm.Arguments.AddRange(remove.Select(GitArguments.LiteralPathspec));
                    await Context.RunCheckedAsync(rm, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static bool SideExists(ConflictKind kind, bool ours)
        {
            if (kind == ConflictKind.BothDeleted)
                return false;
            if (ours)
                return kind != ConflictKind.DeletedByUs && kind != ConflictKind.AddedByThem;
            return kind != ConflictKind.DeletedByThem && kind != ConflictKind.AddedByUs;
        }

        private static async Task<bool> FileHasConflictMarkersAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(fullPath))
                return false;
            var bytes = await Task.Run(() => File.ReadAllBytes(fullPath), cancellationToken).ConfigureAwait(false);
            return ContainsConflictMarkers(bytes);
        }

        internal static bool ContainsConflictMarkers(byte[] content)
        {
            var probe = Math.Min(content.Length, 8000);
            for (var i = 0; i < probe; i++)
            {
                if (content[i] == 0)
                    return false; // binary
            }

            var text = Encoding.UTF8.GetString(content);
            bool start = false, separator = false, end = false;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
                    start = true;
                else if (line == "=======")
                    separator = true;
                else if (line.StartsWith(">>>>>>>", StringComparison.Ordinal))
                    end = true;
            }
            return start && separator && end;
        }
    }
}
