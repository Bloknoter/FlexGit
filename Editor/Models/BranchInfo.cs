#nullable enable
using System;

namespace FlexGit
{
    /// <summary>A local or remote-tracking branch.</summary>
    public sealed class BranchInfo
    {
        internal BranchInfo()
        {
        }

        /// <summary>Short name: "main" or, for remote-tracking branches, "origin/main".</summary>
        public string Name { get; internal set; } = string.Empty;

        /// <summary>Full ref name: "refs/heads/main" or "refs/remotes/origin/main".</summary>
        public string CanonicalName { get; internal set; } = string.Empty;

        /// <summary>True for remote-tracking branches (refs/remotes/...).</summary>
        public bool IsRemote { get; internal set; }

        /// <summary>
        /// For remote-tracking branches: the remote they belong to. For local branches: the remote of the upstream (or null).
        /// </summary>
        public string? RemoteName { get; internal set; }

        /// <summary>Branch name without the remote prefix ("main" for "origin/main").</summary>
        public string FriendlyName { get; internal set; } = string.Empty;

        /// <summary>Commit the branch points to.</summary>
        public string TipSha { get; internal set; } = string.Empty;

        /// <summary>Upstream branch of a local branch ("origin/main"), or null.</summary>
        public string? Upstream { get; internal set; }

        /// <summary>True when the configured upstream branch no longer exists on the remote.</summary>
        public bool IsUpstreamGone { get; internal set; }

        /// <summary>Commits not yet on the upstream.</summary>
        public int Ahead { get; internal set; }

        /// <summary>Upstream commits not yet on this branch.</summary>
        public int Behind { get; internal set; }

        /// <summary>True for the branch HEAD points to.</summary>
        public bool IsCurrent { get; internal set; }

        /// <summary>Committer date of the tip commit.</summary>
        public DateTimeOffset? LastCommitDate { get; internal set; }

        /// <summary>Subject of the tip commit.</summary>
        public string LastCommitSubject { get; internal set; } = string.Empty;

        public bool HasUpstream => Upstream != null;

        public override string ToString() => IsCurrent ? $"* {Name}" : Name;
    }
}
