using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FlexGit.Tests.Integration;

[TestClass]
public class GitRunnerTests
{
    [TestMethod]
    [Timeout(60_000)]
    public async Task LocateAsync_FindsSupportedGit()
    {
        using var sandbox = GitSandbox.Create();
        var git = await GitLocator.LocateAsync(sandbox.Options);

        Assert.IsTrue(File.Exists(git.ExecutablePath), git.ExecutablePath);
        Assert.IsTrue(git.Version >= GitLocator.MinimumVersion, git.VersionText);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task StandardInput_IsPassedAsUtf8WithoutBom()
    {
        using var sandbox = GitSandbox.Create();
        var runner = await sandbox.GetRunnerAsync();

        var ascii = await runner.RunAsync(new GitRunRequest("hash-object", "--stdin")
        {
            WorkingDirectory = sandbox.Root,
            StandardInput = Encoding.UTF8.GetBytes("hello\n")
        }, CancellationToken.None);
        Assert.AreEqual("ce013625030ba8dba906f756967f9e9ca394464a", ascii.StandardOutput.Trim());

        var unicodeBytes = new UTF8Encoding(false).GetBytes("Привет, мир ✓ — ünïcødé\n");
        var unicode = await runner.RunAsync(new GitRunRequest("hash-object", "--stdin")
        {
            WorkingDirectory = sandbox.Root,
            StandardInput = unicodeBytes
        }, CancellationToken.None);
        Assert.AreEqual(BlobSha(unicodeBytes), unicode.StandardOutput.Trim());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Arguments_SurviveQuotingUnchanged()
    {
        using var sandbox = GitSandbox.Create();
        var runner = await sandbox.GetRunnerAsync();
        string[] tricky =
        [
            "simple", "with space", "with \"quotes\"", @"back\slash", @"trailing\", @"trail space\",
            "", "ünïcødé ✓", "it's", "a!b", "tab\there", "\\\"mixed\\\\\"", "multi\nline", "%PATH%", "$HOME"
        ];

        var request = new GitRunRequest("rev-parse", "--sq-quote");
        request.Arguments.AddRange(tricky);
        request.WorkingDirectory = sandbox.Root;
        var result = await runner.RunAsync(request, CancellationToken.None);

        result.EnsureSuccess();
        var expected = string.Concat(tricky.Select(a => " '" + a.Replace("'", "'\\''").Replace("!", "'\\!'") + "'"));
        Assert.AreEqual(expected, result.StandardOutput.TrimEnd('\n'));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Failure_IsClassified()
    {
        using var sandbox = GitSandbox.Create();
        var runner = await sandbox.GetRunnerAsync();

        var result = await runner.RunAsync(new GitRunRequest("rev-parse", "--git-dir") { WorkingDirectory = sandbox.Root }, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GitErrorKind.NotARepository, result.ToError().Kind);
        var exception = Assert.ThrowsException<GitCommandException>(() => result.EnsureSuccess());
        Assert.AreEqual(GitErrorKind.NotARepository, exception.Kind);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Cancellation_KillsTheProcessTree()
    {
        using var sandbox = GitSandbox.Create();
        var runner = await sandbox.GetRunnerAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        var stopwatch = Stopwatch.StartNew();

        // A shell alias spawns git -> sh -> sleep, so this also checks that children die.
        var request = new GitRunRequest("-c", "alias.slow=!sleep 30", "slow") { WorkingDirectory = sandbox.Root };
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => runner.RunAsync(request, cts.Token));

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Cancellation took {stopwatch.Elapsed}");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task MissingWorkingDirectory_ThrowsNotFound()
    {
        using var sandbox = GitSandbox.Create();
        var runner = await sandbox.GetRunnerAsync();

        var exception = await Assert.ThrowsExceptionAsync<GitCommandException>(() =>
            runner.RunAsync(new GitRunRequest("status") { WorkingDirectory = sandbox.PathOf("missing") }, CancellationToken.None));
        Assert.AreEqual(GitErrorKind.NotFound, exception.Kind);
    }

    private static string BlobSha(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var hash = SHA1.HashData([.. header, .. content]);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
