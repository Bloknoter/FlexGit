#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace FlexGit
{
    /// <summary>An operation the repository is in the middle of.</summary>
    public enum RepositoryState
    {
        None,
        Merging,
        Rebasing,
        CherryPicking,
        Reverting,
        Bisecting
    }

    /// <summary>Snapshot of <c>git status</c>: branch information plus changed paths.</summary>
    public sealed class RepositoryStatus
    {
        internal RepositoryStatus()
        {
        }

        /// <summary>Current branch name, or null when HEAD is detached.</summary>
        public string? Branch { get; internal set; }

        /// <summary>Commit HEAD points to, or null in a repository without commits.</summary>
        public string? HeadSha { get; internal set; }

        /// <summary>True when HEAD points to a commit instead of a branch.</summary>
        public bool IsDetachedHead { get; internal set; }

        /// <summary>True when the current branch has no commits yet.</summary>
        public bool IsUnborn => HeadSha == null;

        /// <summary>Upstream branch, e.g. "origin/main", or null.</summary>
        public string? Upstream { get; internal set; }

        /// <summary>Commits on the current branch that are not on the upstream.</summary>
        public int Ahead { get; internal set; }

        /// <summary>Commits on the upstream that are not on the current branch.</summary>
        public int Behind { get; internal set; }

        /// <summary>Merge/rebase/... in progress.</summary>
        public RepositoryState State { get; internal set; }

        /// <summary>All reported paths.</summary>
        public IReadOnlyList<StatusEntry> Entries { get; internal set; } = new StatusEntry[0];

        public bool HasUpstream => Upstream != null;

        public bool HasConflicts => Entries.Any(e => e.IsConflicted);

        /// <summary>True when there are no staged, unstaged, untracked or conflicted paths.</summary>
        public bool IsClean => Entries.All(e => e.IsIgnored);

        public IEnumerable<StatusEntry> Staged => Entries.Where(e => e.IsStaged);

        public IEnumerable<StatusEntry> Unstaged => Entries.Where(e => e.HasUnstagedChanges);

        public IEnumerable<StatusEntry> Untracked => Entries.Where(e => e.IsUntracked);

        public IEnumerable<StatusEntry> Conflicted => Entries.Where(e => e.IsConflicted);

        public override string ToString() =>
            $"{Branch ?? "(detached)"} [{State}] ahead {Ahead}, behind {Behind}, {Entries.Count} entries";
    }
}
