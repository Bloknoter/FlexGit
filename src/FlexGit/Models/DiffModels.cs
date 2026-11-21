#nullable enable
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>How a file differs between HEAD and the working tree.</summary>
    public enum DiffChangeKind
    {
        /// <summary>No difference, or the path doesn't exist on either side.</summary>
        None,
        /// <summary>A new file: staged, or untracked (<see cref="FileDiff.IsUntracked"/>).</summary>
        Added,
        /// <summary>The file is in HEAD but was deleted from the working tree or the index.</summary>
        Deleted,
        Modified,
        /// <summary>Moved, possibly with edits; <see cref="FileDiff.OldPath"/> is the path in HEAD.</summary>
        Renamed
    }

    /// <summary>What one line of a <see cref="DiffHunk"/> is.</summary>
    public enum DiffLineKind
    {
        /// <summary>Context line, the same on both sides (" ").</summary>
        Unchanged,
        /// <summary>Only in the old version ("-").</summary>
        Removed,
        /// <summary>Only in the new version ("+").</summary>
        Added
    }

    /// <summary>One line of a <see cref="DiffHunk"/>.</summary>
    public sealed class DiffLine
    {
        internal DiffLine(DiffLineKind kind, string text, int? oldLineNumber, int? newLineNumber, bool noNewlineAtEnd)
        {
            Kind = kind;
            Text = text;
            OldLineNumber = oldLineNumber;
            NewLineNumber = newLineNumber;
            NoNewlineAtEnd = noNewlineAtEnd;
        }

        public DiffLineKind Kind { get; }

        /// <summary>The line's text without its line break (LF or CRLF), decoded as UTF-8.</summary>
        public string Text { get; }

        /// <summary>1-based line number in the old version; null for <see cref="DiffLineKind.Added"/> lines.</summary>
        public int? OldLineNumber { get; }

        /// <summary>1-based line number in the new version; null for <see cref="DiffLineKind.Removed"/> lines.</summary>
        public int? NewLineNumber { get; }

        /// <summary>
        /// The line is the last one of its version and has no line break after it (git's "\ No newline at end of file").
        /// Explains a removed/added pair whose text looks identical.
        /// </summary>
        public bool NoNewlineAtEnd { get; }

        public override string ToString()
        {
            var prefix = Kind == DiffLineKind.Added ? "+" : Kind == DiffLineKind.Removed ? "-" : " ";
            return prefix + Text;
        }
    }

    /// <summary>A block of changed lines with the unchanged lines around them (one <c>@@ ... @@</c> section).</summary>
    public sealed class DiffHunk
    {
        internal DiffHunk(int oldStart, int oldCount, int newStart, int newCount, string heading, IReadOnlyList<DiffLine> lines)
        {
            OldStart = oldStart;
            OldCount = oldCount;
            NewStart = newStart;
            NewCount = newCount;
            Heading = heading;
            Lines = lines;
        }

        /// <summary>First old line number of the hunk (0 when the old version has no lines here, e.g. a new file).</summary>
        public int OldStart { get; }

        /// <summary>Number of old lines in the hunk (unchanged + removed).</summary>
        public int OldCount { get; }

        /// <summary>First new line number of the hunk (0 when the new version has no lines here, e.g. a deleted file).</summary>
        public int NewStart { get; }

        /// <summary>Number of new lines in the hunk (unchanged + added).</summary>
        public int NewCount { get; }

        /// <summary>Text git shows after the second <c>@@</c>, usually the enclosing function or section; may be empty.</summary>
        public string Heading { get; }

        public IReadOnlyList<DiffLine> Lines { get; }

        public override string ToString() => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@";
    }

    /// <summary>
    /// The difference between a file in HEAD and in the working tree (staged and unstaged changes together), as
    /// <c>git diff HEAD</c> shows it. Returned by <see cref="DiffCommands.GetWorkingTreeDiffAsync"/>.
    /// </summary>
    public sealed class FileDiff
    {
        internal FileDiff(string path, string? oldPath, DiffChangeKind change, bool isUntracked, bool isBinary, bool isLfs,
            long? oldSize, long? newSize, IReadOnlyList<DiffHunk> hunks)
        {
            Path = path;
            OldPath = oldPath;
            Change = change;
            IsUntracked = isUntracked;
            IsBinary = isBinary;
            IsLfs = isLfs;
            OldSize = oldSize;
            NewSize = newSize;
            Hunks = hunks;
        }

        /// <summary>Path relative to the repository root, with '/' separators.</summary>
        public string Path { get; }

        /// <summary>The path in HEAD when the file was renamed (<see cref="DiffChangeKind.Renamed"/>), otherwise null.</summary>
        public string? OldPath { get; }

        public DiffChangeKind Change { get; }

        public bool HasChanges => Change != DiffChangeKind.None;

        /// <summary>The file is new and not staged. Its diff is every line added.</summary>
        public bool IsUntracked { get; }

        /// <summary>git considers the content binary, so there are no hunks: show <see cref="OldSize"/>/<see cref="NewSize"/> instead.</summary>
        public bool IsBinary { get; }

        /// <summary>
        /// The file is stored in Git LFS. There are no hunks, because git only compares LFS pointers.
        /// <see cref="OldSize"/> and <see cref="NewSize"/> are the sizes of the real content.
        /// </summary>
        public bool IsLfs { get; }

        /// <summary>
        /// Size in bytes of the HEAD version as git stores it; null when the file is not in HEAD. For an unchanged file
        /// this is the working-tree size, like <see cref="NewSize"/>.
        /// </summary>
        public long? OldSize { get; }

        /// <summary>Size in bytes of the working-tree file; null when it doesn't exist there.</summary>
        public long? NewSize { get; }

        /// <summary>Changed blocks in file order; empty for binary, LFS and unchanged files.</summary>
        public IReadOnlyList<DiffHunk> Hunks { get; }

        public override string ToString() => OldPath == null
            ? $"{Change} {Path} ({Hunks.Count} hunk(s))"
            : $"{Change} {OldPath} -> {Path} ({Hunks.Count} hunk(s))";
    }
}
