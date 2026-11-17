#nullable enable
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>How <c>git status</c> reports untracked files.</summary>
    public enum UntrackedFilesMode
    {
        /// <summary>Don't report untracked files (fastest).</summary>
        No,
        /// <summary>Report untracked directories as a single entry.</summary>
        Normal,
        /// <summary>Report every untracked file individually (needed for staging UIs).</summary>
        All
    }

    /// <summary>Options for <see cref="GitRepository.GetStatusAsync"/>.</summary>
    public sealed class StatusOptions
    {
        public UntrackedFilesMode UntrackedFiles { get; set; } = UntrackedFilesMode.All;

        /// <summary>Also report ignored files.</summary>
        public bool IncludeIgnored { get; set; }

        /// <summary>Limit the status to these paths (repository-relative or absolute). Null = whole repository.</summary>
        public IReadOnlyList<string>? Paths { get; set; }
    }

    /// <summary>Which branches to list.</summary>
    public enum BranchScope
    {
        Local,
        Remote,
        All
    }

    /// <summary>Configuration file to read or write.</summary>
    public enum ConfigScope
    {
        /// <summary>The repository's .git/config.</summary>
        Local,
        /// <summary>The user's ~/.gitconfig.</summary>
        Global,
        /// <summary>The installation-wide config.</summary>
        System
    }

    /// <summary>Selects commits for <see cref="HistoryCommands.GetLogAsync"/>.</summary>
    public sealed class LogQuery
    {
        /// <summary>Revision to start from (branch, tag, sha, "origin/main", ...). Null = HEAD.</summary>
        public string? Revision { get; set; }

        /// <summary>Walk all refs instead of <see cref="Revision"/>.</summary>
        public bool AllRefs { get; set; }

        /// <summary>Only commits touching this path (repository-relative or absolute).</summary>
        public string? Path { get; set; }

        /// <summary>Number of newest commits to skip (paging).</summary>
        public int Skip { get; set; }

        /// <summary>Maximum number of commits to return.</summary>
        public int MaxCount { get; set; } = 100;

        /// <summary>Follow only the first parent of merge commits.</summary>
        public bool FirstParentOnly { get; set; }
    }
}
