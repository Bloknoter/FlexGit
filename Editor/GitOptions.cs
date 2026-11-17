#nullable enable
using System;
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>Severity of a diagnostic message sent to <see cref="GitOptions.Logger"/>.</summary>
    public enum GitLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Configuration shared by every git process FlexGit starts for a repository.
    /// </summary>
    public sealed class GitOptions
    {
        /// <summary>
        /// Full path to the git executable. When null, git is located automatically
        /// (PATH first, then the standard Git for Windows install folders).
        /// </summary>
        public string? GitExecutablePath { get; set; }

        /// <summary>
        /// Extra environment variables for every git process (for example <c>GIT_SSH_COMMAND</c>).
        /// Entries override FlexGit's defaults; a null value removes the variable.
        /// </summary>
        public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Receives diagnostic messages (every executed command, failures).
        /// Called from background threads; the handler must be thread-safe.
        /// </summary>
        public Action<GitLogLevel, string>? Logger { get; set; }

        /// <summary>Creates an independent copy of these options.</summary>
        public GitOptions Clone()
        {
            var copy = new GitOptions
            {
                GitExecutablePath = GitExecutablePath,
                Logger = Logger
            };
            foreach (var pair in Environment)
                copy.Environment[pair.Key] = pair.Value;
            return copy;
        }

        internal void Log(GitLogLevel level, string message)
        {
            var logger = Logger;
            if (logger == null)
                return;
            try
            {
                logger(level, message);
            }
            catch
            {
                // A faulty logger must never break a git operation.
            }
        }
    }
}
