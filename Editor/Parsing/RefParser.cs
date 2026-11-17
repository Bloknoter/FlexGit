#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FlexGit
{
    /// <summary>Parses <c>git for-each-ref</c> output produced with NUL-separated <c>--format</c> fields.</summary>
    internal static class RefParser
    {
        /// <summary>One line per ref: fields separated by NUL.</summary>
        public const string BranchFormat =
            "%(refname)%00%(refname:short)%00%(objectname)%00%(upstream:short)%00%(upstream:track,nobracket)%00" +
            "%(upstream:remotename)%00%(HEAD)%00%(symref)%00%(committerdate:iso-strict)%00%(subject)";

        public const string TagFormat =
            "%(refname)%00%(refname:short)%00%(objecttype)%00%(objectname)%00%(*objectname)%00" +
            "%(taggername)%00%(taggeremail)%00%(taggerdate:iso-strict)%00%(contents:subject)";

        private static readonly Regex s_ahead = new Regex(@"ahead (\d+)", RegexOptions.Compiled);
        private static readonly Regex s_behind = new Regex(@"behind (\d+)", RegexOptions.Compiled);

        public static List<BranchInfo> ParseBranches(string output, IReadOnlyCollection<string> remoteNames)
        {
            var branches = new List<BranchInfo>();
            foreach (var line in SplitLines(output))
            {
                var f = line.Split('\0');
                if (f.Length < 10)
                    continue;

                var canonical = f[0];
                var isRemote = canonical.StartsWith("refs/remotes/", StringComparison.Ordinal);

                // Skip symbolic refs such as refs/remotes/origin/HEAD.
                if (f[7].Length > 0)
                    continue;

                var branch = new BranchInfo
                {
                    CanonicalName = canonical,
                    Name = f[1],
                    TipSha = f[2],
                    Upstream = Empty(f[3]),
                    IsCurrent = f[6] == "*",
                    LastCommitDate = ParseDate(f[8]),
                    LastCommitSubject = f[9],
                    IsRemote = isRemote
                };

                var track = f[4];
                branch.IsUpstreamGone = track == "gone";
                branch.Ahead = ParseCount(s_ahead, track);
                branch.Behind = ParseCount(s_behind, track);

                if (isRemote)
                {
                    var withoutPrefix = canonical.Substring("refs/remotes/".Length);
                    var remote = MatchRemote(withoutPrefix, remoteNames);
                    branch.RemoteName = remote;
                    branch.FriendlyName = remote != null && withoutPrefix.Length > remote.Length + 1
                        ? withoutPrefix.Substring(remote.Length + 1)
                        : withoutPrefix;
                }
                else
                {
                    branch.RemoteName = Empty(f[5]);
                    branch.FriendlyName = canonical.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? canonical.Substring("refs/heads/".Length)
                        : f[1];
                }

                branches.Add(branch);
            }
            return branches;
        }

        public static List<TagInfo> ParseTags(string output)
        {
            var tags = new List<TagInfo>();
            foreach (var line in SplitLines(output))
            {
                var f = line.Split('\0');
                if (f.Length < 9)
                    continue;

                var isAnnotated = f[2] == "tag";
                var target = isAnnotated && f[4].Length > 0 ? f[4] : f[3];
                GitSignature? tagger = null;
                if (isAnnotated && f[5].Length > 0)
                    tagger = new GitSignature(f[5], f[6].Trim('<', '>'), ParseDate(f[7]) ?? DateTimeOffset.MinValue);

                tags.Add(new TagInfo(f[1], f[0], isAnnotated, target, tagger, isAnnotated ? f[8] : null));
            }
            return tags;
        }

        /// <summary>Parses <c>git config -z --get-regexp ^remote\..*\.(url|pushurl)$</c>.</summary>
        public static List<RemoteInfo> ParseRemotes(string output)
        {
            var fetch = new Dictionary<string, string>(StringComparer.Ordinal);
            var push = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var record in output.Split('\0'))
            {
                var newline = record.IndexOf('\n');
                if (newline < 0)
                    continue;
                var key = record.Substring(0, newline);
                var value = record.Substring(newline + 1);

                string? name = null;
                Dictionary<string, string>? target = null;
                if (key.EndsWith(".pushurl", StringComparison.OrdinalIgnoreCase))
                {
                    name = key.Substring("remote.".Length, key.Length - "remote.".Length - ".pushurl".Length);
                    target = push;
                }
                else if (key.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    name = key.Substring("remote.".Length, key.Length - "remote.".Length - ".url".Length);
                    target = fetch;
                }

                if (name == null || target == null || name.Length == 0)
                    continue;
                if (!order.Contains(name))
                    order.Add(name);
                if (!target.ContainsKey(name))
                    target[name] = value;
            }

            return order
                .Where(fetch.ContainsKey)
                .Select(n => new RemoteInfo(n, fetch[n], push.TryGetValue(n, out var p) ? p : fetch[n]))
                .ToList();
        }

        private static string? MatchRemote(string refWithoutPrefix, IReadOnlyCollection<string> remoteNames)
        {
            string? best = null;
            foreach (var remote in remoteNames)
            {
                if (refWithoutPrefix.StartsWith(remote + "/", StringComparison.Ordinal) && (best == null || remote.Length > best.Length))
                    best = remote;
            }

            if (best != null)
                return best;
            var slash = refWithoutPrefix.IndexOf('/');
            return slash > 0 ? refWithoutPrefix.Substring(0, slash) : null;
        }

        private static int ParseCount(Regex regex, string text)
        {
            var match = regex.Match(text);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        internal static DateTimeOffset? ParseDate(string text)
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTimeOffset?)null;
        }

        private static string? Empty(string value) => value.Length == 0 ? null : value;

        internal static IEnumerable<string> SplitLines(string output)
        {
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }
    }

    /// <summary>Parses <c>git log</c> output produced with <see cref="Format"/>.</summary>
    internal static class LogParser
    {
        private const char FieldSeparator = '\0';
        private const char RecordSeparator = '\u001e';

        public const string Format = "%H%x00%P%x00%an%x00%ae%x00%aI%x00%cn%x00%ce%x00%cI%x00%s%x00%b%x1e";

        public static List<CommitInfo> Parse(string output)
        {
            var commits = new List<CommitInfo>();
            foreach (var rawRecord in output.Split(RecordSeparator))
            {
                var record = rawRecord.TrimStart('\n', '\r');
                if (record.Length == 0)
                    continue;

                var f = record.Split(FieldSeparator);
                if (f.Length < 10)
                    continue;

                var parents = f[1].Length == 0 ? new string[0] : f[1].Split(' ');
                var author = new GitSignature(f[2], f[3], RefParser.ParseDate(f[4]) ?? DateTimeOffset.MinValue);
                var committer = new GitSignature(f[5], f[6], RefParser.ParseDate(f[7]) ?? DateTimeOffset.MinValue);
                commits.Add(new CommitInfo(f[0], parents, author, committer, f[8], f[9].TrimEnd('\n', '\r')));
            }
            return commits;
        }
    }
}
