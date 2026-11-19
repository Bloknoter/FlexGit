#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    // Conflict hunks: reading the conflict markers git wrote into working-tree files and resolving them hunk by hunk.
    public sealed partial class ConflictCommands
    {
        /// <summary>
        /// Returns the current conflict state: the operation that stopped and a summary of every unmerged path
        /// (kind, binary or text, number of hunks). File content is not kept; use <see cref="GetFileAsync"/> for one file.
        /// </summary>
        public async Task<ConflictOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                var conflicted = status.Conflicted.ToList();
                var markerSizes = await ReadMarkerSizesAsync(conflicted.Where(e => !e.IsSubmodule).Select(e => e.Path).ToList(), cancellationToken).ConfigureAwait(false);

                var files = new List<ConflictedFileInfo>(conflicted.Count);
                foreach (var entry in conflicted)
                {
                    var file = await LoadFileAsync(entry.Path, entry.Conflict, entry.IsSubmodule, MarkerSizeOf(markerSizes, entry.Path), cancellationToken).ConfigureAwait(false);
                    files.Add(new ConflictedFileInfo(file));
                }
                return new ConflictOverview(status.State, files);
            }
        }

        /// <summary>
        /// Reads one file's conflict hunks and the text around them from the working tree. Works for any path:
        /// <see cref="ConflictedFile.Kind"/> is <see cref="ConflictKind.None"/> when git doesn't consider it unmerged.
        /// The <c>conflict-marker-size</c> attribute is respected.
        /// </summary>
        public async Task<ConflictedFile> GetFileAsync(string path, CancellationToken cancellationToken = default)
        {
            var repositoryPath = Context.ToRepositoryPath(path);
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(
                    new StatusOptions { UntrackedFiles = UntrackedFilesMode.No, Paths = new[] { repositoryPath } }, cancellationToken).ConfigureAwait(false);
                var entry = status.Conflicted.FirstOrDefault(e => string.Equals(e.Path, repositoryPath, StringComparison.Ordinal));
                var isSubmodule = entry?.IsSubmodule ?? false;
                var markerSizes = isSubmodule
                    ? new Dictionary<string, int>()
                    : await ReadMarkerSizesAsync(new[] { repositoryPath }, cancellationToken).ConfigureAwait(false);

                return await LoadFileAsync(repositoryPath, entry?.Conflict ?? ConflictKind.None, isSubmodule,
                    MarkerSizeOf(markerSizes, repositoryPath), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Resolves hunks of <paramref name="file"/> (a snapshot from <see cref="GetFileAsync"/>) each its own way and
        /// writes the file. Hunks not listed keep their markers. When no hunks remain and
        /// <paramref name="stageWhenResolved"/> is set, the file is staged (marked resolved).
        /// </summary>
        /// <remarks>
        /// Nothing is written when the file changed since it was read (<see cref="ConflictResolutionStatus.FileChanged"/>):
        /// read it again and re-apply. Choosing <see cref="ConflictChoice.Ours"/> for every hunk keeps the other side's
        /// changes that merged cleanly; <see cref="TakeOursAsync"/> instead takes our whole file.
        /// </remarks>
        /// <exception cref="ArgumentException">No resolutions, a hunk resolved twice, or the file has no hunks.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A hunk index doesn't exist.</exception>
        public async Task<ConflictResolutionResult> ResolveAsync(ConflictedFile file, IEnumerable<ConflictHunkResolution> resolutions, bool stageWhenResolved = true,
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (resolutions == null)
                throw new ArgumentNullException(nameof(resolutions));
            var list = resolutions.ToList();
            if (list.Count == 0)
                throw new ArgumentException("At least one hunk resolution is required.", nameof(resolutions));
            var document = file.Document;
            if (document == null || document.Hunks.Count == 0)
                throw new ArgumentException(
                    $"'{file.Path}' has no conflict hunks. Use TakeOursAsync/TakeTheirsAsync, or MarkResolvedAsync after editing it.", nameof(file));

            var composed = ConflictMarkerParser.Compose(document, list);
            var fullPath = _repository.GetFullPath(file.Path);
            var paths = new[] { file.Path };

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var current = await ReadFileOrNullAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (current == null || !current.AsSpan().SequenceEqual(document.Content))
                    return new ConflictResolutionResult(ConflictResolutionStatus.FileChanged,
                        $"'{file.Path}' changed after it was read; nothing was written. Read it again.", paths, null, document.Hunks.Count);

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => File.WriteAllBytes(fullPath, composed.Content), CancellationToken.None).ConfigureAwait(false);

                if (composed.RemainingHunks > 0)
                    return new ConflictResolutionResult(ConflictResolutionStatus.PartiallyResolved,
                        $"{list.Count} hunk(s) resolved in '{file.Path}', {composed.RemainingHunks} remaining.", paths, null, composed.RemainingHunks);

                if (!stageWhenResolved)
                    return new ConflictResolutionResult(ConflictResolutionStatus.Resolved, $"'{file.Path}' resolved (not staged).", new string[0], null);

                var add = Context.Request("add", "--all");
                GitContext.AddPathspecsViaStdin(add, paths);
                var result = await Context.RunAsync(add, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    var error = result.ToError();
                    return new ConflictResolutionResult(ConflictResolutionStatus.Failed,
                        $"'{file.Path}' was written but could not be staged: {error.Message}", new string[0], error);
                }

                return new ConflictResolutionResult(ConflictResolutionStatus.Resolved, $"'{file.Path}' resolved and staged.", new string[0], null);
            }
        }

        /// <summary>Resolves every hunk of <paramref name="file"/> with the same <paramref name="choice"/> (see the other overload).</summary>
        /// <exception cref="ArgumentException"><paramref name="choice"/> is <see cref="ConflictChoice.Custom"/>, or the file has no hunks.</exception>
        public Task<ConflictResolutionResult> ResolveAsync(ConflictedFile file, ConflictChoice choice, bool stageWhenResolved = true,
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (choice == ConflictChoice.Custom)
                throw new ArgumentException("Custom text needs a hunk; use ConflictHunkResolution.Custom.", nameof(choice));
            var resolutions = file.Hunks.Select(h => new ConflictHunkResolution(h.Index, choice)).ToList();
            if (resolutions.Count == 0)
                throw new ArgumentException(
                    $"'{file.Path}' has no conflict hunks. Use TakeOursAsync/TakeTheirsAsync, or MarkResolvedAsync after editing it.", nameof(file));
            return ResolveAsync(file, resolutions, stageWhenResolved, cancellationToken);
        }

        /// <summary>
        /// Rewrites the conflicted files from git's index (<c>git checkout --merge</c>), restoring every hunk with
        /// fresh markers and <b>discarding edits</b> made to those files since the conflict. Works only for paths that
        /// are still unmerged and that both sides changed.
        /// </summary>
        /// <param name="style">Marker style; <see cref="ConflictMarkerStyle.Diff3"/> adds the common ancestor (<see cref="ConflictHunk.Base"/>).</param>
        /// <exception cref="GitCommandException">A path is not unmerged (<see cref="GitErrorKind.NotFound"/>), or git failed.</exception>
        public async Task RestoreConflictAsync(IEnumerable<string> paths, ConflictMarkerStyle style = ConflictMarkerStyle.Default,
            CancellationToken cancellationToken = default)
        {
            var repositoryPaths = Context.ToRepositoryPaths(paths);
            if (repositoryPaths.Count == 0)
                return;

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                var conflicted = new HashSet<string>(status.Conflicted.Select(e => e.Path), StringComparer.Ordinal);
                foreach (var path in repositoryPaths)
                {
                    if (!conflicted.Contains(path))
                        throw new GitCommandException(new GitError(GitErrorKind.NotFound, $"'{path}' is not conflicted."));
                }

                var request = Context.Request("checkout", style switch
                {
                    ConflictMarkerStyle.Merge => "--conflict=merge",
                    ConflictMarkerStyle.Diff3 => "--conflict=diff3",
                    _ => "--merge"
                });
                GitContext.AddPathspecsViaStdin(request, repositoryPaths);
                await Context.RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Reads and parses one working-tree file; the caller holds the lock.</summary>
        private async Task<ConflictedFile> LoadFileAsync(string repositoryPath, ConflictKind kind, bool isSubmodule, int markerSize,
            CancellationToken cancellationToken)
        {
            var content = isSubmodule ? null : await ReadFileOrNullAsync(_repository.GetFullPath(repositoryPath), cancellationToken).ConfigureAwait(false);
            if (content == null)
                return new ConflictedFile(repositoryPath, kind, isSubmodule, false, false, null);
            if (ConflictMarkerParser.IsBinary(content))
                return new ConflictedFile(repositoryPath, kind, false, true, true, null);
            return new ConflictedFile(repositoryPath, kind, false, true, false, ConflictMarkerParser.Parse(content, markerSize));
        }

        /// <summary>Reads <c>conflict-marker-size</c> for the paths with one <c>git check-attr</c>; the caller holds the lock.</summary>
        private async Task<IReadOnlyDictionary<string, int>> ReadMarkerSizesAsync(IReadOnlyCollection<string> repositoryPaths, CancellationToken cancellationToken)
        {
            if (repositoryPaths.Count == 0)
                return new Dictionary<string, int>();

            // check-attr takes plain paths (not pathspecs); --stdin keeps them off the command line.
            var request = Context.Request("check-attr", "-z", "--stdin", "conflict-marker-size");
            request.ReadOnly = true;
            request.StandardInput = GitArguments.NulSeparatedPaths(repositoryPaths);
            var result = await Context.RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return ConflictMarkerParser.ParseMarkerSizes(result.StandardOutput);
        }

        private static int MarkerSizeOf(IReadOnlyDictionary<string, int> sizes, string repositoryPath) =>
            sizes.TryGetValue(repositoryPath, out var size) ? size : ConflictMarkerParser.DefaultMarkerSize;

        private static async Task<byte[]?> ReadFileOrNullAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(fullPath))
                return null;
            try
            {
                return await Task.Run(() => File.ReadAllBytes(fullPath), cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }
}
