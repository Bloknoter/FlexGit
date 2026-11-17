#nullable enable
using System;

namespace FlexGit
{
    /// <summary>Describes a failed git invocation.</summary>
    public sealed class GitError
    {
        public GitError(GitErrorKind kind, string message, int exitCode = -1, string standardError = "", string commandLine = "")
        {
            Kind = kind;
            Message = message ?? string.Empty;
            ExitCode = exitCode;
            StandardError = standardError ?? string.Empty;
            CommandLine = commandLine ?? string.Empty;
        }

        /// <summary>Category of the failure.</summary>
        public GitErrorKind Kind { get; }

        /// <summary>The most relevant line of git's output (or a FlexGit explanation).</summary>
        public string Message { get; }

        /// <summary>Exit code of the git process, or -1 if git did not run.</summary>
        public int ExitCode { get; }

        /// <summary>Full standard error output of the git process.</summary>
        public string StandardError { get; }

        /// <summary>The executed command line, with credentials removed from URLs.</summary>
        public string CommandLine { get; }

        public override string ToString()
        {
            var text = $"{Kind}: {Message}";
            if (CommandLine.Length > 0)
                text += $"{Environment.NewLine}Command: {CommandLine} (exit code {ExitCode})";
            return text;
        }
    }
}
