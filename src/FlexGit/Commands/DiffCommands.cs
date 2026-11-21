#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Line-by-line differences of changed files. Access through <see cref="GitRepository.Diff"/>.</summary>
    public sealed class DiffCommands
    {
        /// <summary>LFS pointers are under 1024 bytes (a few lines), so this much context shows both pointers whole.</summary>
        private const int LfsPointerContextLines = 32;

        /// <summary>git's default <c>core.bigFileThreshold</c>: larger files are treated as binary.</summary>
        private const long BigFileThreshold = 512L * 1024 * 1024;

        /// <summary>git's binary heuristic looks for a NUL byte in the first 8000 bytes.</summary>
        private const int BinaryProbeLength = 8000;

        private readonly GitRepository _repository;

        internal DiffCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>
        /// Compares one file in HEAD with the working tree, staged and unstaged changes together (what <c>git diff HEAD</c>
        /// shows). New files, staged or untracked, are <see cref="DiffChangeKind.Added"/> with every line added. Deleted files
        /// are <see cref="DiffChangeKind.Deleted"/> with every old line removed.
        /// </summary>
        /// <remarks>
        /// Runs <c>git diff-index -p</c>, so line-ending conversion (<c>core.autocrlf</c>, <c>text</c>/<c>eol</c> attributes)
        /// and binary detection are git's own. User settings such as colours, external diff tools and prefixes don't
        /// affect it. Git LFS files have no hunks: see <see cref="FileDiff.IsLfs"/>. A path that doesn't exist on either
        /// side returns <see cref="DiffChangeKind.None"/>.
        /// </remarks>
        /// <param name="path">The file, absolute or relative to the repository root.</param>
        /// <param name="contextLines">Unchanged lines shown around each change (git's default is 3).</param>
        /// <param name="originalPath">
        /// For a staged rename, the path in HEAD (<see cref="StatusEntry.OriginalPath"/>). git detects a rename only when it
        /// sees both paths. Without it, a renamed file shows as <see cref="DiffChangeKind.Added"/>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="contextLines"/> is negative.</exception>
        /// <exception cref="GitCommandException">git failed.</exception>
        public async Task<FileDiff> GetWorkingTreeDiffAsync(string path, int contextLines = 3, string? originalPath = null,
            CancellationToken cancellationToken = default)
        {
            if (contextLines < 0)
                throw new ArgumentOutOfRangeException(nameof(contextLines), "Context lines cannot be negative.");
            var repositoryPath = Context.ToRepositoryPath(path);
            var repositoryOriginalPath = string.IsNullOrWhiteSpace(originalPath) ? null : Context.ToRepositoryPath(originalPath!);
            var pathspecs = new List<string> { repositoryPath };
            if (repositoryOriginalPath != null && !string.Equals(repositoryOriginalPath, repositoryPath, StringComparison.Ordinal))
                pathspecs.Add(repositoryOriginalPath);
            var fullPath = _repository.GetFullPath(repositoryPath);

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var attributes = await ReadAttributesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
                var isLfs = attributes.TryGetValue("filter", out var filter) && filter == "lfs";

                var sections = await RunDiffIndexAsync(pathspecs, isLfs ? Math.Max(contextLines, LfsPointerContextLines) : contextLines,
                    cancellationToken).ConfigureAwait(false);
                var section = sections.FirstOrDefault(s => string.Equals(s.NewPath, repositoryPath, StringComparison.Ordinal));
                var newSize = FileLengthOrNull(fullPath);
                if (section == null)
                    return await DiffUnchangedOrUntrackedAsync(repositoryPath, fullPath, newSize, isLfs, attributes, cancellationToken).ConfigureAwait(false);

                var hunks = section.Hunks;
                long? oldSize = null;
                if (isLfs)
                {
                    // git compared pointers: take the real size from the old pointer and hide the pointer lines.
                    oldSize = DiffParser.ParseLfsPointerSize(hunks.SelectMany(h => h.Lines).Where(l => l.Kind != DiffLineKind.Added).Select(l => l.Text));
                    hunks = new DiffHunk[0];
                }
                if (oldSize == null && section.OldObjectId != null)
                    oldSize = await ReadObjectSizeAsync(section.OldObjectId, cancellationToken).ConfigureAwait(false);
                if (section.Change == DiffChangeKind.Deleted)
                    newSize = null; // also when only the index entry was removed and the file is still on disk (untracked)

                return new FileDiff(repositoryPath, section.Change == DiffChangeKind.Renamed ? section.OldPath : null, section.Change,
                    false, section.IsBinary, isLfs, oldSize, newSize, hunks);
            }
        }

        /// <summary>No diff from git: the file is unchanged, untracked (git doesn't diff those) or missing. Caller holds the lock.</summary>
        private async Task<FileDiff> DiffUnchangedOrUntrackedAsync(string repositoryPath, string fullPath, long? size, bool isLfs,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            var none = new DiffHunk[0];
            if (size == null)
                return new FileDiff(repositoryPath, null, DiffChangeKind.None, false, false, isLfs, null, null, none);

            var tracked = Context.Request("ls-files", "-z", "--cached", "--", GitArguments.LiteralPathspec(repositoryPath));
            tracked.ReadOnly = true;
            if ((await Context.RunCheckedAsync(tracked, cancellationToken).ConfigureAwait(false)).StandardOutputBytes.Length > 0)
                return new FileDiff(repositoryPath, null, DiffChangeKind.None, false, false, isLfs, size, size, none);

            if (isLfs)
                return new FileDiff(repositoryPath, null, DiffChangeKind.Added, true, false, true, null, size, none);

            // git's rules: the binary attribute (-diff), a NUL byte near the start, or a file above core.bigFileThreshold.
            var isBinary = (attributes.TryGetValue("diff", out var diff) && diff == "unset") || size > BigFileThreshold;
            var content = isBinary ? null : await ReadTextOrNullAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return content == null
                ? new FileDiff(repositoryPath, null, DiffChangeKind.Added, true, true, false, null, size, none)
                : new FileDiff(repositoryPath, null, DiffChangeKind.Added, true, false, false, null, size, DiffParser.CreateAddedHunks(content));
        }

        /// <summary>Runs diff-index against HEAD, or against the empty tree on a branch without commits. Caller holds the lock.</summary>
        private async Task<List<DiffSection>> RunDiffIndexAsync(IReadOnlyList<string> repositoryPaths, int contextLines, CancellationToken cancellationToken)
        {
            var result = await Context.RunAsync(DiffIndexRequest("HEAD", repositoryPaths, contextLines), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded && await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false) == null)
            {
                var emptyTree = Context.Request("hash-object", "-t", "tree", "--stdin");
                emptyTree.ReadOnly = true;
                emptyTree.StandardInput = new byte[0];
                var treeId = (await Context.RunCheckedAsync(emptyTree, cancellationToken).ConfigureAwait(false)).StandardOutput.Trim();
                result = await Context.RunAsync(DiffIndexRequest(treeId, repositoryPaths, contextLines), cancellationToken).ConfigureAwait(false);
            }
            result.EnsureSuccess();
            return DiffParser.Parse(result.StandardOutputBytes);
        }

        private GitRunRequest DiffIndexRequest(string tree, IReadOnlyList<string> repositoryPaths, int contextLines)
        {
            // Plumbing ignores diff.noprefix, diff.external, color.diff and similar user settings; the flags pin the rest.
            var request = Context.Request("diff-index", "--patch", "--full-index", "--find-renames", "--no-color", "--no-ext-diff",
                "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", "--unified=" + contextLines.ToString(CultureInfo.InvariantCulture),
                tree, "--");
            request.ReadOnly = true;
            request.Arguments.AddRange(repositoryPaths.Select(GitArguments.LiteralPathspec));
            return request;
        }

        /// <summary>Reads the <c>filter</c> and <c>diff</c> attributes of a path. Caller holds the lock.</summary>
        private async Task<IReadOnlyDictionary<string, string>> ReadAttributesAsync(string repositoryPath, CancellationToken cancellationToken)
        {
            var request = Context.Request("check-attr", "-z", "--stdin", "filter", "diff");
            request.ReadOnly = true;
            request.StandardInput = GitArguments.NulSeparatedPaths(new[] { repositoryPath });
            var result = await Context.RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return DiffParser.ParseAttributes(result.StandardOutput, repositoryPath);
        }

        /// <summary>Size of a blob, or null if git can't read it (the size is informational). Caller holds the lock.</summary>
        private async Task<long?> ReadObjectSizeAsync(string objectId, CancellationToken cancellationToken)
        {
            var request = Context.Request("cat-file", "-s", objectId);
            request.ReadOnly = true;
            var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded && long.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                ? size
                : (long?)null;
        }

        private static long? FileLengthOrNull(string fullPath)
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? info.Length : (long?)null;
        }

        /// <summary>Reads the file, or returns null when it is binary by git's NUL-byte check (only that part is read then).</summary>
        private static Task<byte[]?> ReadTextOrNullAsync(string fullPath, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var content = new byte[stream.Length];
                var probe = ReadUpTo(stream, content, 0, Math.Min(content.Length, BinaryProbeLength));
                if (Array.IndexOf(content, (byte)0, 0, probe) >= 0)
                    return null;
                var length = probe + ReadUpTo(stream, content, probe, content.Length - probe);
                if (length < content.Length)
                    Array.Resize(ref content, length); // the file shrank while it was read
                return (byte[]?)content;
            }, cancellationToken);
        }

        private static int ReadUpTo(Stream stream, byte[] buffer, int offset, int count)
        {
            var total = 0;
            int read;
            while (total < count && (read = stream.Read(buffer, offset + total, count - total)) > 0)
                total += read;
            return total;
        }
    }
}
