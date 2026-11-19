#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FlexGit
{
    /// <summary>A text file split into unchanged text and conflict hunks.</summary>
    internal sealed class ConflictDocument
    {
        public ConflictDocument(byte[] content, IReadOnlyList<ConflictHunk> hunks, IReadOnlyList<ConflictSegment> segments, bool hasMalformedMarkers)
        {
            Content = content;
            Hunks = hunks;
            Segments = segments;
            HasMalformedMarkers = hasMalformedMarkers;
        }

        /// <summary>The file bytes the document was parsed from.</summary>
        public byte[] Content { get; }

        public IReadOnlyList<ConflictHunk> Hunks { get; }

        public IReadOnlyList<ConflictSegment> Segments { get; }

        public bool HasMalformedMarkers { get; }
    }

    /// <summary>Output of <see cref="ConflictMarkerParser.Compose"/>.</summary>
    internal readonly struct ComposedConflictFile
    {
        public ComposedConflictFile(byte[] content, int remainingHunks)
        {
            Content = content;
            RemainingHunks = remainingHunks;
        }

        public byte[] Content { get; }

        /// <summary>Hunks that were not resolved and still have their markers.</summary>
        public int RemainingHunks { get; }
    }

    /// <summary>
    /// Parses the conflict markers git writes into working-tree files (merge, diff3 and zdiff3 styles) and builds
    /// resolved content. Works on bytes, so text that isn't UTF-8 is copied unchanged; only display strings are decoded.
    /// </summary>
    internal static class ConflictMarkerParser
    {
        /// <summary>git's default marker length (the <c>conflict-marker-size</c> attribute changes it).</summary>
        public const int DefaultMarkerSize = 7;

        /// <summary>git's binary heuristic looks at the first 8000 bytes.</summary>
        private const int BinaryProbeLength = 8000;

        private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);
        private static readonly byte[] s_lf = { (byte)'\n' };
        private static readonly byte[] s_crlf = { (byte)'\r', (byte)'\n' };

        private enum State
        {
            Outside,
            Ours,
            Base,
            Theirs
        }

        /// <summary>True when the first 8000 bytes contain a NUL byte (git's own binary check).</summary>
        public static bool IsBinary(byte[] content)
        {
            var probe = Math.Min(content.Length, BinaryProbeLength);
            for (var i = 0; i < probe; i++)
            {
                if (content[i] == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Splits <paramref name="content"/> into text and hunks. Markers are exactly <paramref name="markerSize"/>
        /// characters long, followed by the end of the line or a space and a label (longer runs are content). Any
        /// structural problem (a second separator, a start inside a hunk, a missing end) reports no hunks at all,
        /// because the file can't be split without guessing.
        /// </summary>
        public static ConflictDocument Parse(byte[] content, int markerSize)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (markerSize <= 0)
                markerSize = DefaultMarkerSize;

            var bomLength = HasUtf8Bom(content) ? 3 : 0;
            var hunks = new List<ConflictHunk>();
            var segments = new List<ConflictSegment>();
            var state = State.Outside;
            var textStart = bomLength;

            // Current hunk.
            int blockStart = 0, startLine = 0, oursStart = 0, oursEnd = 0, baseStart = -1, baseEnd = -1, theirsStart = 0;
            string? oursLabel = null, baseLabel = null;
            var usesCrLf = false;

            var lineNumber = 0;
            var position = bomLength;
            while (position < content.Length)
            {
                lineNumber++;
                var lineStart = position;
                var newline = Array.IndexOf(content, (byte)'\n', lineStart);
                var next = newline < 0 ? content.Length : newline + 1;
                var contentEnd = newline < 0 ? content.Length : newline;
                var hasCr = contentEnd > lineStart && content[contentEnd - 1] == '\r';
                if (hasCr)
                    contentEnd--;
                position = next;

                string? label;
                switch (state)
                {
                    case State.Outside:
                        if (IsMarker(content, lineStart, contentEnd, '<', markerSize, true, out label))
                        {
                            if (lineStart > textStart)
                                segments.Add(new ConflictSegment(Decode(content, textStart, lineStart, false)));
                            blockStart = lineStart;
                            startLine = lineNumber;
                            oursLabel = label;
                            usesCrLf = hasCr;
                            oursStart = next;
                            baseStart = baseEnd = -1;
                            baseLabel = null;
                            state = State.Ours;
                        }
                        break;

                    case State.Ours:
                        if (IsMarker(content, lineStart, contentEnd, '|', markerSize, true, out label))
                        {
                            oursEnd = lineStart;
                            baseStart = next;
                            baseLabel = label;
                            state = State.Base;
                        }
                        else if (IsMarker(content, lineStart, contentEnd, '=', markerSize, false, out _))
                        {
                            oursEnd = lineStart;
                            theirsStart = next;
                            state = State.Theirs;
                        }
                        else if (IsAnyMarker(content, lineStart, contentEnd, markerSize))
                        {
                            return Malformed(content, bomLength);
                        }
                        break;

                    case State.Base:
                        if (IsMarker(content, lineStart, contentEnd, '=', markerSize, false, out _))
                        {
                            baseEnd = lineStart;
                            theirsStart = next;
                            state = State.Theirs;
                        }
                        else if (IsAnyMarker(content, lineStart, contentEnd, markerSize))
                        {
                            return Malformed(content, bomLength);
                        }
                        break;

                    case State.Theirs:
                        if (IsMarker(content, lineStart, contentEnd, '>', markerSize, true, out label))
                        {
                            var range = new ConflictHunkRange(blockStart, next, oursStart, oursEnd, theirsStart, lineStart, usesCrLf);
                            var hunk = new ConflictHunk(
                                hunks.Count, startLine, lineNumber,
                                Decode(content, oursStart, oursEnd, false),
                                Decode(content, theirsStart, lineStart, false),
                                baseStart >= 0 ? Decode(content, baseStart, baseEnd, false) : null,
                                oursLabel, label, baseLabel, range);
                            hunks.Add(hunk);
                            segments.Add(new ConflictSegment(hunk));
                            textStart = next;
                            state = State.Outside;
                        }
                        else if (IsAnyMarker(content, lineStart, contentEnd, markerSize))
                        {
                            return Malformed(content, bomLength);
                        }
                        break;
                }
            }

            if (state != State.Outside)
                return Malformed(content, bomLength);

            if (content.Length > textStart)
                segments.Add(new ConflictSegment(Decode(content, textStart, content.Length, false)));

            return new ConflictDocument(content, hunks, segments, false);
        }

        /// <summary>
        /// Replaces the marker block of every resolved hunk with the chosen content; everything else is copied byte for
        /// byte, and unresolved hunks keep their markers.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A hunk index doesn't exist.</exception>
        /// <exception cref="ArgumentException">A hunk is resolved twice.</exception>
        public static ComposedConflictFile Compose(ConflictDocument document, IEnumerable<ConflictHunkResolution> resolutions)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (resolutions == null)
                throw new ArgumentNullException(nameof(resolutions));

            var byHunk = new Dictionary<int, ConflictHunkResolution>();
            foreach (var resolution in resolutions)
            {
                if (resolution == null)
                    throw new ArgumentException("Resolutions cannot contain null.", nameof(resolutions));
                if (resolution.HunkIndex >= document.Hunks.Count)
                    throw new ArgumentOutOfRangeException(nameof(resolutions),
                        $"Hunk {resolution.HunkIndex} does not exist; the file has {document.Hunks.Count} hunk(s).");
                if (byHunk.ContainsKey(resolution.HunkIndex))
                    throw new ArgumentException($"Hunk {resolution.HunkIndex} is resolved more than once.", nameof(resolutions));
                byHunk.Add(resolution.HunkIndex, resolution);
            }

            var content = document.Content;
            var output = new MemoryStream(content.Length);
            var position = 0;
            var remaining = 0;
            foreach (var hunk in document.Hunks)
            {
                var range = hunk.Range;
                if (!byHunk.TryGetValue(hunk.Index, out var resolution))
                {
                    remaining++;
                    continue; // copied with the surrounding text
                }

                output.Write(content, position, range.BlockStart - position);
                var lineBreak = range.UsesCrLf ? s_crlf : s_lf;
                switch (resolution.Choice)
                {
                    case ConflictChoice.Ours:
                        WriteLines(output, content, range.OursStart, range.OursEnd, lineBreak);
                        break;
                    case ConflictChoice.Theirs:
                        WriteLines(output, content, range.TheirsStart, range.TheirsEnd, lineBreak);
                        break;
                    case ConflictChoice.OursThenTheirs:
                        WriteLines(output, content, range.OursStart, range.OursEnd, lineBreak);
                        WriteLines(output, content, range.TheirsStart, range.TheirsEnd, lineBreak);
                        break;
                    case ConflictChoice.TheirsThenOurs:
                        WriteLines(output, content, range.TheirsStart, range.TheirsEnd, lineBreak);
                        WriteLines(output, content, range.OursStart, range.OursEnd, lineBreak);
                        break;
                    case ConflictChoice.Custom:
                        var custom = s_utf8.GetBytes(resolution.CustomText ?? string.Empty);
                        WriteLines(output, custom, 0, custom.Length, lineBreak);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(resolutions), $"Unknown choice {resolution.Choice}.");
                }
                position = range.BlockEnd;
            }

            output.Write(content, position, content.Length - position);
            return new ComposedConflictFile(output.ToArray(), remaining);
        }

        /// <summary>
        /// Reads <c>git check-attr -z conflict-marker-size</c> output (<c>path NUL attribute NUL value NUL</c>...).
        /// Unspecified, set, unset and non-positive values mean the default size.
        /// </summary>
        public static IReadOnlyDictionary<string, int> ParseMarkerSizes(string checkAttrOutput)
        {
            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
            var fields = (checkAttrOutput ?? string.Empty).Split('\0');
            for (var i = 0; i + 2 < fields.Length; i += 3)
            {
                var path = fields[i];
                if (path.Length == 0)
                    continue;
                sizes[path] = int.TryParse(fields[i + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0
                    ? size
                    : DefaultMarkerSize;
            }
            return sizes;
        }

        /// <summary>Decodes a byte range as UTF-8 for display, optionally without a leading BOM.</summary>
        public static string Decode(byte[] content, int start, int end, bool stripBom)
        {
            if (stripBom && start == 0 && HasUtf8Bom(content))
                start = Math.Min(3, end);
            return end > start ? s_utf8.GetString(content, start, end - start) : string.Empty;
        }

        private static bool HasUtf8Bom(byte[] content) =>
            content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;

        private static ConflictDocument Malformed(byte[] content, int bomLength)
        {
            var segments = content.Length > bomLength
                ? new[] { new ConflictSegment(Decode(content, bomLength, content.Length, false)) }
                : new ConflictSegment[0];
            return new ConflictDocument(content, new ConflictHunk[0], segments, true);
        }

        /// <summary>Writes a non-empty range and makes sure it ends with a line break, so the next line stays separate.</summary>
        private static void WriteLines(Stream output, byte[] source, int start, int end, byte[] lineBreak)
        {
            if (end <= start)
                return;
            output.Write(source, start, end - start);
            if (source[end - 1] != '\n')
                output.Write(lineBreak, 0, lineBreak.Length);
        }

        private static bool IsAnyMarker(byte[] content, int start, int end, int size) =>
            IsMarker(content, start, end, '<', size, true, out _) ||
            IsMarker(content, start, end, '|', size, true, out _) ||
            IsMarker(content, start, end, '=', size, false, out _) ||
            IsMarker(content, start, end, '>', size, true, out _);

        /// <summary>
        /// True when the line (without its line break) is exactly <paramref name="size"/> <paramref name="marker"/>
        /// characters, optionally followed by a space and a label.
        /// </summary>
        private static bool IsMarker(byte[] content, int start, int end, char marker, int size, bool allowLabel, out string? label)
        {
            label = null;
            if (end - start < size)
                return false;
            for (var i = 0; i < size; i++)
            {
                if (content[start + i] != marker)
                    return false;
            }

            var afterMarker = start + size;
            if (afterMarker == end)
                return true;
            if (!allowLabel || content[afterMarker] != ' ')
                return false;
            label = Decode(content, afterMarker + 1, end, false);
            return true;
        }
    }
}
