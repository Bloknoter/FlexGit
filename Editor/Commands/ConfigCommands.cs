#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>git configuration. Access through <see cref="GitRepository.Config"/>.</summary>
    public sealed class ConfigCommands
    {
        private readonly GitRepository _repository;

        internal ConfigCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Reads a value (e.g. "user.name"). <paramref name="scope"/> null = the effective value from all config files.</summary>
        public async Task<string?> GetAsync(string key, ConfigScope? scope = null, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(key, nameof(key));
            var request = Context.Request("config", "-z");
            if (scope != null)
                request.Add(ScopeArgument(scope.Value));
            request.Add("--get").Add(key);
            request.ReadOnly = true;

            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 1)
                return null; // not set
            result.EnsureSuccess();
            return result.StandardOutput.TrimEnd('\0');
        }

        /// <summary>Writes a value (default: the repository's .git/config).</summary>
        public Task SetAsync(string key, string value, ConfigScope scope = ConfigScope.Local, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(key, nameof(key));
            var request = Context.Request("config", ScopeArgument(scope), "--", key, value ?? string.Empty);
            return Context.RunLockedCheckedAsync(request, cancellationToken);
        }

        /// <summary>Removes a value. Does nothing when it isn't set.</summary>
        public async Task UnsetAsync(string key, ConfigScope scope = ConfigScope.Local, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(key, nameof(key));
            var result = await Context.RunLockedAsync(Context.Request("config", ScopeArgument(scope), "--unset", key), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 5) // 5 = key not set
                result.EnsureSuccess();
        }

        /// <summary>The effective user.name/user.email, or null when either is missing.</summary>
        public async Task<UserIdentity?> GetUserIdentityAsync(CancellationToken cancellationToken = default)
        {
            var name = await GetAsync("user.name", null, cancellationToken).ConfigureAwait(false);
            var email = await GetAsync("user.email", null, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(name) || email == null ? null : new UserIdentity(name!, email);
        }

        /// <summary>Writes user.name and user.email.</summary>
        public async Task SetUserIdentityAsync(UserIdentity identity, ConfigScope scope = ConfigScope.Local, CancellationToken cancellationToken = default)
        {
            await SetAsync("user.name", identity.Name, scope, cancellationToken).ConfigureAwait(false);
            await SetAsync("user.email", identity.Email, scope, cancellationToken).ConfigureAwait(false);
        }

        private static string ScopeArgument(ConfigScope scope) => scope switch
        {
            ConfigScope.Global => "--global",
            ConfigScope.System => "--system",
            _ => "--local"
        };
    }
}
