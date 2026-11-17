#nullable enable

namespace FlexGit
{
    /// <summary>Options for <see cref="GitRepository.CloneAsync"/>.</summary>
    public sealed class CloneOptions
    {
        /// <summary>Branch to check out (null = the remote's default branch).</summary>
        public string? Branch { get; set; }

        /// <summary>Create a shallow clone with this many commits (null = full history).</summary>
        public int? Depth { get; set; }

        /// <summary>Also clone submodules.</summary>
        public bool RecurseSubmodules { get; set; }

        /// <summary>Create a bare repository (no working tree).</summary>
        public bool Bare { get; set; }
    }

    /// <summary>Options for <see cref="GitRepository.FetchAsync"/>.</summary>
    public sealed class FetchOptions
    {
        /// <summary>Remote to fetch from (null = the current branch's remote, else "origin").</summary>
        public string? Remote { get; set; }

        /// <summary>Fetch from every remote.</summary>
        public bool AllRemotes { get; set; }

        /// <summary>Delete remote-tracking branches that no longer exist on the remote (default true).</summary>
        public bool Prune { get; set; } = true;

        /// <summary>Fetch all tags.</summary>
        public bool Tags { get; set; }
    }

    /// <summary>How <see cref="GitRepository.PullAsync"/> integrates the fetched commits.</summary>
    public enum PullMode
    {
        /// <summary>Merge (fast-forward when possible).</summary>
        Merge,
        /// <summary>Rebase local commits on top of the fetched ones.</summary>
        Rebase,
        /// <summary>Only fast-forward; fail with <see cref="PullStatus.CannotFastForward"/> when branches diverged.</summary>
        FastForwardOnly
    }

    /// <summary>Options for <see cref="GitRepository.PullAsync"/>.</summary>
    public sealed class PullOptions
    {
        public PullMode Mode { get; set; } = PullMode.Merge;

        /// <summary>Remote to pull from (null = the current branch's upstream).</summary>
        public string? Remote { get; set; }

        /// <summary>Remote branch to pull (requires <see cref="Remote"/>; null = the upstream branch).</summary>
        public string? Branch { get; set; }

        /// <summary>Stash local changes before and re-apply them after the pull (<c>--autostash</c>).</summary>
        public bool AutoStash { get; set; }
    }

    /// <summary>Options for <see cref="GitRepository.PushAsync"/>.</summary>
    public sealed class PushOptions
    {
        /// <summary>Remote to push to (null = the branch's push remote / remote, else "origin").</summary>
        public string? Remote { get; set; }

        /// <summary>What to push, e.g. "HEAD", "main", "feature:release" (null = the current branch).</summary>
        public string? RefSpec { get; set; }

        /// <summary>
        /// Set the pushed branch as upstream (<c>-u</c>). Null = automatically when the current branch has no upstream yet.
        /// </summary>
        public bool? SetUpstream { get; set; }

        /// <summary>Also push annotated tags reachable from the pushed commits.</summary>
        public bool FollowTags { get; set; }

        /// <summary>Also push all tags.</summary>
        public bool AllTags { get; set; }

        /// <summary>Overwrite the remote branch if it hasn't changed since the last fetch (<c>--force-with-lease</c>).</summary>
        public bool ForceWithLease { get; set; }
    }

    /// <summary>Fast-forward behaviour of <see cref="GitRepository.MergeAsync"/>.</summary>
    public enum FastForwardMode
    {
        /// <summary>Fast-forward when possible, otherwise create a merge commit.</summary>
        Auto,
        /// <summary>Only fast-forward; fail when a merge commit would be needed.</summary>
        Only,
        /// <summary>Always create a merge commit.</summary>
        Never
    }

    /// <summary>Options for <see cref="GitRepository.MergeAsync"/>.</summary>
    public sealed class MergeOptions
    {
        public FastForwardMode FastForward { get; set; } = FastForwardMode.Auto;

        /// <summary>Stage the combined changes without committing (<c>--squash</c>).</summary>
        public bool Squash { get; set; }

        /// <summary>Merge commit message (null = git's default "Merge branch '...'").</summary>
        public string? Message { get; set; }
    }
}
