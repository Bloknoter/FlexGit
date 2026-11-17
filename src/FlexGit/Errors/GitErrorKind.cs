#nullable enable

namespace FlexGit
{
    /// <summary>Category of a git failure, derived from the exit code and git's (English) error output.</summary>
    public enum GitErrorKind
    {
        /// <summary>The failure could not be categorized. See <see cref="GitError.StandardError"/>.</summary>
        Unknown,

        /// <summary>No git executable was found.</summary>
        GitNotFound,

        /// <summary>The installed git is older than <see cref="GitLocator.MinimumVersion"/>.</summary>
        GitTooOld,

        /// <summary>The path is not inside a git repository.</summary>
        NotARepository,

        /// <summary>git refuses to work in the repository because it is owned by another user (safe.directory).</summary>
        UnsafeRepository,

        /// <summary>user.name / user.email are not configured.</summary>
        IdentityNotConfigured,

        /// <summary>Credentials were rejected or could not be obtained.</summary>
        AuthenticationFailed,

        /// <summary>The remote host could not be reached.</summary>
        NetworkError,

        /// <summary>The remote (or the repository behind its URL) does not exist.</summary>
        RemoteNotFound,

        /// <summary>A branch, tag, revision or path does not exist.</summary>
        NotFound,

        /// <summary>A branch, tag or remote with that name already exists.</summary>
        AlreadyExists,

        /// <summary>The name is not a valid branch/tag/remote name.</summary>
        InvalidName,

        /// <summary>The remote rejected the push (usually non-fast-forward: pull first).</summary>
        Rejected,

        /// <summary>The operation would overwrite uncommitted local changes.</summary>
        LocalChangesWouldBeOverwritten,

        /// <summary>The operation stopped because of merge conflicts.</summary>
        Conflicts,

        /// <summary>Another git process holds the index lock (index.lock exists).</summary>
        IndexLocked,

        /// <summary>There is nothing to commit.</summary>
        NothingToCommit,

        /// <summary>The current branch has no upstream branch.</summary>
        NoUpstream,

        /// <summary>A fast-forward-only operation was not possible because the branches diverged.</summary>
        CannotFastForward,

        /// <summary>The branch is not fully merged and was not deleted (use force).</summary>
        NotFullyMerged,

        /// <summary>No merge/rebase is in progress to continue or abort.</summary>
        NoOperationInProgress,

        /// <summary>The repository uses Git LFS but git-lfs is not installed.</summary>
        LfsNotInstalled
    }
}
