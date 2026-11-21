#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Entry point of FlexGit: an async API over the git command line for one repository.
    /// All operations on the same repository are serialized; every method is safe to call from any thread.
    /// </summary>
    /// <remarks>
    /// Error model: operations with expected non-success outcomes (commit, switch, pull, push, merge, rebase,
    /// stash apply, clone, ...) return a result object with a status enum. Queries and simple ref/config
    /// manipulations throw <see cref="GitCommandException"/>. Cancellation throws <see cref="OperationCanceledException"/>
    /// after killing the git process tree.
    /// </remarks>
    public sealed partial class GitRepository
    {
        private GitRepository(GitContext context)
        {
            Context = context;
            Index = new IndexCommands(this);
            Branches = new BranchCommands(this);
            Remotes = new RemoteCommands(this);
            Tags = new TagCommands(this);
            History = new HistoryCommands(this);
            Config = new ConfigCommands(this);
            Stash = new StashCommands(this);
            Conflicts = new ConflictCommands(this);
            Rebase = new RebaseCommands(this);
            Lfs = new LfsCommands(this);
            Diff = new DiffCommands(this);
        }

        internal GitContext Context { get; }

        /// <summary>Root of the working tree (equals <see cref="GitDirectory"/> for bare repositories).</summary>
        public string WorkingDirectory => Context.WorkingDirectory;

        /// <summary>The .git directory.</summary>
        public string GitDirectory => Context.GitDirectory;

        public bool IsBare => Context.IsBare;

        /// <summary>The git executable used by this repository.</summary>
        public GitInstallation Git => Context.Runner.Installation;

        /// <summary>Options this repository was opened with.</summary>
        public GitOptions Options => Context.Options;

        /// <summary>Staging, unstaging and discarding changes.</summary>
        public IndexCommands Index { get; }

        /// <summary>Listing, creating, switching, renaming and deleting branches.</summary>
        public BranchCommands Branches { get; }

        /// <summary>Managing remotes.</summary>
        public RemoteCommands Remotes { get; }

        /// <summary>Listing, creating, pushing and deleting tags.</summary>
        public TagCommands Tags { get; }

        /// <summary>Commit log and file contents at a revision.</summary>
        public HistoryCommands History { get; }

        /// <summary>Reading and writing git configuration.</summary>
        public ConfigCommands Config { get; }

        /// <summary>Stashing changes.</summary>
        public StashCommands Stash { get; }

        /// <summary>Inspecting and resolving merge/rebase conflicts.</summary>
        public ConflictCommands Conflicts { get; }

        /// <summary>Rebasing the current branch.</summary>
        public RebaseCommands Rebase { get; }

        /// <summary>Git LFS: tracking patterns and file locking.</summary>
        public LfsCommands Lfs { get; }

        /// <summary>Line-by-line differences of changed files.</summary>
        public DiffCommands Diff { get; }

        /// <summary>Opens the repository containing <paramref name="path"/> (the path may be any folder inside it).</summary>
        /// <exception cref="GitCommandException">git is missing/too old, or the path is not inside a repository.</exception>
        public static async Task<GitRepository> OpenAsync(string path, GitOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));

            options ??= new GitOptions();
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                throw new GitCommandException(new GitError(GitErrorKind.NotARepository, $"Directory '{fullPath}' does not exist."));

            var installation = await GitLocator.LocateAsync(options, cancellationToken).ConfigureAwait(false);
            var runner = new GitRunner(installation, options);

            var info = await runner.RunAsync(
                new GitRunRequest("rev-parse", "--is-bare-repository", "--absolute-git-dir") { WorkingDirectory = fullPath, ReadOnly = true },
                cancellationToken).ConfigureAwait(false);
            info.EnsureSuccess();

            var lines = new List<string>(RefParser.SplitLines(info.StandardOutput));
            if (lines.Count < 2)
                throw new GitCommandException(new GitError(GitErrorKind.Unknown, $"Unexpected rev-parse output: '{info.StandardOutput}'", info.ExitCode, info.StandardError, info.CommandLine));

            var isBare = lines[0] == "true";
            var gitDirectory = Path.GetFullPath(lines[1]);
            var workingDirectory = gitDirectory;
            if (!isBare)
            {
                var top = await runner.RunAsync(
                    new GitRunRequest("rev-parse", "--show-toplevel") { WorkingDirectory = fullPath, ReadOnly = true },
                    cancellationToken).ConfigureAwait(false);
                top.EnsureSuccess();
                workingDirectory = Path.GetFullPath(top.StandardOutput.Trim());
            }

            return new GitRepository(new GitContext(runner, workingDirectory, gitDirectory, isBare));
        }

        /// <summary>True when <paramref name="path"/> is inside a git repository.</summary>
        public static async Task<bool> IsRepositoryAsync(string path, GitOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            options ??= new GitOptions();
            var installation = await GitLocator.LocateAsync(options, cancellationToken).ConfigureAwait(false);
            var runner = new GitRunner(installation, options);
            var result = await runner.RunAsync(
                new GitRunRequest("rev-parse", "--git-dir") { WorkingDirectory = Path.GetFullPath(path), ReadOnly = true },
                cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }

        /// <summary>Creates (or reinitializes) a repository in <paramref name="path"/>.</summary>
        public static async Task<GitRepository> InitAsync(string path, string initialBranch = "main", bool bare = false,
            GitOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            GitArguments.EnsureSafeRef(initialBranch, nameof(initialBranch));

            options ??= new GitOptions();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);

            var installation = await GitLocator.LocateAsync(options, cancellationToken).ConfigureAwait(false);
            var runner = new GitRunner(installation, options);
            var request = new GitRunRequest("init", "--quiet", "--initial-branch=" + initialBranch) { WorkingDirectory = fullPath };
            request.AddIf(bare, "--bare");
            (await runner.RunAsync(request, cancellationToken).ConfigureAwait(false)).EnsureSuccess();

            return await OpenAsync(fullPath, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads branch information and changed paths (<c>git status</c>).</summary>
        public async Task<RepositoryStatus> GetStatusAsync(StatusOptions? options = null, CancellationToken cancellationToken = default)
        {
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await Context.ReadStatusAsync(options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Returns the merge/rebase/... operation in progress (cheap: file checks only).</summary>
        public RepositoryState GetState() => RepositoryStateDetector.Detect(GitDirectory);

        /// <summary>Resolves a revision (branch, tag, "HEAD~1", ...) to a commit sha, or null if it doesn't exist.</summary>
        public async Task<string?> ResolveRevisionAsync(string revision, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(revision, nameof(revision));
            using (await Context.LockAsync(cancellationToken).ConfigureAwait(false))
                return await Context.TryRevParseAsync(revision, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Converts an absolute or repository-relative path to a repository-relative path with '/'.</summary>
        public string ToRepositoryPath(string path) => Context.ToRepositoryPath(path);

        /// <summary>Converts a repository-relative path (as reported by status/log) to an absolute path.</summary>
        public string GetFullPath(string repositoryPath) =>
            Path.GetFullPath(Path.Combine(WorkingDirectory, repositoryPath.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>
        /// Runs any git command in the repository root: the escape hatch for everything FlexGit doesn't wrap.
        /// Never throws for a non-zero exit code; inspect <see cref="GitResult.Succeeded"/>.
        /// </summary>
        public Task<GitResult> RunRawAsync(params string[] arguments) => RunRawAsync(arguments, null, CancellationToken.None);

        /// <inheritdoc cref="RunRawAsync(string[])"/>
        public async Task<GitResult> RunRawAsync(IEnumerable<string> arguments, RawCommandOptions? options, CancellationToken cancellationToken = default)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            options ??= new RawCommandOptions();

            var request = new GitRunRequest(arguments)
            {
                WorkingDirectory = WorkingDirectory,
                Progress = options.Progress,
                LiteralPathspecs = options.LiteralPathspecs,
                Environment = options.Environment,
                StandardInput = options.StandardInputBytes ?? (options.StandardInput != null ? GitArguments.Utf8(options.StandardInput) : null)
            };

            if (!options.LockRepository)
                return await Context.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public override string ToString() => WorkingDirectory;
    }

    /// <summary>Options for <see cref="GitRepository.RunRawAsync(IEnumerable{string}, RawCommandOptions, CancellationToken)"/>.</summary>
    public sealed class RawCommandOptions
    {
        /// <summary>Text written to stdin (UTF-8).</summary>
        public string? StandardInput { get; set; }

        /// <summary>Raw bytes written to stdin (takes precedence over <see cref="StandardInput"/>).</summary>
        public byte[]? StandardInputBytes { get; set; }

        /// <summary>Receives progress parsed from stderr.</summary>
        public IProgress<GitProgress>? Progress { get; set; }

        /// <summary>Serialize with other FlexGit operations on this repository (default true).</summary>
        public bool LockRepository { get; set; } = true;

        /// <summary>Treat pathspecs literally instead of as globs (default false, like plain git).</summary>
        public bool LiteralPathspecs { get; set; }

        /// <summary>Extra environment variables for this command.</summary>
        public IDictionary<string, string?>? Environment { get; set; }
    }
}
