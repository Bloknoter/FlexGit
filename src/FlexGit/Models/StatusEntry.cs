#nullable enable

namespace FlexGit
{
    /// <summary>State of a file in the index or the working tree (one side of git's "XY" status code).</summary>
    public enum FileStatus
    {
        Unmodified,
        Modified,
        TypeChanged,
        Added,
        Deleted,
        Renamed,
        Copied,
        /// <summary>Part of an unresolved merge conflict.</summary>
        Unmerged,
        /// <summary>Not tracked by git (working tree only).</summary>
        Untracked,
        /// <summary>Ignored by .gitignore (working tree only, reported when requested).</summary>
        Ignored
    }

    /// <summary>Which sides of a merge changed a conflicted file.</summary>
    public enum ConflictKind
    {
        None,
        /// <summary>DD: deleted on both sides.</summary>
        BothDeleted,
        /// <summary>AU: added by us.</summary>
        AddedByUs,
        /// <summary>UD: modified by us, deleted by them.</summary>
        DeletedByThem,
        /// <summary>UA: added by them.</summary>
        AddedByThem,
        /// <summary>DU: deleted by us, modified by them.</summary>
        DeletedByUs,
        /// <summary>AA: added on both sides.</summary>
        BothAdded,
        /// <summary>UU: modified on both sides.</summary>
        BothModified
    }

    /// <summary>One changed, untracked, ignored or conflicted path from <c>git status</c>.</summary>
    public sealed class StatusEntry
    {
        internal StatusEntry(string path, string? originalPath, FileStatus indexStatus, FileStatus workTreeStatus, ConflictKind conflict, bool isSubmodule)
        {
            Path = path;
            OriginalPath = originalPath;
            IndexStatus = indexStatus;
            WorkTreeStatus = workTreeStatus;
            Conflict = conflict;
            IsSubmodule = isSubmodule;
        }

        /// <summary>Path relative to the repository root, with '/' separators.</summary>
        public string Path { get; }

        /// <summary>Previous path for renames/copies, otherwise null.</summary>
        public string? OriginalPath { get; }

        /// <summary>Staged state (index vs HEAD).</summary>
        public FileStatus IndexStatus { get; }

        /// <summary>Unstaged state (working tree vs index).</summary>
        public FileStatus WorkTreeStatus { get; }

        /// <summary>Conflict type, or <see cref="ConflictKind.None"/>.</summary>
        public ConflictKind Conflict { get; }

        /// <summary>True when the path is a submodule.</summary>
        public bool IsSubmodule { get; }

        public bool IsConflicted => Conflict != ConflictKind.None;

        public bool IsUntracked => WorkTreeStatus == FileStatus.Untracked;

        public bool IsIgnored => WorkTreeStatus == FileStatus.Ignored;

        /// <summary>True when the entry has staged changes.</summary>
        public bool IsStaged => !IsConflicted && IndexStatus != FileStatus.Unmodified;

        /// <summary>True when the working tree differs from the index (excluding untracked/ignored files).</summary>
        public bool HasUnstagedChanges => !IsConflicted && WorkTreeStatus != FileStatus.Unmodified && !IsUntracked && !IsIgnored;

        public override string ToString() => OriginalPath == null
            ? $"{IndexStatus}/{WorkTreeStatus} {Path}"
            : $"{IndexStatus}/{WorkTreeStatus} {OriginalPath} -> {Path}";
    }
}
