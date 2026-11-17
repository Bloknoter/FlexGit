#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Branch operations. Access through <see cref="GitRepository.Branches"/>.</summary>
    public sealed partial class BranchCommands
    {
        private readonly GitRepository _repository;

        internal BranchCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Lists local and/or remote-tracking branches.</summary>
        public async Task<IReadOnlyList<BranchInfo>> ListAsync(BranchScope scope = BranchScope.Local, CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var remotes = scope == BranchScope.Local
                    ? new List<string>()
                    : await Context.ReadRemoteNamesAsync(cancellationToken).ConfigureAwait(false);

                var request = Context.Request("for-each-ref", "--format=" + RefParser.BranchFormat);
                request.ReadOnly = true;
                if (scope != BranchScope.Remote)
                    request.Add("refs/heads");
                if (scope != BranchScope.Local)
                    request.Add("refs/remotes");

                var result = await Context.RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
                return RefParser.ParseBranches(result.StandardOutput, remotes);
            }
        }

        /// <summary>Name of the current branch (also for a branch without commits), or null when HEAD is detached.</summary>
        public async Task<string?> GetCurrentNameAsync(CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await Context.ReadCurrentBranchAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The current branch, or null when HEAD is detached or the branch has no commits yet.</summary>
        public async Task<BranchInfo?> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var branches = await ListAsync(BranchScope.Local, cancellationToken).ConfigureAwait(false);
            return branches.FirstOrDefault(b => b.IsCurrent);
        }

        /// <summary>True when a local branch with this name exists.</summary>
        public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            var request = Context.Request("show-ref", "--verify", "--quiet", "refs/heads/" + name);
            request.ReadOnly = true;
            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }

        /// <summary>Creates a branch without switching to it.</summary>
        /// <param name="name">New branch name.</param>
        /// <param name="startPoint">Revision to start from (null = HEAD).</param>
        /// <param name="track">Set the start point as upstream (use with a remote-tracking start point such as "origin/feature").</param>
        public Task CreateAsync(string name, string? startPoint = null, bool track = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            var request = Context.Request("branch", track ? "--track" : "--no-track", name);
            if (startPoint != null)
                request.Add(GitArguments.EnsureSafeRef(startPoint, nameof(startPoint)));
            return Context.RunLockedCheckedAsync(request, cancellationToken);
        }

        /// <summary>Deletes a local branch. Without <paramref name="force"/>, git refuses to delete unmerged branches (<see cref="GitErrorKind.NotFullyMerged"/>).</summary>
        public Task DeleteAsync(string name, bool force = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            return Context.RunLockedCheckedAsync(Context.Request("branch", force ? "-D" : "-d", name), cancellationToken);
        }

        /// <summary>Renames a local branch.</summary>
        public Task RenameAsync(string oldName, string newName, bool force = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(oldName, nameof(oldName));
            GitArguments.EnsureSafeRef(newName, nameof(newName));
            return Context.RunLockedCheckedAsync(Context.Request("branch", force ? "-M" : "-m", oldName, newName), cancellationToken);
        }

        /// <summary>Sets the upstream of <paramref name="branch"/>, e.g. to "origin/main".</summary>
        public Task SetUpstreamAsync(string branch, string upstream, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(branch, nameof(branch));
            GitArguments.EnsureSafeRef(upstream, nameof(upstream));
            return Context.RunLockedCheckedAsync(Context.Request("branch", "--set-upstream-to=" + upstream, branch), cancellationToken);
        }

        /// <summary>Removes the upstream configuration of <paramref name="branch"/>.</summary>
        public Task UnsetUpstreamAsync(string branch, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(branch, nameof(branch));
            return Context.RunLockedCheckedAsync(Context.Request("branch", "--unset-upstream", branch), cancellationToken);
        }
    }
}
