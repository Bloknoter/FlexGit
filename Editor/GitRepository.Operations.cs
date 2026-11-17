#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    public sealed partial class GitRepository
    {
        /// <summary>
        /// Commits the staged changes. Pass a null <paramref name="message"/> to reuse git's prepared message
        /// (e.g. to conclude a merge after resolving conflicts).
        /// </summary>
        public async Task<CommitResult> CommitAsync(string? message, CommitOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new CommitOptions();
            var request = Context.Request("commit");
            if (message != null)
            {
                request.Add("--file=-");
                request.StandardInput = GitArguments.Utf8(message);
            }
            else
            {
                request.Add("--no-edit");
            }
            request.AddIf(options.Amend, "--amend");
            request.AddIf(options.AllowEmpty, "--allow-empty");
            request.AddIf(options.AllowEmptyMessage, "--allow-empty-message");
            request.AddIf(options.StageTrackedChanges, "--all");
            request.AddIf(options.SkipHooks, "--no-verify");
            if (options.Author != null)
                request.Environment = IdentityEnvironment(options.Author);

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    var error = result.ToError();
                    return error.Kind == GitErrorKind.NothingToCommit
                        ? new CommitResult(CommitStatus.NothingToCommit, "Nothing to commit.", null, error)
                        : new CommitResult(CommitStatus.Failed, error.Message, null, error);
                }

                var sha = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                return new CommitResult(CommitStatus.Committed, $"Committed {Short(sha)}.", sha, null);
            }
        }

        /// <summary>Moves the current branch to <paramref name="revision"/> (<c>git reset</c>). <see cref="ResetMode.Hard"/> is destructive.</summary>
        public Task ResetAsync(string revision, ResetMode mode = ResetMode.Mixed, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            var modeArgument = mode switch
            {
                ResetMode.Soft => "--soft",
                ResetMode.Hard => "--hard",
                _ => "--mixed"
            };
            return Context.RunLockedCheckedAsync(Context.Request("reset", "--quiet", modeArgument, revision, "--"), cancellationToken);
        }

        internal static Dictionary<string, string?> IdentityEnvironment(UserIdentity identity) => new Dictionary<string, string?>
        {
            ["GIT_AUTHOR_NAME"] = identity.Name,
            ["GIT_AUTHOR_EMAIL"] = identity.Email,
            ["GIT_COMMITTER_NAME"] = identity.Name,
            ["GIT_COMMITTER_EMAIL"] = identity.Email
        };

        internal static string Short(string? sha) => sha == null ? "(none)" : sha.Length > 7 ? sha.Substring(0, 7) : sha;
    }
}
