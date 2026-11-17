#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlexGit
{
    /// <summary>
    /// Minimal JSON reader for git-lfs <c>--json</c> output (System.Text.Json is not part of .NET Standard 2.1).
    /// Produces Dictionary&lt;string, object?&gt;, List&lt;object?&gt;, string, double, bool or null.
    /// </summary>
    internal sealed class JsonReader
    {
        private readonly string _text;
        private int _position;

        private JsonReader(string text) => _text = text;

        public static object? Parse(string text)
        {
            var reader = new JsonReader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (reader._position != reader._text.Length)
                throw new FormatException($"Unexpected trailing JSON content at {reader._position}.");
            return value;
        }

        private object? ReadValue()
        {
            SkipWhitespace();
            if (_position >= _text.Length)
                throw new FormatException("Unexpected end of JSON.");

            switch (_text[_position])
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default: return ReadNumber();
            }
        }

        private Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            _position++; // {
            SkipWhitespace();
            if (Peek() == '}')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                var key = ReadString();
                SkipWhitespace();
                Consume(':');
                result[key] = ReadValue();
                SkipWhitespace();
                if (Peek() == ',')
                {
                    _position++;
                    continue;
                }
                Consume('}');
                return result;
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _position++; // [
            SkipWhitespace();
            if (Peek() == ']')
            {
                _position++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();
                if (Peek() == ',')
                {
                    _position++;
                    continue;
                }
                Consume(']');
                return result;
            }
        }

        private string ReadString()
        {
            Consume('"');
            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var c = _text[_position++];
                if (c == '"')
                    return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (_position >= _text.Length)
                    break;
                var escape = _text[_position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length)
                            throw new FormatException("Invalid unicode escape in JSON.");
                        builder.Append((char)int.Parse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _position += 4;
                        break;
                    default:
                        throw new FormatException($"Invalid escape '\\{escape}' in JSON.");
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private double ReadNumber()
        {
            var start = _position;
            while (_position < _text.Length && "+-0123456789.eE".IndexOf(_text[_position]) >= 0)
                _position++;
            if (start == _position)
                throw new FormatException($"Unexpected character '{_text[start]}' in JSON at {start}.");
            return double.Parse(_text.Substring(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
                throw new FormatException($"Expected '{literal}' in JSON at {_position}.");
            _position += literal.Length;
        }

        private void Consume(char expected)
        {
            if (Peek() != expected)
                throw new FormatException($"Expected '{expected}' in JSON at {_position}.");
            _position++;
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }
    }

    /// <summary>Parses <c>git lfs locks/lock --json</c> output.</summary>
    internal static class LfsLockParser
    {
        /// <summary>Parses a lock list: a JSON array, or {"ours": [...], "theirs": [...]} with --verify.</summary>
        public static List<LfsLock> ParseList(string json)
        {
            var locks = new List<LfsLock>();
            var root = JsonReader.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);

            if (root is List<object?> array)
            {
                foreach (var item in array)
                    AddLock(locks, item, null);
            }
            else if (root is Dictionary<string, object?> verified)
            {
                if (verified.TryGetValue("ours", out var ours) && ours is List<object?> oursList)
                    foreach (var item in oursList)
                        AddLock(locks, item, true);
                if (verified.TryGetValue("theirs", out var theirs) && theirs is List<object?> theirsList)
                    foreach (var item in theirsList)
                        AddLock(locks, item, false);
            }
            return locks;
        }

        public static LfsLock? ParseSingle(string json)
        {
            var root = JsonReader.Parse(json);
            return root is Dictionary<string, object?> map ? ToLock(map, null) : null;
        }

        private static void AddLock(List<LfsLock> locks, object? item, bool? ours)
        {
            if (item is Dictionary<string, object?> map)
                locks.Add(ToLock(map, ours));
        }

        private static LfsLock ToLock(Dictionary<string, object?> map, bool? ours)
        {
            string Get(string key) => map.TryGetValue(key, out var value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

            var owner = string.Empty;
            if (map.TryGetValue("owner", out var ownerValue) && ownerValue is Dictionary<string, object?> ownerMap &&
                ownerMap.TryGetValue("name", out var name) && name != null)
                owner = Convert.ToString(name, CultureInfo.InvariantCulture) ?? string.Empty;

            return new LfsLock(Get("id"), Get("path"), owner, RefParser.ParseDate(Get("locked_at")), ours);
        }
    }
}
