#nullable enable
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>A commit from the history.</summary>
    public sealed class CommitInfo
    {
        internal CommitInfo(string sha, IReadOnlyList<string> parentShas, GitSignature author, GitSignature committer, string subject, string body)
        {
            Sha = sha;
            ParentShas = parentShas;
            Author = author;
            Committer = committer;
            Subject = subject;
            Body = body;
        }

        /// <summary>Full commit id.</summary>
        public string Sha { get; }

        /// <summary>Abbreviated commit id (7 characters).</summary>
        public string ShortSha => Sha.Length > 7 ? Sha.Substring(0, 7) : Sha;

        public IReadOnlyList<string> ParentShas { get; }

        public GitSignature Author { get; }

        public GitSignature Committer { get; }

        /// <summary>First line of the message.</summary>
        public string Subject { get; }

        /// <summary>Message after the first paragraph (may be empty).</summary>
        public string Body { get; }

        /// <summary>Full message.</summary>
        public string Message => Body.Length == 0 ? Subject : Subject + "\n\n" + Body;

        public bool IsMerge => ParentShas.Count > 1;

        public override string ToString() => $"{ShortSha} {Subject}";
    }
}
