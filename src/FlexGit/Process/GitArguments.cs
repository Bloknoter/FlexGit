#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FlexGit
{
    /// <summary>
    /// Builds git command lines safely. <c>ProcessStartInfo.ArgumentList</c> is not available in
    /// .NET Standard 2.1, so arguments are quoted with the Windows CommandLineToArgvW rules,
    /// which Mono and .NET also use to split <c>ProcessStartInfo.Arguments</c> on Unix.
    /// </summary>
    internal static class GitArguments
    {
        private static readonly char[] s_charsRequiringQuotes = { ' ', '\t', '\n', '\v', '"' };
        private static readonly Regex s_urlCredentials = new Regex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)[^/@\s]+@", RegexOptions.Compiled);
        private static readonly UTF8Encoding s_utf8NoBom = new UTF8Encoding(false);

        public static string Join(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                AppendQuoted(builder, argument);
            }
            return builder.ToString();
        }

        public static string Quote(string argument)
        {
            var builder = new StringBuilder();
            AppendQuoted(builder, argument);
            return builder.ToString();
        }

        private static void AppendQuoted(StringBuilder builder, string argument)
        {
            if (argument == null)
                throw new ArgumentNullException(nameof(argument));

            if (argument.Length > 0 && argument.IndexOfAny(s_charsRequiringQuotes) < 0)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            for (var i = 0; i < argument.Length; i++)
            {
                var backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Backslashes before the closing quote must be doubled.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }
            }
            builder.Append('"');
        }

        /// <summary>Removes credentials embedded in URLs (https://user:token@host → https://***@host).</summary>
        public static string RedactUrls(string text)
        {
            return string.IsNullOrEmpty(text) ? text : s_urlCredentials.Replace(text, "${scheme}***@");
        }

        /// <summary>
        /// Guards values that are passed as revisions/names (never as pathspecs) so they can't be
        /// interpreted as command-line options.
        /// </summary>
        public static string EnsureSafeRef(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value cannot be null or empty.", parameterName);
            if (value![0] == '-')
                throw new ArgumentException($"'{value}' is not allowed: values passed to git must not start with '-'.", parameterName);
            if (value.IndexOf('\0') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                throw new ArgumentException("Value must not contain NUL or line breaks.", parameterName);
            return value;
        }

        /// <summary>Encodes paths for <c>--pathspec-from-file=- --pathspec-file-nul</c>.</summary>
        public static byte[] NulSeparatedPaths(IEnumerable<string> paths)
        {
            var builder = new StringBuilder();
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                if (builder.Length > 0)
                    builder.Append('\0');
                builder.Append(NormalizePathSeparators(path));
            }
            return s_utf8NoBom.GetBytes(builder.ToString());
        }

        public static string NormalizePathSeparators(string path) => path.Replace('\\', '/');

        /// <summary>
        /// Marks a repository-relative path as a literal pathspec, so "Texture[1].png" or "*.txt" match only
        /// that exact file. Preferred over GIT_LITERAL_PATHSPECS, which changes the behaviour of some git internals.
        /// </summary>
        public static string LiteralPathspec(string repositoryPath) => ":(literal)" + NormalizePathSeparators(repositoryPath);

        public static byte[] Utf8(string text) => s_utf8NoBom.GetBytes(text);
    }
}
