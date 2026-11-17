#nullable enable
using System;
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>Base class of operation results: a status enum, a human-readable message and the git error (if any).</summary>
    public abstract class GitOperationResult<TStatus> where TStatus : struct, Enum
    {
        protected GitOperationResult(TStatus status, string message, GitError? error)
        {
            Status = status;
            Message = message ?? string.Empty;
            Error = error;
        }

        /// <summary>Outcome of the operation.</summary>
        public TStatus Status { get; }

        /// <summary>Human-readable summary (git's most relevant output line on failure).</summary>
        public string Message { get; }

        /// <summary>Details of the git failure, or null when git succeeded.</summary>
        public GitError? Error { get; }

        /// <summary>True when the operation achieved its goal.</summary>
        public abstract bool Succeeded { get; }

        public override string ToString() => $"{Status}: {Message}";
    }

    public enum CommitStatus
    {
        Committed,
        NothingToCommit,
        Failed
    }

    /// <summary>Result of <see cref="GitRepository.CommitAsync"/>.</summary>
    public sealed class CommitResult : GitOperationResult<CommitStatus>
    {
        internal CommitResult(CommitStatus status, string message, string? commitSha, GitError? error)
            : base(status, message, error)
        {
            CommitSha = commitSha;
        }

        /// <summary>Id of the new commit.</summary>
        public string? CommitSha { get; }

        public override bool Succeeded => Status == CommitStatus.Committed;
    }

    public enum SwitchStatus
    {
        Switched,
        LocalChangesWouldBeOverwritten,
        NotFound,
        AlreadyExists,
        Failed
    }

    /// <summary>Result of switching branches or detaching HEAD.</summary>
    public sealed class SwitchResult : GitOperationResult<SwitchStatus>
    {
        internal SwitchResult(SwitchStatus status, string message, string? branch, string? headSha, GitError? error)
            : base(status, message, error)
        {
            Branch = branch;
            HeadSha = headSha;
        }

        /// <summary>The branch HEAD now points to (null when detached or on failure).</summary>
        public string? Branch { get; }

        /// <summary>The commit HEAD now points to.</summary>
        public string? HeadSha { get; }

        public override bool Succeeded => Status == SwitchStatus.Switched;
    }

    public enum StashStatus
    {
        Succeeded,
        NothingToStash,
        Conflicts,
        LocalChangesWouldBeOverwritten,
        NotFound,
        Failed
    }

    /// <summary>Result of stash push/apply/pop.</summary>
    public sealed class StashResult : GitOperationResult<StashStatus>
    {
        internal StashResult(StashStatus status, string message, GitError? error)
            : base(status, message, error)
        {
        }

        public override bool Succeeded => Status == StashStatus.Succeeded;
    }

    public enum ConflictResolutionStatus
    {
        Resolved,
        /// <summary>Some files still contain conflict markers; nothing was staged.</summary>
        ConflictMarkersFound,
        Failed
    }

    /// <summary>Result of marking conflicts as resolved.</summary>
    public sealed class ConflictResolutionResult : GitOperationResult<ConflictResolutionStatus>
    {
        internal ConflictResolutionResult(ConflictResolutionStatus status, string message, IReadOnlyList<string> filesWithMarkers, GitError? error)
            : base(status, message, error)
        {
            FilesWithMarkers = filesWithMarkers;
        }

        /// <summary>Files that still contain &lt;&lt;&lt;&lt;&lt;&lt;&lt; / ======= / &gt;&gt;&gt;&gt;&gt;&gt;&gt; markers.</summary>
        public IReadOnlyList<string> FilesWithMarkers { get; }

        public override bool Succeeded => Status == ConflictResolutionStatus.Resolved;
    }

    public enum MergeStatus
    {
        UpToDate,
        FastForwarded,
        Merged,
        /// <summary>Squash merge: changes are staged but not committed.</summary>
        Squashed,
        Conflicts,
        LocalChangesWouldBeOverwritten,
        CannotFastForward,
        Aborted,
        NoMergeInProgress,
        Failed
    }

    /// <summary>Result of merge, merge abort and merge continue.</summary>
    public sealed class MergeResult : GitOperationResult<MergeStatus>
    {
        internal MergeResult(MergeStatus status, string message, string? headSha, GitError? error)
            : base(status, message, error)
        {
            HeadSha = headSha;
        }

        /// <summary>HEAD after the operation.</summary>
        public string? HeadSha { get; }

        public override bool Succeeded =>
            Status == MergeStatus.UpToDate || Status == MergeStatus.FastForwarded || Status == MergeStatus.Merged ||
            Status == MergeStatus.Squashed || Status == MergeStatus.Aborted;
    }

    public enum PullStatus
    {
        UpToDate,
        FastForwarded,
        Merged,
        Rebased,
        Conflicts,
        LocalChangesWouldBeOverwritten,
        NoUpstream,
        CannotFastForward,
        Failed
    }

    /// <summary>Result of <see cref="GitRepository.PullAsync"/>.</summary>
    public sealed class PullResult : GitOperationResult<PullStatus>
    {
        internal PullResult(PullStatus status, string message, string? headSha, GitError? error)
            : base(status, message, error)
        {
            HeadSha = headSha;
        }

        /// <summary>HEAD after the pull.</summary>
        public string? HeadSha { get; }

        public override bool Succeeded =>
            Status == PullStatus.UpToDate || Status == PullStatus.FastForwarded || Status == PullStatus.Merged || Status == PullStatus.Rebased;
    }

    public enum FetchStatus
    {
        Succeeded,
        Failed
    }

    /// <summary>Result of <see cref="GitRepository.FetchAsync"/>.</summary>
    public sealed class FetchResult : GitOperationResult<FetchStatus>
    {
        internal FetchResult(FetchStatus status, string message, GitError? error)
            : base(status, message, error)
        {
        }

        public override bool Succeeded => Status == FetchStatus.Succeeded;
    }

    public enum PushStatus
    {
        Pushed,
        UpToDate,
        Rejected,
        Failed
    }

    /// <summary>How a single ref was updated by a push.</summary>
    public enum PushRefStatus
    {
        FastForward,
        Forced,
        Deleted,
        New,
        Rejected,
        UpToDate
    }

    /// <summary>One line of <c>git push --porcelain</c>.</summary>
    public sealed class PushRefUpdate
    {
        internal PushRefUpdate(PushRefStatus status, string source, string destination, string summary)
        {
            Status = status;
            Source = source;
            Destination = destination;
            Summary = summary;
        }

        public PushRefStatus Status { get; }

        /// <summary>Local ref (empty for deletions).</summary>
        public string Source { get; }

        /// <summary>Remote ref.</summary>
        public string Destination { get; }

        /// <summary>git's summary, e.g. "abc..def" or "[rejected] (fetch first)".</summary>
        public string Summary { get; }

        public override string ToString() => $"{Status} {Source}:{Destination} {Summary}";
    }

    /// <summary>Result of a push.</summary>
    public sealed class PushResult : GitOperationResult<PushStatus>
    {
        internal PushResult(PushStatus status, string message, IReadOnlyList<PushRefUpdate> refUpdates, GitError? error)
            : base(status, message, error)
        {
            RefUpdates = refUpdates;
        }

        /// <summary>Per-ref outcome.</summary>
        public IReadOnlyList<PushRefUpdate> RefUpdates { get; }

        public override bool Succeeded => Status == PushStatus.Pushed || Status == PushStatus.UpToDate;
    }

    public enum RebaseStatus
    {
        Completed,
        UpToDate,
        Conflicts,
        LocalChangesWouldBeOverwritten,
        Aborted,
        NoRebaseInProgress,
        Failed
    }

    /// <summary>Result of rebase start/continue/skip/abort.</summary>
    public sealed class RebaseResult : GitOperationResult<RebaseStatus>
    {
        internal RebaseResult(RebaseStatus status, string message, string? headSha, GitError? error)
            : base(status, message, error)
        {
            HeadSha = headSha;
        }

        public string? HeadSha { get; }

        public override bool Succeeded =>
            Status == RebaseStatus.Completed || Status == RebaseStatus.UpToDate || Status == RebaseStatus.Aborted;
    }

    public enum CloneStatus
    {
        Cloned,
        DirectoryNotEmpty,
        Failed
    }

    /// <summary>Result of <see cref="GitRepository.CloneAsync"/>.</summary>
    public sealed class CloneResult : GitOperationResult<CloneStatus>
    {
        internal CloneResult(CloneStatus status, string message, GitRepository? repository, GitError? error)
            : base(status, message, error)
        {
            Repository = repository;
        }

        /// <summary>The cloned repository (null on failure).</summary>
        public GitRepository? Repository { get; }

        public override bool Succeeded => Status == CloneStatus.Cloned;
    }

    public enum LfsLockStatus
    {
        Locked,
        Unlocked,
        /// <summary>Someone else holds the lock.</summary>
        LockedByOtherUser,
        Failed
    }

    /// <summary>Result of an LFS lock/unlock request.</summary>
    public sealed class LfsLockResult : GitOperationResult<LfsLockStatus>
    {
        internal LfsLockResult(LfsLockStatus status, string message, LfsLock? lockInfo, GitError? error)
            : base(status, message, error)
        {
            Lock = lockInfo;
        }

        /// <summary>The lock that was created (for lock requests).</summary>
        public LfsLock? Lock { get; }

        public override bool Succeeded => Status == LfsLockStatus.Locked || Status == LfsLockStatus.Unlocked;
    }
}
