#nullable enable
using System;
using System.Text;

namespace FlexGit
{
    /// <summary>Raw outcome of a git process.</summary>
    public sealed class GitResult
    {
        private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);
        private string? _standardOutput;

        internal GitResult(int exitCode, byte[] standardOutputBytes, string standardError, string commandLine, TimeSpan duration)
        {
            ExitCode = exitCode;
            StandardOutputBytes = standardOutputBytes;
            StandardError = standardError;
            CommandLine = commandLine;
            Duration = duration;
        }

        /// <summary>Exit code of the git process (0 = success).</summary>
        public int ExitCode { get; }

        /// <summary>Standard output decoded as UTF-8.</summary>
        public string StandardOutput => _standardOutput ??= s_utf8.GetString(StandardOutputBytes);

        /// <summary>Standard output as raw bytes (for binary content such as file blobs).</summary>
        public byte[] StandardOutputBytes { get; }

        /// <summary>Standard error (progress and error messages).</summary>
        public string StandardError { get; }

        /// <summary>The executed command line, with credentials removed from URLs.</summary>
        public string CommandLine { get; }

        /// <summary>Wall-clock duration of the process.</summary>
        public TimeSpan Duration { get; }

        /// <summary>True when the exit code is 0.</summary>
        public bool Succeeded => ExitCode == 0;

        /// <summary>Categorizes this result as an error.</summary>
        public GitError ToError() => GitErrorClassifier.Classify(this);

        /// <summary>Throws <see cref="GitCommandException"/> unless the command succeeded.</summary>
        public GitResult EnsureSuccess()
        {
            if (!Succeeded)
                throw new GitCommandException(ToError());
            return this;
        }
    }
}
