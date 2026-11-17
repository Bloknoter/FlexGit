#nullable enable
using System;

namespace FlexGit
{
    /// <summary>
    /// Thrown when a git command fails in a way the API does not report through a result object
    /// (queries, simple ref/config manipulation, missing git, ...).
    /// </summary>
    public sealed class GitCommandException : Exception
    {
        public GitCommandException(GitError error)
            : base(error.ToString())
        {
            Error = error;
        }

        /// <summary>Details of the failure.</summary>
        public GitError Error { get; }

        /// <summary>Category of the failure.</summary>
        public GitErrorKind Kind => Error.Kind;
    }
}
