#nullable enable
using System;
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>One git process invocation.</summary>
    internal sealed class GitRunRequest
    {
        public GitRunRequest(params string[] arguments)
        {
            Arguments = new List<string>(arguments);
        }

        public GitRunRequest(IEnumerable<string> arguments)
        {
            Arguments = new List<string>(arguments);
        }

        /// <summary>Arguments after "git" (FlexGit's default <c>-c</c> options are prepended automatically).</summary>
        public List<string> Arguments { get; }

        /// <summary>Working directory of the process. Repository commands run in the repository root.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Bytes written to stdin before it is closed. Stdin is always closed so git never waits for input.</summary>
        public byte[]? StandardInput { get; set; }

        /// <summary>Receives parsed progress lines from stderr.</summary>
        public IProgress<GitProgress>? Progress { get; set; }

        /// <summary>Read-only commands set GIT_OPTIONAL_LOCKS=0 so they never take index.lock.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>
        /// Sets GIT_LITERAL_PATHSPECS=1. Off for typed APIs: they prefix each path with the ":(literal)" magic instead,
        /// because the environment variable changes git internals (e.g. "stash push -u" then leaves untracked files behind).
        /// </summary>
        public bool LiteralPathspecs { get; set; }

        /// <summary>Per-invocation environment variables (applied after the repository-wide ones).</summary>
        public IDictionary<string, string?>? Environment { get; set; }

        public GitRunRequest Add(string argument)
        {
            Arguments.Add(argument);
            return this;
        }

        public GitRunRequest AddIf(bool condition, string argument)
        {
            if (condition)
                Arguments.Add(argument);
            return this;
        }
    }
}
