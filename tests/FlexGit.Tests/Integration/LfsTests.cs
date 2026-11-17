using System.Text;

namespace FlexGit.Tests.Integration;

[TestClass]
[TestCategory("Lfs")]
public class LfsTests
{
    private static async Task<GitRepository> CreateLfsRepositoryAsync(GitSandbox sandbox, string name)
    {
        var repo = await sandbox.InitRepositoryAsync(name);
        if (!repo.Lfs.IsInstalled)
            Assert.Inconclusive("git-lfs is not installed on this machine.");
        return repo;
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EnsureInstalled_TrackAndCommit_StoresPointer()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateLfsRepositoryAsync(sandbox, "repo");

        Assert.IsFalse(await repo.Lfs.IsUsedInRepositoryAsync());
        Assert.IsTrue(await repo.Lfs.EnsureInstalledAsync(), "the sandbox has no system config, so filters/hooks must be installed");
        Assert.IsFalse(await repo.Lfs.EnsureInstalledAsync(), "second call is a no-op");

        await repo.Lfs.TrackAsync("*.bin");
        StringAssert.Contains(GitSandbox.ReadFile(repo.WorkingDirectory, ".gitattributes"), "*.bin filter=lfs");
        Assert.IsTrue(await repo.Lfs.IsUsedInRepositoryAsync());

        var payload = new byte[64 * 1024];
        new Random(42).NextBytes(payload);
        Directory.CreateDirectory(repo.GetFullPath("data"));
        await File.WriteAllBytesAsync(repo.GetFullPath("data/big.bin"), payload);
        await repo.Index.StageAllAsync();
        var commit = await repo.CommitAsync("Add LFS file");
        Assert.AreEqual(CommitStatus.Committed, commit.Status, commit.Message);

        var stored = await repo.History.ReadFileAsync("HEAD", "data/big.bin", applyFilters: false);
        StringAssert.StartsWith(Encoding.ASCII.GetString(stored), "version https://git-lfs.github.com/spec/v1", "the commit must contain a pointer, not the binary");

        var smudged = await repo.History.ReadFileAsync("HEAD", "data/big.bin");
        CollectionAssert.AreEqual(payload, smudged, "ReadFileAsync applies the LFS smudge filter by default");

        await repo.Lfs.UntrackAsync("*.bin");
        Assert.IsFalse(GitSandbox.ReadFile(repo.WorkingDirectory, ".gitattributes").Contains("*.bin filter=lfs"));
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task PushAndClone_TransfersRealContent()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateLfsRepositoryAsync(sandbox, "alice");
        await sandbox.GitAsync(sandbox.Root, "lfs", "install", "--skip-repo"); // filters into the sandbox's global config (needed for clone)
        var remote = await sandbox.InitBareRemoteAsync();
        await repo.Remotes.AddAsync("origin", remote);
        await repo.Lfs.EnsureInstalledAsync();
        await repo.Lfs.TrackAsync("*.psd");

        var payload = Encoding.UTF8.GetBytes(new string('x', 10_000));
        await File.WriteAllBytesAsync(repo.GetFullPath("art.psd"), payload);
        await repo.Index.StageAllAsync();
        await repo.CommitAsync("Add art");

        var pushed = await repo.PushAsync();
        if (!pushed.Succeeded && pushed.Error?.StandardError.Contains("lfs", StringComparison.OrdinalIgnoreCase) == true)
            Assert.Inconclusive("git-lfs cannot upload to a local bare remote on this machine: " + pushed.Message);
        Assert.AreEqual(PushStatus.Pushed, pushed.Status, pushed.Message);

        var cloned = await GitRepository.CloneAsync(remote, sandbox.PathOf("bob"), options: sandbox.Options);
        Assert.AreEqual(CloneStatus.Cloned, cloned.Status, cloned.Message);
        CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(cloned.Repository!.GetFullPath("art.psd")),
            "the clone must contain the real file, not the LFS pointer");
    }

    [TestMethod]
    public void LockParser_ParsesPlainAndVerifiedLists()
    {
        var plain = LfsLockParser.ParseList("""
            [{"id":"12","path":"data/hero.psd","owner":{"name":"Jane"},"locked_at":"2026-11-14T10:00:00Z"}]
            """);
        Assert.AreEqual(1, plain.Count);
        Assert.AreEqual("12", plain[0].Id);
        Assert.AreEqual("data/hero.psd", plain[0].Path);
        Assert.AreEqual("Jane", plain[0].OwnerName);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), plain[0].LockedAt);
        Assert.IsNull(plain[0].IsOwnedByCurrentUser);

        var verified = LfsLockParser.ParseList("""
            {"ours":[{"id":"1","path":"a.psd","owner":{"name":"me"},"locked_at":"2026-11-14T10:00:00Z"}],
             "theirs":[{"id":"2","path":"b \"quoted\" é.psd","owner":{"name":"you"},"locked_at":"2026-11-14T10:00:00Z"}]}
            """);
        Assert.IsTrue(verified.Single(l => l.Id == "1").IsOwnedByCurrentUser!.Value);
        var theirs = verified.Single(l => l.Id == "2");
        Assert.IsFalse(theirs.IsOwnedByCurrentUser!.Value);
        Assert.AreEqual("b \"quoted\" é.psd", theirs.Path);

        Assert.AreEqual(0, LfsLockParser.ParseList("").Count);
        Assert.AreEqual("7", LfsLockParser.ParseSingle("""{"id":"7","path":"x","owner":{"name":"n"},"locked_at":"2026-01-01T00:00:00Z"}""")!.Id);
    }

    [TestMethod]
    public void JsonReader_HandlesNumbersBoolsAndNesting()
    {
        var value = (Dictionary<string, object?>)JsonReader.Parse("""{"a":[1,2.5,-3e2],"b":true,"c":null,"d":{"e":"f"}}""")!;
        CollectionAssert.AreEqual(new object[] { 1.0, 2.5, -300.0 }, (List<object?>)value["a"]!);
        Assert.AreEqual(true, value["b"]);
        Assert.IsNull(value["c"]);
        Assert.AreEqual("f", ((Dictionary<string, object?>)value["d"]!)["e"]);
        Assert.ThrowsException<FormatException>(() => JsonReader.Parse("{\"a\":"));
    }
}
