#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    public sealed partial class BranchCommands
    {
        /// <summary>
        /// Switches to a branch (<c>git switch</c>). Never discards local changes unless
        /// <see cref="SwitchOptions.DiscardLocalChanges"/> is set: conflicting changes produce
        /// <see cref="SwitchStatus.LocalChangesWouldBeOverwritten"/>. Switching to "feature" when only
        /// "origin/feature" exists creates a local tracking branch.
        /// </summary>
        public async Task<SwitchResult> SwitchAsync(string branch, SwitchOptions? options = null, IProgress<GitProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(branch, nameof(branch));
            options ??= new SwitchOptions();

            var request = Context.Request("switch");
            request.Progress = progress;
            request.AddIf(progress != null, "--progress");
            request.AddIf(options.DiscardLocalChanges, "--discard-changes");
            if (options.Track == true)
                request.Add("--track");
            else if (options.Track == false)
                request.Add("--no-track");

            if (options.Create)
            {
                request.Add("--create").Add(branch);
                if (options.StartPoint != null)
                    request.Add(GitArguments.EnsureSafeRef(options.StartPoint, nameof(options.StartPoint)));
            }
            else
            {
                request.Add(branch);
            }

            return await RunSwitchAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Checks out a commit without a branch (detached HEAD).</summary>
        public Task<SwitchResult> DetachAsync(string revision, bool discardLocalChanges = false, IProgress<GitProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            var request = Context.Request("switch", "--detach");
            request.Progress = progress;
            request.AddIf(progress != null, "--progress");
            request.AddIf(discardLocalChanges, "--discard-changes");
            request.Add(revision);
            return RunSwitchAsync(request, cancellationToken);
        }

        private async Task<SwitchResult> RunSwitchAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    var error = result.ToError();
                    var status = error.Kind switch
                    {
                        GitErrorKind.LocalChangesWouldBeOverwritten => SwitchStatus.LocalChangesWouldBeOverwritten,
                        GitErrorKind.NotFound => SwitchStatus.NotFound,
                        GitErrorKind.AlreadyExists => SwitchStatus.AlreadyExists,
                        _ => SwitchStatus.Failed
                    };
                    return new SwitchResult(status, error.Message, null, null, error);
                }

                var branch = await Context.ReadCurrentBranchAsync(cancellationToken).ConfigureAwait(false);
                var head = await Context.TryRevParseAsync("HEAD", cancellationToken).ConfigureAwait(false);
                var message = branch != null ? $"Switched to '{branch}'." : $"HEAD is now at {GitRepository.Short(head)} (detached).";
                return new SwitchResult(SwitchStatus.Switched, message, branch, head, null);
            }
        }
    }
}
