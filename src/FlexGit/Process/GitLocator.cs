#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Finds the git executable and checks its version (cached per executable path).</summary>
    public static class GitLocator
    {
        /// <summary>
        /// Oldest supported git: needed for switch/restore, --pathspec-from-file and GIT_CONFIG_GLOBAL.
        /// </summary>
        public static readonly Version MinimumVersion = new Version(2, 32, 0);

        private static readonly Regex s_gitVersion = new Regex(@"(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);
        private static readonly Regex s_lfsVersion = new Regex(@"git-lfs/(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, Lazy<Task<GitInstallation>>> s_cache =
            new ConcurrentDictionary<string, Lazy<Task<GitInstallation>>>(StringComparer.OrdinalIgnoreCase);

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Locates git, verifies the minimum version and probes git-lfs.</summary>
        /// <exception cref="GitCommandException">Git is missing (<see cref="GitErrorKind.GitNotFound"/>) or too old.</exception>
        public static async Task<GitInstallation> LocateAsync(GitOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            options ??= new GitOptions();

            var path = FindExecutable(options.GitExecutablePath);
            if (path == null)
            {
                var message = options.GitExecutablePath != null
                    ? $"git executable not found at '{options.GitExecutablePath}'."
                    : "git executable not found. Install Git (https://git-scm.com) or set GitOptions.GitExecutablePath.";
                throw new GitCommandException(new GitError(GitErrorKind.GitNotFound, message));
            }

            var lazy = s_cache.GetOrAdd(path, p => new Lazy<Task<GitInstallation>>(() => ProbeAsync(p, options)));
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                // Don't cache failures (git may get installed/updated while the application is running).
                s_cache.TryRemove(path, out _);
                throw;
            }
        }

        /// <summary>Returns the full path of the git executable, or null when none is found.</summary>
        public static string? FindExecutable(string? explicitPath = null)
        {
            if (!string.IsNullOrEmpty(explicitPath))
                return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

            var fileName = IsWindows ? "git.exe" : "git";
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in pathVariable.Split(Path.PathSeparator))
            {
                var trimmed = directory.Trim().Trim('"');
                if (trimmed.Length == 0)
                    continue;
                try
                {
                    var candidate = Path.Combine(trimmed, fileName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch (ArgumentException)
                {
                    // Invalid characters in a PATH entry: skip it.
                }
            }

            foreach (var candidate in GetWellKnownLocations())
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>Forgets cached probe results (e.g. after the user updated git).</summary>
        public static void ClearCache() => s_cache.Clear();

        private static IEnumerable<string> GetWellKnownLocations()
        {
            if (IsWindows)
            {
                foreach (var root in new[]
                         {
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                         })
                {
                    if (!string.IsNullOrEmpty(root))
                        yield return Path.Combine(root, "Git", "cmd", "git.exe");
                }
            }
            else
            {
                // GUI apps on macOS don't inherit the shell PATH.
                yield return "/usr/bin/git";
                yield return "/usr/local/bin/git";
                yield return "/opt/homebrew/bin/git";
            }
        }

        private static async Task<GitInstallation> ProbeAsync(string path, GitOptions options)
        {
            var bootstrap = new GitInstallation(path, MinimumVersion, string.Empty, null);
            var runner = new GitRunner(bootstrap, options);

            var versionResult = await runner.RunAsync(new GitRunRequest("--version"), CancellationToken.None).ConfigureAwait(false);
            if (!versionResult.Succeeded)
                throw new GitCommandException(new GitError(GitErrorKind.GitNotFound,
                    $"'{path} --version' failed: {versionResult.StandardError.Trim()}", versionResult.ExitCode, versionResult.StandardError, versionResult.CommandLine));

            var versionText = versionResult.StandardOutput.Trim();
            var version = ParseVersion(s_gitVersion.Match(versionText))
                          ?? throw new GitCommandException(new GitError(GitErrorKind.GitNotFound, $"Unrecognized git version output: '{versionText}'."));

            if (version < MinimumVersion)
                throw new GitCommandException(new GitError(GitErrorKind.GitTooOld,
                    $"git {version} is too old; FlexGit requires git {MinimumVersion} or newer ({path})."));

            Version? lfsVersion = null;
            var lfsResult = await runner.RunAsync(new GitRunRequest("lfs", "version"), CancellationToken.None).ConfigureAwait(false);
            if (lfsResult.Succeeded)
                lfsVersion = ParseVersion(s_lfsVersion.Match(lfsResult.StandardOutput));

            var installation = new GitInstallation(path, version, versionText, lfsVersion);
            options.Log(GitLogLevel.Info, $"FlexGit: using {installation}");
            return installation;
        }

        internal static Version? ParseVersion(Match match)
        {
            if (!match.Success)
                return null;
            var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(major, minor, patch);
        }
    }
}
