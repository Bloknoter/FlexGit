#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Commit history and file contents. Access through <see cref="GitRepository.History"/>.</summary>
    public sealed class HistoryCommands
    {
        private readonly GitRepository _repository;

        internal HistoryCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Returns commits newest first (paged with <see cref="LogQuery.Skip"/>/<see cref="LogQuery.MaxCount"/>).</summary>
        public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(LogQuery? query = null, CancellationToken cancellationToken = default)
        {
            query ??= new LogQuery();
            if (query.MaxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(query), "MaxCount must be positive.");

            var request = Context.Request(
                "log",
                "--format=" + LogParser.Format,
                "--max-count=" + query.MaxCount.ToString(CultureInfo.InvariantCulture),
                "--skip=" + Math.Max(0, query.Skip).ToString(CultureInfo.InvariantCulture));
            request.ReadOnly = true;
            request.AddIf(query.FirstParentOnly, "--first-parent");

            if (query.AllRefs)
                request.Add("--all");
            else if (query.Revision != null)
                request.Add("--end-of-options").Add(GitArguments.EnsureSafeRef(query.Revision, nameof(query.Revision)));
            // else: git defaults to HEAD (and reports an unborn branch distinctly).

            request.Add("--");
            if (query.Path != null)
                request.Add(GitArguments.LiteralPathspec(Context.ToRepositoryPath(query.Path)));

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                // A branch without commits has no history: return an empty list instead of failing.
                if (query.Revision == null && !query.AllRefs &&
                    (result.StandardError.IndexOf("does not have any commits yet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     result.StandardError.IndexOf("bad revision 'HEAD'", StringComparison.OrdinalIgnoreCase) >= 0))
                    return new CommitInfo[0];
                result.EnsureSuccess();
            }

            return LogParser.Parse(result.StandardOutput);
        }

        /// <summary>Returns a single commit, or null when the revision doesn't exist.</summary>
        public async Task<CommitInfo?> GetCommitAsync(string revision, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            var request = Context.Request("log", "--max-count=1", "--format=" + LogParser.Format, "--end-of-options", revision, "--");
            request.ReadOnly = true;
            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var error = result.ToError();
                if (error.Kind == GitErrorKind.NotFound)
                    return null;
                throw new GitCommandException(error);
            }
            return LogParser.Parse(result.StandardOutput).FirstOrDefault();
        }

        /// <summary>
        /// Reads a file's content at a revision. With <paramref name="applyFilters"/> (default) git's filters run,
        /// so Git LFS files return their real content instead of the pointer, and line endings match a checkout.
        /// </summary>
        public async Task<byte[]> ReadFileAsync(string revision, string path, bool applyFilters = true, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            var objectName = revision + ":" + Context.ToRepositoryPath(path);
            var request = applyFilters
                ? Context.Request("cat-file", "--filters", objectName)
                : Context.Request("cat-file", "blob", objectName);
            request.ReadOnly = true;
            var result = await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return result.StandardOutputBytes;
        }
    }
}
