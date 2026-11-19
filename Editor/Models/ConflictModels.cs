#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlexGit
{
    /// <summary>How to resolve one conflict hunk.</summary>
    public enum ConflictChoice
    {
        /// <summary>Keep our side ("Use A").</summary>
        Ours,
        /// <summary>Keep their side ("Use B").</summary>
        Theirs,
        /// <summary>Keep both: our lines first, then theirs ("Use A then B").</summary>
        OursThenTheirs,
        /// <summary>Keep both: their lines first, then ours ("Use B then A").</summary>
        TheirsThenOurs,
        /// <summary>Replace the hunk with caller-provided text (<see cref="ConflictHunkResolution.Custom"/>).</summary>
        Custom
    }

    /// <summary>Which conflict-marker style <see cref="ConflictCommands.RestoreConflictAsync"/> writes.</summary>
    public enum ConflictMarkerStyle
    {
        /// <summary>git's configured style (<c>merge.conflictStyle</c>).</summary>
        Default,
        /// <summary>Ours and theirs only.</summary>
        Merge,
        /// <summary>Ours, the common ancestor (<see cref="ConflictHunk.Base"/>) and theirs.</summary>
        Diff3
    }

    /// <summary>The resolution of one hunk of a <see cref="ConflictedFile"/>.</summary>
    public sealed class ConflictHunkResolution
    {
        /// <summary>Resolves hunk <paramref name="hunkIndex"/> with one of the sides (anything but <see cref="ConflictChoice.Custom"/>).</summary>
        public ConflictHunkResolution(int hunkIndex, ConflictChoice choice)
            : this(hunkIndex, choice, null)
        {
            if (choice == ConflictChoice.Custom)
                throw new ArgumentException("Use ConflictHunkResolution.Custom(index, text) for custom text.", nameof(choice));
        }

        private ConflictHunkResolution(int hunkIndex, ConflictChoice choice, string? customText)
        {
            if (hunkIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(hunkIndex), "Hunk index cannot be negative.");
            HunkIndex = hunkIndex;
            Choice = choice;
            CustomText = customText;
        }

        /// <summary>
        /// Replaces hunk <paramref name="hunkIndex"/> with <paramref name="text"/> (written as UTF-8; a line break is
        /// appended when missing). An empty string removes the hunk.
        /// </summary>
        public static ConflictHunkResolution Custom(int hunkIndex, string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            return new ConflictHunkResolution(hunkIndex, ConflictChoice.Custom, text);
        }

        /// <summary>Index of the hunk in <see cref="ConflictedFile.Hunks"/>.</summary>
        public int HunkIndex { get; }

        public ConflictChoice Choice { get; }

        /// <summary>Replacement text for <see cref="ConflictChoice.Custom"/>, otherwise null.</summary>
        public string? CustomText { get; }

        public override string ToString() => Choice == ConflictChoice.Custom ? $"#{HunkIndex}: Custom" : $"#{HunkIndex}: {Choice}";
    }

    /// <summary>
    /// One conflict place in a text file: the lines between <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> and <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>.
    /// Side texts keep their line breaks.
    /// </summary>
    public sealed class ConflictHunk
    {
        internal ConflictHunk(int index, int startLine, int endLine, string ours, string theirs, string? baseText,
            string? oursLabel, string? theirsLabel, string? baseLabel, ConflictHunkRange range)
        {
            Index = index;
            StartLine = startLine;
            EndLine = endLine;
            Ours = ours;
            Theirs = theirs;
            Base = baseText;
            OursLabel = oursLabel;
            TheirsLabel = theirsLabel;
            BaseLabel = baseLabel;
            Range = range;
        }

        /// <summary>Position in <see cref="ConflictedFile.Hunks"/>; use it in <see cref="ConflictHunkResolution"/>.</summary>
        public int Index { get; }

        /// <summary>1-based line number of the <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line in the file.</summary>
        public int StartLine { get; }

        /// <summary>1-based line number of the <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line in the file.</summary>
        public int EndLine { get; }

        /// <summary>Our lines ("A"). During a rebase this is the upstream side.</summary>
        public string Ours { get; }

        /// <summary>Their lines ("B"). During a rebase this is your commit being replayed.</summary>
        public string Theirs { get; }

        /// <summary>The common ancestor's lines; only present with the diff3/zdiff3 marker style.</summary>
        public string? Base { get; }

        /// <summary>Label after <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, e.g. "HEAD", "Updated upstream" or "ours"; null when the marker has none.</summary>
        public string? OursLabel { get; }

        /// <summary>Label after <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>, e.g. a branch name, "abc1234 (subject)" or "Stashed changes".</summary>
        public string? TheirsLabel { get; }

        /// <summary>Label after <c>|||||||</c> (diff3 style only).</summary>
        public string? BaseLabel { get; }

        internal ConflictHunkRange Range { get; }

        /// <summary>
        /// Display text for a choice using the marker labels: "Use HEAD", "Use feature", "Use HEAD then feature",
        /// "Use feature then HEAD" (or "ours"/"theirs" when the markers have no labels).
        /// </summary>
        public string GetChoiceLabel(ConflictChoice choice)
        {
            var ours = string.IsNullOrEmpty(OursLabel) ? "ours" : OursLabel;
            var theirs = string.IsNullOrEmpty(TheirsLabel) ? "theirs" : TheirsLabel;
            switch (choice)
            {
                case ConflictChoice.Ours: return "Use " + ours;
                case ConflictChoice.Theirs: return "Use " + theirs;
                case ConflictChoice.OursThenTheirs: return $"Use {ours} then {theirs}";
                case ConflictChoice.TheirsThenOurs: return $"Use {theirs} then {ours}";
                default: return "Custom";
            }
        }

        public override string ToString() => $"#{Index} lines {StartLine}-{EndLine}";
    }

    /// <summary>Byte offsets of a hunk in the file content (end offsets are exclusive).</summary>
    internal readonly struct ConflictHunkRange
    {
        public ConflictHunkRange(int blockStart, int blockEnd, int oursStart, int oursEnd, int theirsStart, int theirsEnd, bool usesCrLf)
        {
            BlockStart = blockStart;
            BlockEnd = blockEnd;
            OursStart = oursStart;
            OursEnd = oursEnd;
            TheirsStart = theirsStart;
            TheirsEnd = theirsEnd;
            UsesCrLf = usesCrLf;
        }

        /// <summary>Start of the <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line.</summary>
        public int BlockStart { get; }

        /// <summary>End of the <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line, including its line break.</summary>
        public int BlockEnd { get; }

        public int OursStart { get; }

        public int OursEnd { get; }

        public int TheirsStart { get; }

        public int TheirsEnd { get; }

        /// <summary>The start marker line ends with CRLF: git wrote the file with CRLF line endings.</summary>
        public bool UsesCrLf { get; }
    }

    /// <summary>A piece of a conflicted file in order: either unchanged text or a conflict hunk.</summary>
    public sealed class ConflictSegment
    {
        internal ConflictSegment(string text)
        {
            Text = text;
        }

        internal ConflictSegment(ConflictHunk hunk)
        {
            Hunk = hunk;
        }

        /// <summary>Unchanged text (null for a hunk).</summary>
        public string? Text { get; }

        /// <summary>The conflict hunk (null for unchanged text).</summary>
        public ConflictHunk? Hunk { get; }

        public bool IsConflict => Hunk != null;

        public override string ToString() => Hunk != null ? Hunk.ToString() : $"text ({Text!.Length} chars)";
    }

    /// <summary>
    /// A snapshot of a conflicted file: its conflict hunks and the text around them, read from the working tree.
    /// Pass it to <see cref="ConflictCommands.ResolveAsync(ConflictedFile, IEnumerable{ConflictHunkResolution}, bool, System.Threading.CancellationToken)"/>.
    /// </summary>
    public sealed class ConflictedFile
    {
        internal ConflictedFile(string path, ConflictKind kind, bool isSubmodule, bool existsInWorkingTree, bool isBinary, ConflictDocument? document)
        {
            Path = path;
            Kind = kind;
            IsSubmodule = isSubmodule;
            ExistsInWorkingTree = existsInWorkingTree;
            IsBinary = isBinary;
            Document = document;
        }

        /// <summary>Path relative to the repository root, with '/' separators.</summary>
        public string Path { get; }

        /// <summary>Which sides changed the file; <see cref="ConflictKind.None"/> when git doesn't consider it unmerged.</summary>
        public ConflictKind Kind { get; }

        public bool IsSubmodule { get; }

        /// <summary>False when the file is missing from the working tree (e.g. deleted on one side).</summary>
        public bool ExistsInWorkingTree { get; }

        /// <summary>True for binary content: there are no hunks, resolve the whole file with ours/theirs.</summary>
        public bool IsBinary { get; }

        /// <summary>
        /// True when conflict markers are present but don't form valid hunks (e.g. a side contains a line equal to
        /// the <c>=======</c> separator). There are no hunks: resolve the whole file or edit it manually.
        /// </summary>
        public bool HasMalformedMarkers => Document?.HasMalformedMarkers ?? false;

        /// <summary>Conflict hunks in file order (empty for binary, missing or already resolved files).</summary>
        public IReadOnlyList<ConflictHunk> Hunks => Document?.Hunks ?? (IReadOnlyList<ConflictHunk>)new ConflictHunk[0];

        /// <summary>The whole file as unchanged text and hunks, in order (empty for binary or missing files).</summary>
        public IReadOnlyList<ConflictSegment> Segments => Document?.Segments ?? (IReadOnlyList<ConflictSegment>)new ConflictSegment[0];

        /// <summary>True when the file has hunks that can be resolved one by one.</summary>
        public bool CanResolveHunks => Hunks.Count > 0;

        /// <summary>Label of our side from the first hunk (e.g. "HEAD"), or null.</summary>
        public string? OursLabel => Hunks.Count > 0 ? Hunks[0].OursLabel : null;

        /// <summary>Label of their side from the first hunk (e.g. a branch name), or null.</summary>
        public string? TheirsLabel => Hunks.Count > 0 ? Hunks[0].TheirsLabel : null;

        internal ConflictDocument? Document { get; }

        /// <summary>
        /// Returns the file's text after applying <paramref name="resolutions"/> (hunks not listed keep their markers),
        /// without writing anything: a preview of <see cref="ConflictCommands.ResolveAsync(ConflictedFile, IEnumerable{ConflictHunkResolution}, bool, System.Threading.CancellationToken)"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The file is binary or missing.</exception>
        public string GetResolvedText(IEnumerable<ConflictHunkResolution> resolutions)
        {
            if (Document == null)
                throw new InvalidOperationException($"'{Path}' has no text content to resolve.");
            var composed = ConflictMarkerParser.Compose(Document, resolutions);
            return ConflictMarkerParser.Decode(composed.Content, 0, composed.Content.Length, stripBom: true);
        }

        public override string ToString() => $"{Path} ({Kind}, {Hunks.Count} hunk(s))";
    }

    /// <summary>Summary of one conflicted path in a <see cref="ConflictOverview"/> (no file content is kept).</summary>
    public sealed class ConflictedFileInfo
    {
        internal ConflictedFileInfo(ConflictedFile file)
        {
            Path = file.Path;
            Kind = file.Kind;
            IsSubmodule = file.IsSubmodule;
            ExistsInWorkingTree = file.ExistsInWorkingTree;
            IsBinary = file.IsBinary;
            HasMalformedMarkers = file.HasMalformedMarkers;
            HunkCount = file.Hunks.Count;
        }

        /// <summary>Path relative to the repository root, with '/' separators.</summary>
        public string Path { get; }

        public ConflictKind Kind { get; }

        public bool IsSubmodule { get; }

        /// <summary>False when the file is missing from the working tree (e.g. deleted on one side).</summary>
        public bool ExistsInWorkingTree { get; }

        /// <summary>True for binary content: resolve the whole file with ours/theirs.</summary>
        public bool IsBinary { get; }

        /// <summary>Markers are present but don't form valid hunks (see <see cref="ConflictedFile.HasMalformedMarkers"/>).</summary>
        public bool HasMalformedMarkers { get; }

        /// <summary>Number of conflict hunks still in the file.</summary>
        public int HunkCount { get; }

        /// <summary>
        /// True when the file has hunks that can be resolved one by one. A text file that is still unmerged but has
        /// no hunks was edited by hand (or kept one side): stage it with <see cref="ConflictCommands.MarkResolvedAsync"/>.
        /// </summary>
        public bool CanResolveHunks => HunkCount > 0;

        public override string ToString() => $"{Path} ({Kind}, {HunkCount} hunk(s))";
    }

    /// <summary>The conflict state of a repository: the operation in progress and every unmerged path.</summary>
    public sealed class ConflictOverview
    {
        internal ConflictOverview(RepositoryState operation, IReadOnlyList<ConflictedFileInfo> files)
        {
            Operation = operation;
            Files = files;
        }

        /// <summary>
        /// The merge/rebase/cherry-pick/revert that stopped. <see cref="RepositoryState.None"/> with conflicts means a
        /// stash apply/pop conflicted (the stash is kept: drop it after resolving).
        /// </summary>
        public RepositoryState Operation { get; }

        /// <summary>Unmerged paths, in git's order.</summary>
        public IReadOnlyList<ConflictedFileInfo> Files { get; }

        public bool HasConflicts => Files.Count > 0;

        /// <summary>Sum of <see cref="ConflictedFileInfo.HunkCount"/> over all files.</summary>
        public int TotalHunks => Files.Sum(f => f.HunkCount);

        public override string ToString() => $"{Operation}: {Files.Count} file(s), {TotalHunks} hunk(s)";
    }
}
