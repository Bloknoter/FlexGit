#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlexGit
{
    /// <summary>Parses <c>git status --porcelain=v2 --branch -z</c>.</summary>
    internal static class StatusParser
    {
        public static RepositoryStatus Parse(string output)
        {
            var status = new RepositoryStatus();
            var entries = new List<StatusEntry>();
            var records = output.Split('\0');

            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record.Length < 2)
                    continue;

                switch (record[0])
                {
                    case '#':
                        ParseHeader(record, status);
                        break;

                    case '1':
                    {
                        // 1 XY sub mH mI mW hH hI path
                        var fields = record.Split(new[] { ' ' }, 9);
                        if (fields.Length < 9)
                            break;
                        entries.Add(CreateEntry(fields[1], fields[2], fields[8], null));
                        break;
                    }

                    case '2':
                    {
                        // 2 XY sub mH mI mW hH hI Xscore path NUL origPath
                        var fields = record.Split(new[] { ' ' }, 10);
                        if (fields.Length < 10)
                            break;
                        var originalPath = i + 1 < records.Length ? records[++i] : null;
                        entries.Add(CreateEntry(fields[1], fields[2], fields[9], originalPath));
                        break;
                    }

                    case 'u':
                    {
                        // u XY sub m1 m2 m3 mW h1 h2 h3 path
                        var fields = record.Split(new[] { ' ' }, 11);
                        if (fields.Length < 11)
                            break;
                        var xy = fields[1];
                        entries.Add(new StatusEntry(fields[10], null, ParseCode(xy[0]), ParseCode(xy[1]), ParseConflict(xy), IsSubmodule(fields[2])));
                        break;
                    }

                    case '?':
                        entries.Add(new StatusEntry(record.Substring(2), null, FileStatus.Unmodified, FileStatus.Untracked, ConflictKind.None, false));
                        break;

                    case '!':
                        entries.Add(new StatusEntry(record.Substring(2), null, FileStatus.Unmodified, FileStatus.Ignored, ConflictKind.None, false));
                        break;
                }
            }

            status.Entries = entries;
            return status;
        }

        private static void ParseHeader(string record, RepositoryStatus status)
        {
            // "# branch.oid <sha>|(initial)", "# branch.head <name>|(detached)", "# branch.upstream <name>", "# branch.ab +A -B"
            var parts = record.Split(new[] { ' ' }, 3);
            if (parts.Length < 3)
                return;

            var value = parts[2];
            switch (parts[1])
            {
                case "branch.oid":
                    status.HeadSha = value == "(initial)" ? null : value;
                    break;
                case "branch.head":
                    if (value == "(detached)")
                    {
                        status.IsDetachedHead = true;
                        status.Branch = null;
                    }
                    else
                    {
                        status.Branch = value;
                    }
                    break;
                case "branch.upstream":
                    status.Upstream = value;
                    break;
                case "branch.ab":
                    var counts = value.Split(' ');
                    if (counts.Length == 2)
                    {
                        status.Ahead = ParseSigned(counts[0]);
                        status.Behind = ParseSigned(counts[1]);
                    }
                    break;
            }
        }

        private static int ParseSigned(string value)
        {
            var trimmed = value.TrimStart('+', '-');
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }

        private static StatusEntry CreateEntry(string xy, string submodule, string path, string? originalPath)
        {
            return new StatusEntry(path, originalPath, ParseCode(xy[0]), ParseCode(xy[1]), ConflictKind.None, IsSubmodule(submodule));
        }

        private static bool IsSubmodule(string field) => field.Length > 0 && field[0] == 'S';

        internal static FileStatus ParseCode(char code)
        {
            switch (code)
            {
                case 'M': return FileStatus.Modified;
                case 'T': return FileStatus.TypeChanged;
                case 'A': return FileStatus.Added;
                case 'D': return FileStatus.Deleted;
                case 'R': return FileStatus.Renamed;
                case 'C': return FileStatus.Copied;
                case 'U': return FileStatus.Unmerged;
                default: return FileStatus.Unmodified;
            }
        }

        internal static ConflictKind ParseConflict(string xy)
        {
            switch (xy)
            {
                case "DD": return ConflictKind.BothDeleted;
                case "AU": return ConflictKind.AddedByUs;
                case "UD": return ConflictKind.DeletedByThem;
                case "UA": return ConflictKind.AddedByThem;
                case "DU": return ConflictKind.DeletedByUs;
                case "AA": return ConflictKind.BothAdded;
                case "UU": return ConflictKind.BothModified;
                default: return ConflictKind.BothModified;
            }
        }
    }

    /// <summary>Detects an in-progress merge/rebase/... from marker files in the git directory.</summary>
    internal static class RepositoryStateDetector
    {
        public static RepositoryState Detect(string gitDirectory)
        {
            bool Exists(string name) =>
                System.IO.File.Exists(System.IO.Path.Combine(gitDirectory, name)) ||
                System.IO.Directory.Exists(System.IO.Path.Combine(gitDirectory, name));

            try
            {
                if (Exists("rebase-merge") || Exists("rebase-apply"))
                    return RepositoryState.Rebasing;
                if (Exists("MERGE_HEAD"))
                    return RepositoryState.Merging;
                if (Exists("CHERRY_PICK_HEAD"))
                    return RepositoryState.CherryPicking;
                if (Exists("REVERT_HEAD"))
                    return RepositoryState.Reverting;
                if (Exists("BISECT_LOG"))
                    return RepositoryState.Bisecting;
            }
            catch (Exception)
            {
                // Inaccessible git dir: report no operation rather than failing a status call.
            }

            return RepositoryState.None;
        }
    }
}
