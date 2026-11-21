#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FlexGit
{
    /// <summary>One <c>diff --git</c> section of a patch.</summary>
    internal sealed class DiffSection
    {
        public DiffSection(string oldPath, string newPath, DiffChangeKind change, bool isBinary, string? oldObjectId, IReadOnlyList<DiffHunk> hunks)
        {
            OldPath = oldPath;
            NewPath = newPath;
            Change = change;
            IsBinary = isBinary;
            OldObjectId = oldObjectId;
            Hunks = hunks;
        }

        /// <summary>Path on the old side (equals <see cref="NewPath"/> unless renamed).</summary>
        public string OldPath { get; }

        public string NewPath { get; }

        public DiffChangeKind Change { get; }

        public bool IsBinary { get; }

        /// <summary>Old blob id from the <c>index</c> line (full with <c>--full-index</c>); null when the file is new.</summary>
        public string? OldObjectId { get; }

        public IReadOnlyList<DiffHunk> Hunks { get; }
    }

    /// <summary>
    /// Parses git's patch format (<c>diff-index -p</c>), the format <c>git apply</c> reads. Hunk boundaries come from
    /// the line counts in the <c>@@</c> headers, so content lines that look like headers stay content.
    /// </summary>
    internal static class DiffParser
    {
        private const string DiffHeader = "diff --git ";
        private const string LfsPointerVersion = "version https://git-lfs.github.com/spec/";

        private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);
        private static readonly Regex s_hunkHeader = new Regex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(?: (.*))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<DiffSection> Parse(byte[] output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            var lines = SplitLines(output);
            var sections = new List<DiffSection>();
            var index = 0;
            while (index < lines.Count)
            {
                var header = DecodeLine(output, lines[index]);
                if (header.StartsWith(DiffHeader, StringComparison.Ordinal))
                    sections.Add(ParseSection(output, lines, ref index, header.Substring(DiffHeader.Length)));
                else
                    index++;
            }
            return sections;
        }

        /// <summary>The diff of a new file with <paramref name="content"/>: one hunk with every line added (none when empty).</summary>
        public static IReadOnlyList<DiffHunk> CreateAddedHunks(byte[] content)
        {
            var ranges = SplitLines(content);
            if (ranges.Count == 0)
                return new DiffHunk[0];

            var endsWithNewline = content[content.Length - 1] == (byte)'\n';
            var lines = new List<DiffLine>(ranges.Count);
            for (var i = 0; i < ranges.Count; i++)
            {
                var noNewline = i == ranges.Count - 1 && !endsWithNewline;
                lines.Add(new DiffLine(DiffLineKind.Added, DecodeText(content, ranges[i].Start, ranges[i].End), null, i + 1, noNewline));
            }
            return new[] { new DiffHunk(0, 0, 1, lines.Count, string.Empty, lines) };
        }

        /// <summary>Returns the <c>size</c> of a Git LFS pointer, or null when <paramref name="lines"/> are not a pointer.</summary>
        public static long? ParseLfsPointerSize(IEnumerable<string> lines)
        {
            var first = true;
            foreach (var line in lines)
            {
                if (first)
                {
                    if (!line.StartsWith(LfsPointerVersion, StringComparison.Ordinal))
                        return null;
                    first = false;
                    continue;
                }
                if (line.StartsWith("size ", StringComparison.Ordinal) &&
                    long.TryParse(line.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                    return size;
            }
            return null;
        }

        /// <summary>Reads <c>git check-attr -z</c> output (<c>path NUL attribute NUL value NUL</c>...) for one path.</summary>
        public static IReadOnlyDictionary<string, string> ParseAttributes(string checkAttrOutput, string path)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            var fields = (checkAttrOutput ?? string.Empty).Split('\0');
            for (var i = 0; i + 2 < fields.Length; i += 3)
            {
                if (string.Equals(fields[i], path, StringComparison.Ordinal))
                    attributes[fields[i + 1]] = fields[i + 2];
            }
            return attributes;
        }

        private static DiffSection ParseSection(byte[] output, List<LineRange> lines, ref int index, string header)
        {
            var headerPath = ParseHeaderPath(header);
            string? renameFrom = null;
            string? renameTo = null;
            string? oldObjectId = null;
            var change = DiffChangeKind.Modified;
            var isBinary = false;

            // Extended header lines up to the first hunk or the next file.
            for (index++; index < lines.Count; index++)
            {
                var line = DecodeLine(output, lines[index]);
                if (line.StartsWith(DiffHeader, StringComparison.Ordinal) || line.StartsWith("@@ ", StringComparison.Ordinal))
                    break;
                if (line.StartsWith("new file mode ", StringComparison.Ordinal))
                    change = DiffChangeKind.Added;
                else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
                    change = DiffChangeKind.Deleted;
                else if (line.StartsWith("rename from ", StringComparison.Ordinal))
                    renameFrom = Unquote(line.Substring("rename from ".Length));
                else if (line.StartsWith("rename to ", StringComparison.Ordinal))
                    renameTo = Unquote(line.Substring("rename to ".Length));
                else if (line.StartsWith("index ", StringComparison.Ordinal))
                    oldObjectId = ParseOldObjectId(line.Substring("index ".Length));
                else if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line == "GIT binary patch")
                    isBinary = true;
            }

            var hunks = new List<DiffHunk>();
            while (index < lines.Count)
            {
                var match = s_hunkHeader.Match(DecodeLine(output, lines[index]));
                if (!match.Success)
                    break;
                index++;
                hunks.Add(ParseHunk(output, lines, ref index, match));
            }

            if (renameFrom != null && renameTo != null)
                return new DiffSection(renameFrom, renameTo, DiffChangeKind.Renamed, isBinary, oldObjectId, hunks);
            var path = headerPath ?? string.Empty;
            return new DiffSection(path, path, change, isBinary, oldObjectId, hunks);
        }

        private static DiffHunk ParseHunk(byte[] output, List<LineRange> lines, ref int index, Match header)
        {
            var oldStart = ParseNumber(header.Groups[1].Value);
            var oldCount = header.Groups[2].Success ? ParseNumber(header.Groups[2].Value) : 1;
            var newStart = ParseNumber(header.Groups[3].Value);
            var newCount = header.Groups[4].Success ? ParseNumber(header.Groups[4].Value) : 1;
            var heading = header.Groups[5].Success ? header.Groups[5].Value.TrimEnd('\r') : string.Empty;

            var result = new List<DiffLine>(oldCount + newCount);
            int oldLine = oldStart, newLine = newStart, oldLeft = oldCount, newLeft = newCount;
            for (; index < lines.Count; index++)
            {
                var range = lines[index];
                var marker = range.End > range.Start ? (char)output[range.Start] : ' '; // diff.suppressBlankEmpty prints "" for " "
                if (marker == '\\')
                {
                    // "\ No newline at end of file" belongs to the line before it.
                    if (result.Count > 0)
                    {
                        var last = result[result.Count - 1];
                        result[result.Count - 1] = new DiffLine(last.Kind, last.Text, last.OldLineNumber, last.NewLineNumber, true);
                    }
                    continue;
                }
                if (oldLeft == 0 && newLeft == 0)
                    break;

                var text = range.End > range.Start ? DecodeText(output, range.Start + 1, range.End) : string.Empty;
                if (marker == ' ' && oldLeft > 0 && newLeft > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Unchanged, text, oldLine++, newLine++, false));
                    oldLeft--;
                    newLeft--;
                }
                else if (marker == '-' && oldLeft > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Removed, text, oldLine++, null, false));
                    oldLeft--;
                }
                else if (marker == '+' && newLeft > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Added, text, null, newLine++, false));
                    newLeft--;
                }
                else
                {
                    break; // malformed: the hunk ends early
                }
            }
            return new DiffHunk(oldStart, oldCount, newStart, newCount, heading, result);
        }

        /// <summary>
        /// Reads the path from <c>a/P b/P</c> (or the quoted form). Both names are the same unless the file was renamed;
        /// renames return null here and take their paths from the <c>rename from/to</c> lines.
        /// </summary>
        private static string? ParseHeaderPath(string header)
        {
            string oldName;
            string newName;
            if (header.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = ReadQuoted(header, 0, out oldName);
                if (end + 1 >= header.Length || header[end] != ' ')
                    return null;
                var rest = header.Substring(end + 1);
                newName = rest.StartsWith("\"", StringComparison.Ordinal) ? Unquote(rest) : rest;
            }
            else
            {
                // "a/" + P + " b/" + P: the halves have the same length.
                if ((header.Length - 1) % 2 != 0)
                    return null;
                var half = (header.Length - 1) / 2;
                oldName = header.Substring(0, half);
                newName = header.Substring(half + 1);
                if (header[half] != ' ')
                    return null;
            }

            if (!oldName.StartsWith("a/", StringComparison.Ordinal) || !newName.StartsWith("b/", StringComparison.Ordinal))
                return null;
            var path = oldName.Substring(2);
            return string.Equals(path, newName.Substring(2), StringComparison.Ordinal) ? path : null;
        }

        private static string? ParseOldObjectId(string indexLine)
        {
            var separator = indexLine.IndexOf("..", StringComparison.Ordinal);
            if (separator <= 0)
                return null;
            var id = indexLine.Substring(0, separator);
            return id.Trim('0').Length == 0 ? null : id;
        }

        /// <summary>Undoes git's C-style quoting (<c>"a\"b\303\244"</c>); unquoted text is returned as is.</summary>
        private static string Unquote(string value)
        {
            if (!value.StartsWith("\"", StringComparison.Ordinal))
                return value;
            ReadQuoted(value, 0, out var result);
            return result;
        }

        /// <summary>Reads a quoted string starting at <paramref name="start"/>; returns the index after the closing quote.</summary>
        private static int ReadQuoted(string text, int start, out string value)
        {
            // Escapes stand for raw bytes (\303\244 is UTF-8 "ä"), so the result is collected as bytes.
            var bytes = new List<byte>();
            var i = start + 1;
            while (i < text.Length && text[i] != '"')
            {
                if (text[i] != '\\' || i + 1 >= text.Length)
                {
                    var runStart = i++;
                    while (i < text.Length && text[i] != '"' && text[i] != '\\')
                        i++;
                    bytes.AddRange(s_utf8.GetBytes(text.Substring(runStart, i - runStart)));
                    continue;
                }

                var next = text[i + 1];
                if (i + 3 < text.Length && IsOctalDigit(next) && IsOctalDigit(text[i + 2]) && IsOctalDigit(text[i + 3]))
                {
                    bytes.Add((byte)Convert.ToInt32(text.Substring(i + 1, 3), 8));
                    i += 4;
                    continue;
                }
                bytes.Add(next switch
                {
                    'a' => (byte)7,
                    'b' => (byte)8,
                    't' => (byte)'\t',
                    'n' => (byte)'\n',
                    'v' => (byte)11,
                    'f' => (byte)12,
                    'r' => (byte)'\r',
                    _ => (byte)next
                });
                i += 2;
            }
            value = s_utf8.GetString(bytes.ToArray());
            return Math.Min(i + 1, text.Length);
        }

        private static bool IsOctalDigit(char c) => c >= '0' && c <= '7';

        private static int ParseNumber(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

        private static string DecodeLine(byte[] data, LineRange range) => s_utf8.GetString(data, range.Start, range.End - range.Start);

        /// <summary>Decodes content text without its line break (a trailing CR is part of a CRLF break).</summary>
        private static string DecodeText(byte[] data, int start, int end)
        {
            if (end > start && data[end - 1] == (byte)'\r')
                end--;
            return end > start ? s_utf8.GetString(data, start, end - start) : string.Empty;
        }

        /// <summary>Line byte ranges without the '\n'; a final line break doesn't start another line.</summary>
        private static List<LineRange> SplitLines(byte[] data)
        {
            var lines = new List<LineRange>();
            var start = 0;
            for (var i = 0; i < data.Length; i++)
            {
                if (data[i] != (byte)'\n')
                    continue;
                lines.Add(new LineRange(start, i));
                start = i + 1;
            }
            if (start < data.Length)
                lines.Add(new LineRange(start, data.Length));
            return lines;
        }

        private readonly struct LineRange
        {
            public LineRange(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start { get; }

            /// <summary>Exclusive, before the '\n'.</summary>
            public int End { get; }
        }
    }
}
