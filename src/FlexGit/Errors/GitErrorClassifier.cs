#nullable enable
using System;

namespace FlexGit
{
    /// <summary>
    /// Maps git output to a <see cref="GitErrorKind"/>. git runs with LC_ALL=C, so messages are English.
    /// Order matters: more specific patterns are checked first.
    /// </summary>
    internal static class GitErrorClassifier
    {
        private static readonly (GitErrorKind Kind, string[] Patterns)[] s_rules =
        {
            (GitErrorKind.UnsafeRepository, new[] { "dubious ownership" }),
            (GitErrorKind.NotARepository, new[] { "not a git repository" }),
            (GitErrorKind.IdentityNotConfigured, new[] { "Please tell me who you are", "unable to auto-detect email address", "empty ident name" }),
            (GitErrorKind.LfsNotInstalled, new[] { "'lfs' is not a git command", "git-lfs: command not found", "git-lfs was not found", "'git-lfs' was not found" }),
            (GitErrorKind.AuthenticationFailed, new[]
            {
                "Authentication failed", "could not read Username", "could not read Password", "terminal prompts disabled",
                "Permission denied (publickey", "Invalid username or password", "returned error: 401", "returned error: 403",
                "HTTP Basic: Access denied"
            }),
            (GitErrorKind.RemoteNotFound, new[]
            {
                "does not appear to be a git repository", "Repository not found", "No such remote", "returned error: 404"
            }),
            (GitErrorKind.NetworkError, new[]
            {
                "Could not resolve host", "Failed to connect", "Connection timed out", "Connection refused",
                "Couldn't connect to server", "Operation timed out", "The remote end hung up unexpectedly", "early EOF",
                "unable to access"
            }),
            (GitErrorKind.IndexLocked, new[] { "index.lock", "Another git process seems to be running" }),
            (GitErrorKind.LocalChangesWouldBeOverwritten, new[]
            {
                "would be overwritten by", "Please commit your changes or stash them", "You have unstaged changes",
                "Your index contains uncommitted changes", "cannot pull with rebase"
            }),
            (GitErrorKind.Conflicts, new[]
            {
                "CONFLICT (", "Automatic merge failed", "needs merge", "resolve your current index first", "unmerged files",
                "Resolve all conflicts manually", "could not apply", "you have unmerged paths"
            }),
            (GitErrorKind.Rejected, new[]
            {
                "[rejected]", "[remote rejected]", "non-fast-forward", "failed to push some refs", "Updates were rejected", "stale info"
            }),
            (GitErrorKind.CannotFastForward, new[] { "Not possible to fast-forward", "can't be fast-forwarded" }),
            (GitErrorKind.NoUpstream, new[] { "There is no tracking information", "has no upstream branch", "no upstream configured" }),
            (GitErrorKind.NoOperationInProgress, new[]
            {
                "There is no merge to abort", "There is no merge in progress", "MERGE_HEAD missing", "No rebase in progress"
            }),
            (GitErrorKind.NothingToCommit, new[] { "nothing to commit", "nothing added to commit", "no changes added to commit" }),
            (GitErrorKind.NotFullyMerged, new[] { "is not fully merged" }),
            (GitErrorKind.AlreadyExists, new[] { "already exists" }),
            (GitErrorKind.InvalidName, new[]
            {
                "is not a valid branch name", "is not a valid tag name", "not a valid ref name", "is not a valid remote name",
                "invalid branch name"
            }),
            (GitErrorKind.NotFound, new[]
            {
                "unknown revision", "invalid reference", "not a valid object name", "bad revision", "couldn't find remote ref",
                "not something we can merge", "did not match any", "Needed a single revision", "not found", "invalid upstream",
                "does not exist", "no such branch", "No stash entries found", "is not a valid reference",
                "log for 'stash' only has", "is not a stash-like commit"
            }),
        };

        public static GitErrorKind ClassifyText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return GitErrorKind.Unknown;

            foreach (var (kind, patterns) in s_rules)
            {
                foreach (var pattern in patterns)
                {
                    if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kind;
                }
            }

            return GitErrorKind.Unknown;
        }

        public static GitError Classify(GitResult result)
        {
            var combined = result.StandardError + "\n" + result.StandardOutput;
            var kind = ClassifyText(combined);
            return new GitError(kind, ExtractMessage(result), result.ExitCode, result.StandardError, result.CommandLine);
        }

        /// <summary>Picks the most useful line: the first "fatal:"/"error:" line, else the last non-empty line.</summary>
        internal static string ExtractMessage(GitResult result)
        {
            var fromStdErr = ExtractMessage(result.StandardError);
            if (fromStdErr.Length > 0)
                return fromStdErr;

            var fromStdOut = ExtractMessage(result.StandardOutput);
            if (fromStdOut.Length > 0)
                return fromStdOut;

            return $"git exited with code {result.ExitCode}";
        }

        private static string ExtractMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text.Replace("\r", "\n").Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("fatal:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
                    return line;
            }

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length > 0)
                    return line;
            }

            return string.Empty;
        }
    }
}
