#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Plumbing shared by <see cref="GitRepository"/> and its command groups.</summary>
    internal sealed class GitContext
    {
        private static readonly StringComparison s_pathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public GitContext(GitRunner runner, string workingDirectory, string gitDirectory, bool isBare)
        {
            Runner = runner;
            WorkingDirectory = workingDirectory;
            GitDirectory = gitDirectory;
            IsBare = isBare;
        }

        public GitRunner Runner { get; }

        public GitOptions Options => Runner.Options;

        public string WorkingDirectory { get; }

        public string GitDirectory { get; }

        public bool IsBare { get; }

        /// <summary>Serializes FlexGit operations on this repository. Hold it for multi-command operations.</summary>
        public Task<IDisposable> LockAsync(CancellationToken cancellationToken) => CommandQueue.AcquireAsync(GitDirectory, cancellationToken);

        public GitRunRequest Request(params string[] arguments) => new GitRunRequest(arguments) { WorkingDirectory = WorkingDirectory };

        /// <summary>Runs without taking the lock (caller holds it).</summary>
        public Task<GitResult> RunAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            request.WorkingDirectory ??= WorkingDirectory;
            return Runner.RunAsync(request, cancellationToken);
        }

        /// <summary>Runs without taking the lock and throws <see cref="GitCommandException"/> on failure.</summary>
        public async Task<GitResult> RunCheckedAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.EnsureSuccess();
        }

        /// <summary>Takes the lock, runs one command and throws on failure.</summary>
        public async Task<GitResult> RunLockedCheckedAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            using (await LockAsync(cancellationToken).ConfigureAwait(false))
                return await RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Takes the lock and runs one command, returning the result regardless of the exit code.</summary>
        public async Task<GitResult> RunLockedAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            using (await LockAsync(cancellationToken).ConfigureAwait(false))
                return await RunAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Converts an absolute path or a repository-relative path into a repository-relative path with '/'.
        /// Paths relative to some other folder (for example a project that is a subfolder of the repository)
        /// must be passed as absolute paths.
        /// </summary>
        public string ToRepositoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));

            if (!Path.IsPathRooted(path))
                return GitArguments.NormalizePathSeparators(path);

            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, root, s_pathComparison))
                return ".";
            var prefix = root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, s_pathComparison))
                throw new ArgumentException($"'{path}' is outside the repository '{WorkingDirectory}'.", nameof(path));
            return GitArguments.NormalizePathSeparators(full.Substring(prefix.Length));
        }

        public List<string> ToRepositoryPaths(IEnumerable<string> paths)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(ToRepositoryPath).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>Adds <c>--pathspec-from-file=- --pathspec-file-nul</c> and feeds the paths through stdin.</summary>
        public static void AddPathspecsViaStdin(GitRunRequest request, IReadOnlyCollection<string> repositoryPaths)
        {
            request.Add("--pathspec-from-file=-").Add("--pathspec-file-nul");
            request.StandardInput = GitArguments.NulSeparatedPaths(repositoryPaths.Select(GitArguments.LiteralPathspec));
        }

        public async Task<string?> TryRevParseAsync(string revision, CancellationToken cancellationToken)
        {
            var request = Request("rev-parse", "--verify", "--quiet", "--end-of-options", revision + "^{commit}");
            request.ReadOnly = true;
            var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }

        public async Task<RepositoryStatus> ReadStatusAsync(StatusOptions? options, CancellationToken cancellationToken)
        {
            options ??= new StatusOptions();
            var untracked = options.UntrackedFiles switch
            {
                UntrackedFilesMode.No => "no",
                UntrackedFilesMode.Normal => "normal",
                _ => "all"
            };

            var request = Request("status", "--porcelain=v2", "--branch", "-z", "--untracked-files=" + untracked);
            request.ReadOnly = true;
            if (options.IncludeIgnored)
                request.Add("--ignored=matching");
            if (options.Paths != null && options.Paths.Count > 0)
            {
                request.Add("--");
                request.Arguments.AddRange(ToRepositoryPaths(options.Paths).Select(GitArguments.LiteralPathspec));
            }

            var result = await RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            var status = StatusParser.Parse(result.StandardOutput);
            status.State = RepositoryStateDetector.Detect(GitDirectory);
            return status;
        }

        public async Task<List<string>> ReadRemoteNamesAsync(CancellationToken cancellationToken)
        {
            var request = Request("remote");
            request.ReadOnly = true;
            var result = await RunCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return RefParser.SplitLines(result.StandardOutput).ToList();
        }

        public async Task<string?> ReadCurrentBranchAsync(CancellationToken cancellationToken)
        {
            var request = Request("symbolic-ref", "--quiet", "--short", "HEAD");
            request.ReadOnly = true;
            var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }

        public async Task<string?> ReadConfigAsync(string key, CancellationToken cancellationToken)
        {
            var request = Request("config", "-z", "--get", key);
            request.ReadOnly = true;
            var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.TrimEnd('\0') : null;
        }
    }
}
