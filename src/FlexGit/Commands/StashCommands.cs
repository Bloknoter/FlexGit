#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Stash operations. Access through <see cref="GitRepository.Stash"/>.</summary>
    public sealed partial class StashCommands
    {
        private static readonly Regex s_stashIndex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private readonly GitRepository _repository;

        internal StashCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>
        /// Saves local changes to a new stash and reverts the working tree.
        /// Untracked files are included by default (ignored files never are).
        /// </summary>
        public async Task<StashResult> PushAsync(string? message = null, bool includeUntracked = true, IEnumerable<string>? paths = null,
            CancellationToken cancellationToken = default)
        {
            var request = Context.Request("stash", "push");
            request.AddIf(includeUntracked, "--include-untracked");
            if (!string.IsNullOrEmpty(message))
                request.Add("--message=" + message);
            if (paths != null)
            {
                var repositoryPaths = Context.ToRepositoryPaths(paths);
                if (repositoryPaths.Count > 0)
                    GitContext.AddPathspecsViaStdin(request, repositoryPaths);
            }

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return Failure(result);
            if (result.StandardOutput.IndexOf("No local changes to save", StringComparison.OrdinalIgnoreCase) >= 0)
                return new StashResult(StashStatus.NothingToStash, "No local changes to save.", null);
            return new StashResult(StashStatus.Succeeded, "Changes stashed.", null);
        }

        /// <summary>Lists stashes, newest first.</summary>
        public async Task<IReadOnlyList<StashEntry>> ListAsync(CancellationToken cancellationToken = default)
        {
            var request = Context.Request("stash", "list", "--format=%gd%x00%H%x00%gs%x00%cI");
            request.ReadOnly = true;
            var result = await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);

            var entries = new List<StashEntry>();
            foreach (var line in RefParser.SplitLines(result.StandardOutput))
            {
                var f = line.Split('\0');
                if (f.Length < 4)
                    continue;
                var match = s_stashIndex.Match(f[0]);
                var index = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : entries.Count;
                entries.Add(new StashEntry(index, f[0], f[1], f[2], RefParser.ParseDate(f[3])));
            }
            return entries;
        }

        /// <summary>Applies a stash and keeps it in the list.</summary>
        /// <param name="restoreIndex">Also restore which changes were staged.</param>
        public Task<StashResult> ApplyAsync(int index = 0, bool restoreIndex = false, CancellationToken cancellationToken = default)
            => ApplyOrPopAsync("apply", index, restoreIndex, cancellationToken);

        /// <summary>Applies a stash and removes it from the list (it is kept when the apply produced conflicts).</summary>
        public Task<StashResult> PopAsync(int index = 0, bool restoreIndex = false, CancellationToken cancellationToken = default)
            => ApplyOrPopAsync("pop", index, restoreIndex, cancellationToken);

        /// <summary>Deletes a stash.</summary>
        public Task DropAsync(int index = 0, CancellationToken cancellationToken = default)
            => Context.RunLockedCheckedAsync(Context.Request("stash", "drop", "--quiet", StashRef(index)), cancellationToken);

        /// <summary>Deletes all stashes.</summary>
        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Context.RunLockedCheckedAsync(Context.Request("stash", "clear"), cancellationToken);

        private async Task<StashResult> ApplyOrPopAsync(string verb, int index, bool restoreIndex, CancellationToken cancellationToken)
        {
            var request = Context.Request("stash", verb, "--quiet");
            request.AddIf(restoreIndex, "--index");
            request.Add(StashRef(index));

            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    return new StashResult(StashStatus.Succeeded, $"Stash {verb} succeeded.", null);

                // Conflicts are reported with exit code 1 and leave unmerged entries.
                var status = await Context.ReadStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No }, cancellationToken).ConfigureAwait(false);
                if (status.HasConflicts)
                    return new StashResult(StashStatus.Conflicts, "Applying the stash produced conflicts.", result.ToError());
                return Failure(result);
            }
        }

        private static StashResult Failure(GitResult result)
        {
            var error = result.ToError();
            var status = error.Kind switch
            {
                GitErrorKind.Conflicts => StashStatus.Conflicts,
                GitErrorKind.LocalChangesWouldBeOverwritten => StashStatus.LocalChangesWouldBeOverwritten,
                GitErrorKind.NotFound => StashStatus.NotFound,
                _ => StashStatus.Failed
            };
            return new StashResult(status, error.Message, error);
        }

        private static string StashRef(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            return "stash@{" + index.ToString(CultureInfo.InvariantCulture) + "}";
        }
    }
}
