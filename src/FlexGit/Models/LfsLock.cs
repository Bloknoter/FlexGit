#nullable enable
using System;

namespace FlexGit
{
    /// <summary>A Git LFS file lock held on the LFS server.</summary>
    public sealed class LfsLock
    {
        internal LfsLock(string id, string path, string ownerName, DateTimeOffset? lockedAt, bool? isOwnedByCurrentUser)
        {
            Id = id;
            Path = path;
            OwnerName = ownerName;
            LockedAt = lockedAt;
            IsOwnedByCurrentUser = isOwnedByCurrentUser;
        }

        /// <summary>Server-side lock id.</summary>
        public string Id { get; }

        /// <summary>Locked path, relative to the repository root.</summary>
        public string Path { get; }

        /// <summary>Name of the lock owner as reported by the server.</summary>
        public string OwnerName { get; }

        public DateTimeOffset? LockedAt { get; }

        /// <summary>True/false when listed with ownership verification, otherwise null.</summary>
        public bool? IsOwnedByCurrentUser { get; }

        public override string ToString() => $"{Path} (locked by {OwnerName})";
    }
}
