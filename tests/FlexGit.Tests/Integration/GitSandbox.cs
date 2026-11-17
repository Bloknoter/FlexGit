using System.Text;

namespace FlexGit.Tests.Integration;

/// <summary>
/// A throw-away directory with an isolated git configuration:
/// GIT_CONFIG_GLOBAL points to a sandbox file and the system config is ignored,
/// so tests never depend on (or modify) the developer's or CI machine's git setup.
/// </summary>
public sealed class GitSandbox : IDisposable
{
    private GitRunner? _runner;

    private GitSandbox(string root)
    {
        Root = root;
        GlobalConfigPath = Path.Combine(root, "global.gitconfig");
        File.WriteAllText(GlobalConfigPath, """
            [user]
            	name = FlexGit Tests
            	email = tests@flexgit.invalid
            [init]
            	defaultBranch = main
            [core]
            	autocrlf = false
            	safecrlf = false
            [commit]
            	gpgsign = false
            [tag]
            	gpgsign = false
            [advice]
            	defaultBranchName = false
            [safe]
            	directory = *
            """.Replace("\r\n", "\n"));

        Options = new GitOptions();
        Options.Environment["GIT_CONFIG_GLOBAL"] = GlobalConfigPath;
        Options.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        Options.Environment["GIT_CEILING_DIRECTORIES"] = root;
        Options.Logger = (level, message) => System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
    }

    public string Root { get; }

    public string GlobalConfigPath { get; }

    public GitOptions Options { get; }

    public static GitSandbox Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "flexgit-tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return new GitSandbox(root);
    }

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public string CreateDirectory(string name)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(path);
        return path;
    }

    internal async Task<GitRunner> GetRunnerAsync()
    {
        if (_runner != null)
            return _runner;
        var installation = await GitLocator.LocateAsync(Options);
        return _runner = new GitRunner(installation, Options);
    }

    /// <summary>Runs raw git in <paramref name="workingDirectory"/> and fails the test if it doesn't succeed.</summary>
    public async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await RunAsync(workingDirectory, arguments);
        if (!result.Succeeded)
            Assert.Fail($"Setup command failed: {result.CommandLine}\n{result.StandardError}\n{result.StandardOutput}");
        return result.StandardOutput;
    }

    /// <summary>Runs raw git in <paramref name="workingDirectory"/> and returns the result regardless of the exit code.</summary>
    public async Task<GitResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        var runner = await GetRunnerAsync();
        return await runner.RunAsync(new GitRunRequest(arguments) { WorkingDirectory = workingDirectory }, CancellationToken.None);
    }

    /// <summary>Creates a repository through FlexGit (git init) using the sandbox's isolated configuration.</summary>
    public Task<GitRepository> InitRepositoryAsync(string name, string initialBranch = "main")
        => GitRepository.InitAsync(PathOf(name), initialBranch, bare: false, Options);

    /// <summary>Creates a bare repository to act as a remote.</summary>
    public async Task<string> InitBareRemoteAsync(string name = "remote.git")
    {
        var path = CreateDirectory(name);
        await GitAsync(path, "init", "--bare", "--quiet", "--initial-branch=main");
        return path;
    }

    /// <summary>Clones <paramref name="remotePath"/> into a new folder and opens it.</summary>
    public async Task<GitRepository> CloneRepositoryAsync(string remotePath, string name)
    {
        await GitAsync(Root, "clone", "--quiet", remotePath, name);
        return await GitRepository.OpenAsync(PathOf(name), Options);
    }

    /// <summary>Writes a file and commits it with raw git (independent of the APIs under test).</summary>
    public async Task<string> CommitFileAsync(GitRepository repository, string relativePath, string content, string message)
    {
        WriteFile(repository.WorkingDirectory, relativePath, content);
        await GitAsync(repository.WorkingDirectory, "add", "--", relativePath);
        await GitAsync(repository.WorkingDirectory, "commit", "--quiet", "-m", message);
        return (await GitAsync(repository.WorkingDirectory, "rev-parse", "HEAD")).Trim();
    }

    public static void WriteFile(GitRepository repository, string relativePath, string content)
        => WriteFile(repository.WorkingDirectory, relativePath, content);

    public static void WriteFile(string repositoryPath, string relativePath, string content)
    {
        var path = Path.Combine(repositoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public static string ReadFile(string repositoryPath, string relativePath)
        => File.ReadAllText(Path.Combine(repositoryPath, relativePath));

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!Directory.Exists(Root))
                    return;
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal); // git objects are read-only on Windows
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
