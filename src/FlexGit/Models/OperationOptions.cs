#nullable enable
using System;

namespace FlexGit
{
    /// <summary>Options for <see cref="GitRepository.CommitAsync"/>.</summary>
    public sealed class CommitOptions
    {
        /// <summary>Replace the last commit instead of creating a new one.</summary>
        public bool Amend { get; set; }

        /// <summary>Allow a commit without changes.</summary>
        public bool AllowEmpty { get; set; }

        /// <summary>Allow an empty message.</summary>
        public bool AllowEmptyMessage { get; set; }

        /// <summary>Stage modified and deleted tracked files before committing (<c>git commit --all</c>).</summary>
        public bool StageTrackedChanges { get; set; }

        /// <summary>Skip pre-commit and commit-msg hooks (<c>--no-verify</c>).</summary>
        public bool SkipHooks { get; set; }

        /// <summary>Author and committer for this commit. Null = user.name/user.email from git config.</summary>
        public UserIdentity? Author { get; set; }
    }

    /// <summary>Options for <see cref="BranchCommands.SwitchAsync"/>.</summary>
    public sealed class SwitchOptions
    {
        /// <summary>Create the branch (<c>switch -c</c>).</summary>
        public bool Create { get; set; }

        /// <summary>Start point for a new branch (null = HEAD).</summary>
        public string? StartPoint { get; set; }

        /// <summary>
        /// Upstream tracking for a new branch: true = track the start point, false = don't, null = git's default
        /// (switching to "feature" when only "origin/feature" exists creates a tracking branch automatically).
        /// </summary>
        public bool? Track { get; set; }

        /// <summary>Throw away local changes that conflict with the target (<c>--discard-changes</c>). Destructive.</summary>
        public bool DiscardLocalChanges { get; set; }
    }

    /// <summary>What <see cref="GitRepository.ResetAsync"/> resets.</summary>
    public enum ResetMode
    {
        /// <summary>Move the branch only; index and working tree keep the changes (staged).</summary>
        Soft,
        /// <summary>Move the branch and reset the index; working tree keeps the changes (unstaged).</summary>
        Mixed,
        /// <summary>Move the branch and discard all changes in index and working tree. Destructive.</summary>
        Hard
    }

    /// <summary>One entry of <c>git stash list</c>.</summary>
    public sealed class StashEntry
    {
        internal StashEntry(int index, string name, string sha, string message, DateTimeOffset? date)
        {
            Index = index;
            Name = name;
            Sha = sha;
            Message = message;
            Date = date;
        }

        /// <summary>Position in the stash list (0 = newest).</summary>
        public int Index { get; }

        /// <summary>Stash ref, e.g. "stash@{0}".</summary>
        public string Name { get; }

        public string Sha { get; }

        public string Message { get; }

        public DateTimeOffset? Date { get; }

        public override string ToString() => $"{Name}: {Message}";
    }
}
