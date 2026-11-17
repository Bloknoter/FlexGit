using System.Text;

namespace FlexGit.Tests.Integration;

[TestClass]
public class RepositoryReadTests
{
    [TestMethod]
    [Timeout(60_000)]
    public async Task InitAndOpen_ResolveRootFromSubfolder()
    {
        using var sandbox = GitSandbox.Create();
        var created = await sandbox.InitRepositoryAsync("repo");
        Directory.CreateDirectory(Path.Combine(created.WorkingDirectory, "data", "Scripts"));

        var opened = await GitRepository.OpenAsync(Path.Combine(created.WorkingDirectory, "data", "Scripts"), sandbox.Options);

        Assert.AreEqual(Path.GetFullPath(sandbox.PathOf("repo")), opened.WorkingDirectory, ignoreCase: true);
        Assert.AreEqual(Path.Combine(opened.WorkingDirectory, ".git"), opened.GitDirectory, ignoreCase: true);
        Assert.IsFalse(opened.IsBare);
        Assert.IsTrue(await GitRepository.IsRepositoryAsync(opened.WorkingDirectory, sandbox.Options));
        Assert.IsFalse(await GitRepository.IsRepositoryAsync(sandbox.CreateDirectory("plain"), sandbox.Options));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Open_NonRepository_Throws()
    {
        using var sandbox = GitSandbox.Create();
        var exception = await Assert.ThrowsExceptionAsync<GitCommandException>(
            () => GitRepository.OpenAsync(sandbox.CreateDirectory("plain"), sandbox.Options));
        Assert.AreEqual(GitErrorKind.NotARepository, exception.Kind);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Status_ReportsUnbornThenChanges()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");

        var empty = await repo.GetStatusAsync();
        Assert.AreEqual("main", empty.Branch);
        Assert.IsTrue(empty.IsUnborn);
        Assert.IsTrue(empty.IsClean);
        Assert.AreEqual(RepositoryState.None, empty.State);

        GitSandbox.WriteFile(repo, "data/Texture [1].png", "png");
        GitSandbox.WriteFile(repo, "data/Сцена.txt", "scene");
        var untracked = await repo.GetStatusAsync();
        CollectionAssert.AreEquivalent(
            new[] { "data/Texture [1].png", "data/Сцена.txt" },
            untracked.Untracked.Select(e => e.Path).ToArray());

        await sandbox.CommitFileAsync(repo, "readme.md", "hello", "Initial commit");
        await sandbox.GitAsync(repo.WorkingDirectory, "add", "--", "data/Сцена.txt");
        GitSandbox.WriteFile(repo, "readme.md", "changed");

        var changed = await repo.GetStatusAsync(new StatusOptions { UntrackedFiles = UntrackedFilesMode.No });
        Assert.IsFalse(changed.IsUnborn);
        Assert.AreEqual(2, changed.Entries.Count);
        Assert.IsTrue(changed.Entries.Single(e => e.Path == "data/Сцена.txt").IsStaged);
        Assert.IsTrue(changed.Entries.Single(e => e.Path == "readme.md").HasUnstagedChanges);

        var filtered = await repo.GetStatusAsync(new StatusOptions { Paths = [Path.Combine(repo.WorkingDirectory, "readme.md")] });
        Assert.AreEqual("readme.md", filtered.Entries.Single().Path);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task BranchesAndHistory_AreListed()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        Assert.IsNull(await repo.Branches.GetCurrentAsync(), "unborn branch is not listed");
        Assert.AreEqual("main", await repo.Branches.GetCurrentNameAsync());
        Assert.AreEqual(0, (await repo.History.GetLogAsync()).Count);

        var first = await sandbox.CommitFileAsync(repo, "a.txt", "1", "First");
        await repo.Branches.CreateAsync("feature/x");
        var second = await sandbox.CommitFileAsync(repo, "a.txt", "2", "Second\n\nWith body");

        var branches = await repo.Branches.ListAsync();
        CollectionAssert.AreEquivalent(new[] { "main", "feature/x" }, branches.Select(b => b.Name).ToArray());
        Assert.AreEqual(second, branches.Single(b => b.IsCurrent).TipSha);
        Assert.AreEqual(first, branches.Single(b => b.Name == "feature/x").TipSha);
        Assert.IsTrue(await repo.Branches.ExistsAsync("feature/x"));
        Assert.IsFalse(await repo.Branches.ExistsAsync("nope"));

        var log = await repo.History.GetLogAsync();
        Assert.AreEqual(2, log.Count);
        Assert.AreEqual("Second", log[0].Subject);
        Assert.AreEqual("With body", log[0].Body);
        Assert.AreEqual("FlexGit Tests", log[0].Author.Name);
        CollectionAssert.AreEqual(new[] { first }, log[0].ParentShas.ToArray());

        var paged = await repo.History.GetLogAsync(new LogQuery { Skip = 1, MaxCount = 1 });
        Assert.AreEqual(first, paged.Single().Sha);

        var ofBranch = await repo.History.GetLogAsync(new LogQuery { Revision = "feature/x" });
        Assert.AreEqual(first, ofBranch.Single().Sha);

        Assert.AreEqual(first, (await repo.History.GetCommitAsync("HEAD~1"))!.Sha);
        Assert.IsNull(await repo.History.GetCommitAsync("does-not-exist"));
        Assert.AreEqual(second, await repo.ResolveRevisionAsync("main"));

        var content = await repo.History.ReadFileAsync("HEAD~1", "a.txt");
        Assert.AreEqual("1", Encoding.UTF8.GetString(content));

        await repo.Branches.RenameAsync("feature/x", "feature/y");
        await repo.Branches.DeleteAsync("feature/y", force: true);
        Assert.AreEqual(1, (await repo.Branches.ListAsync()).Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Tags_CreateListDelete()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        var first = await sandbox.CommitFileAsync(repo, "a.txt", "1", "First");
        var second = await sandbox.CommitFileAsync(repo, "a.txt", "2", "Second");

        await repo.Tags.CreateAsync("light", target: first);
        await repo.Tags.CreateAsync("v1.0.0", message: "Release 1.0.0\n\nNotes");
        var duplicate = await Assert.ThrowsExceptionAsync<GitCommandException>(() => repo.Tags.CreateAsync("light"));
        Assert.AreEqual(GitErrorKind.AlreadyExists, duplicate.Kind);

        var tags = await repo.Tags.ListAsync();
        var light = tags.Single(t => t.Name == "light");
        Assert.IsFalse(light.IsAnnotated);
        Assert.AreEqual(first, light.TargetSha);

        var release = tags.Single(t => t.Name == "v1.0.0");
        Assert.IsTrue(release.IsAnnotated);
        Assert.AreEqual(second, release.TargetSha);
        Assert.AreEqual("Release 1.0.0", release.Message);
        Assert.AreEqual("tests@flexgit.invalid", release.Tagger!.Email);

        await repo.Tags.DeleteAsync("light");
        Assert.IsFalse(await repo.Tags.ExistsAsync("light"));
        Assert.IsTrue(await repo.Tags.ExistsAsync("v1.0.0"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RemotesAndConfig_RoundTrip()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");

        Assert.AreEqual(0, (await repo.Remotes.ListAsync()).Count);
        await repo.Remotes.AddAsync("origin", "https://example.com/a.git");
        await repo.Remotes.AddOrUpdateAsync("origin", "https://example.com/b.git");
        await repo.Remotes.AddOrUpdateAsync("backup", "C:/backup.git");
        await repo.Remotes.SetUrlAsync("backup", "C:/push.git", pushUrlOnly: true);

        var remotes = await repo.Remotes.ListAsync();
        Assert.AreEqual("https://example.com/b.git", remotes.Single(r => r.Name == "origin").FetchUrl);
        Assert.AreEqual("C:/push.git", remotes.Single(r => r.Name == "backup").PushUrl);

        await repo.Remotes.RenameAsync("backup", "mirror");
        await repo.Remotes.RemoveAsync("mirror");
        Assert.AreEqual("origin", (await repo.Remotes.ListAsync()).Single().Name);

        Assert.IsNull(await repo.Config.GetAsync("flexgit.test"));
        await repo.Config.SetAsync("flexgit.test", "-value with spaces");
        Assert.AreEqual("-value with spaces", await repo.Config.GetAsync("flexgit.test", ConfigScope.Local));
        await repo.Config.UnsetAsync("flexgit.test");
        await repo.Config.UnsetAsync("flexgit.test");
        Assert.IsNull(await repo.Config.GetAsync("flexgit.test"));

        var identity = await repo.Config.GetUserIdentityAsync();
        Assert.AreEqual("FlexGit Tests", identity!.Name);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Paths_AreConvertedToRepositoryRelative()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");

        Assert.AreEqual("data/A.png", repo.ToRepositoryPath(Path.Combine(repo.WorkingDirectory, "data", "A.png")));
        Assert.AreEqual("data/A.png", repo.ToRepositoryPath(@"data\A.png"));
        Assert.AreEqual(".", repo.ToRepositoryPath(repo.WorkingDirectory));
        Assert.ThrowsException<ArgumentException>(() => repo.ToRepositoryPath(sandbox.PathOf("elsewhere", "x.txt")));
        Assert.AreEqual(Path.Combine(repo.WorkingDirectory, "data", "A.png"), repo.GetFullPath("data/A.png"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RunRaw_ExposesAnyCommand()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");

        var ok = await repo.RunRawAsync("rev-parse", "--is-inside-work-tree");
        Assert.AreEqual("true", ok.StandardOutput.Trim());

        var failed = await repo.RunRawAsync("rev-parse", "--verify", "nope");
        Assert.IsFalse(failed.Succeeded);

        var hashed = await repo.RunRawAsync(["hash-object", "--stdin"], new RawCommandOptions { StandardInput = "hello\n" });
        Assert.AreEqual("ce013625030ba8dba906f756967f9e9ca394464a", hashed.StandardOutput.Trim());
    }
}
