#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Remote management. Access through <see cref="GitRepository.Remotes"/>.</summary>
    public sealed class RemoteCommands
    {
        private readonly GitRepository _repository;

        internal RemoteCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Lists configured remotes with their URLs.</summary>
        public async Task<IReadOnlyList<RemoteInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            var request = Context.Request("config", "-z", "--get-regexp", @"^remote\..*\.(url|pushurl)$");
            request.ReadOnly = true;
            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 1 && result.StandardError.Length == 0)
                return new RemoteInfo[0]; // no remotes configured
            result.EnsureSuccess();
            return RefParser.ParseRemotes(result.StandardOutput);
        }

        /// <summary>Returns the remote with this name, or null.</summary>
        public async Task<RemoteInfo?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            var remotes = await ListAsync(cancellationToken).ConfigureAwait(false);
            return remotes.FirstOrDefault(r => r.Name == name);
        }

        /// <summary>Adds a remote (<c>git remote add</c>).</summary>
        public Task AddAsync(string name, string url, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            GitArguments.EnsureSafeRef(url, nameof(url));
            return Context.RunLockedCheckedAsync(Context.Request("remote", "add", "--", name, url), cancellationToken);
        }

        /// <summary>
        /// Changes a remote's URL. Unlike removing and re-adding, this keeps remote-tracking branches and upstream settings.
        /// </summary>
        public Task SetUrlAsync(string name, string url, bool pushUrlOnly = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            GitArguments.EnsureSafeRef(url, nameof(url));
            var request = Context.Request("remote", "set-url");
            request.AddIf(pushUrlOnly, "--push");
            request.Add("--").Add(name).Add(url);
            return Context.RunLockedCheckedAsync(request, cancellationToken);
        }

        /// <summary>Adds the remote, or updates its URL when it already exists.</summary>
        public async Task AddOrUpdateAsync(string name, string url, CancellationToken cancellationToken = default)
        {
            var existing = await GetAsync(name, cancellationToken).ConfigureAwait(false);
            if (existing == null)
                await AddAsync(name, url, cancellationToken).ConfigureAwait(false);
            else if (existing.FetchUrl != url)
                await SetUrlAsync(name, url, false, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Removes a remote together with its remote-tracking branches.</summary>
        public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            return Context.RunLockedCheckedAsync(Context.Request("remote", "remove", name), cancellationToken);
        }

        /// <summary>Renames a remote (remote-tracking branches and upstream settings are updated).</summary>
        public Task RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(oldName, nameof(oldName));
            GitArguments.EnsureSafeRef(newName, nameof(newName));
            return Context.RunLockedCheckedAsync(Context.Request("remote", "rename", oldName, newName), cancellationToken);
        }
    }
}
