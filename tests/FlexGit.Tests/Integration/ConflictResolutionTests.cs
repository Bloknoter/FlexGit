namespace FlexGit.Tests.Integration;

[TestClass]
public class ConflictResolutionTests
{
    private const string Path = "data/Read me.txt";

    /// <summary>main and "other" both change line 1 and line 10 of the file, so merging gives two separate hunks.</summary>
    private static async Task<GitRepository> CreateTwoHunkMergeConflictAsync(GitSandbox sandbox, string path = Path, string lineBreak = "\n")
    {
        string Lines(params string[] lines) => string.Join(lineBreak, lines) + lineBreak;

        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, path, Lines("1", "2", "3", "4", "5", "6", "7", "8", "9", "end"), "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, path, Lines("one", "2", "3", "4", "5", "6", "7", "8", "9", "END"), "Theirs");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, path, Lines("uno", "2", "3", "4", "5", "6", "7", "8", "9", "FIN"), "Ours");

        var merge = await repo.MergeAsync("other");
        Assert.AreEqual(MergeStatus.Conflicts, merge.Status, merge.Message);
        return repo;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetFile_TwoHunkMergeConflict_ReturnsHunksAndLabels()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);

        var file = await repo.Conflicts.GetFileAsync(Path);

        Assert.AreEqual(Path, file.Path);
        Assert.AreEqual(ConflictKind.BothModified, file.Kind);
        Assert.IsTrue(file.ExistsInWorkingTree);
        Assert.IsFalse(file.IsBinary);
        Assert.AreEqual(2, file.Hunks.Count);
        Assert.AreEqual("HEAD", file.OursLabel);
        Assert.AreEqual("other", file.TheirsLabel);
        Assert.AreEqual("uno\n", file.Hunks[0].Ours);
        Assert.AreEqual("one\n", file.Hunks[0].Theirs);
        Assert.AreEqual("FIN\n", file.Hunks[1].Ours);
        Assert.AreEqual("END\n", file.Hunks[1].Theirs);
        Assert.AreEqual("Use HEAD then other", file.Hunks[1].GetChoiceLabel(ConflictChoice.OursThenTheirs));
        Assert.AreEqual("2\n3\n4\n5\n6\n7\n8\n9\n", file.Segments[1].Text);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Resolve_HunksOneByOne_StagesWhenDoneAndMergeConcludes()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);

        var first = await repo.Conflicts.ResolveAsync(await repo.Conflicts.GetFileAsync(Path), [new ConflictHunkResolution(0, ConflictChoice.Ours)]);

        Assert.AreEqual(ConflictResolutionStatus.PartiallyResolved, first.Status, first.Message);
        Assert.AreEqual(1, first.RemainingHunks);
        Assert.AreEqual(ConflictKind.BothModified, (await repo.Conflicts.ListAsync()).Single().Conflict, "still unmerged");

        var file = await repo.Conflicts.GetFileAsync(Path);
        Assert.AreEqual(1, file.Hunks.Count);
        Assert.AreEqual("FIN\n", file.Hunks[0].Ours);

        var second = await repo.Conflicts.ResolveAsync(file, [new ConflictHunkResolution(0, ConflictChoice.TheirsThenOurs)]);

        Assert.AreEqual(ConflictResolutionStatus.Resolved, second.Status, second.Message);
        Assert.AreEqual(0, second.RemainingHunks);
        Assert.AreEqual(0, (await repo.Conflicts.ListAsync()).Count);
        Assert.AreEqual("uno\n2\n3\n4\n5\n6\n7\n8\n9\nEND\nFIN\n", GitSandbox.ReadFile(repo.WorkingDirectory, Path));

        var merged = await repo.ContinueMergeAsync();
        Assert.AreEqual(MergeStatus.Merged, merged.Status, merged.Message);
        Assert.AreEqual("uno\n2\n3\n4\n5\n6\n7\n8\n9\nEND\nFIN\n", await sandbox.GitAsync(repo.WorkingDirectory, "show", "HEAD:" + Path));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Resolve_WithoutStaging_LeavesFileUnmerged()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);

        var result = await repo.Conflicts.ResolveAsync(await repo.Conflicts.GetFileAsync(Path), ConflictChoice.Theirs, stageWhenResolved: false);

        Assert.AreEqual(ConflictResolutionStatus.Resolved, result.Status, result.Message);
        Assert.AreEqual("one\n2\n3\n4\n5\n6\n7\n8\n9\nEND\n", GitSandbox.ReadFile(repo.WorkingDirectory, Path));
        Assert.AreEqual(1, (await repo.Conflicts.ListAsync()).Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Resolve_FileChangedSinceRead_WritesNothing()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);
        var file = await repo.Conflicts.GetFileAsync(Path);
        var edited = GitSandbox.ReadFile(repo.WorkingDirectory, Path) + "edited elsewhere\n";
        GitSandbox.WriteFile(repo, Path, edited);

        var result = await repo.Conflicts.ResolveAsync(file, ConflictChoice.Ours);

        Assert.AreEqual(ConflictResolutionStatus.FileChanged, result.Status, result.Message);
        Assert.AreEqual(edited, GitSandbox.ReadFile(repo.WorkingDirectory, Path));
        Assert.AreEqual(1, (await repo.Conflicts.ListAsync()).Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Resolve_InvalidRequests_Throw()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);
        var file = await repo.Conflicts.GetFileAsync(Path);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => repo.Conflicts.ResolveAsync(file, []));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => repo.Conflicts.ResolveAsync(file, ConflictChoice.Custom));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => repo.Conflicts.ResolveAsync(file, [new ConflictHunkResolution(5, ConflictChoice.Ours)]));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Resolve_CrLfFile_KeepsCrLf()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox, lineBreak: "\r\n");
        var file = await repo.Conflicts.GetFileAsync(Path);
        Assert.AreEqual("uno\r\n", file.Hunks[0].Ours);

        var result = await repo.Conflicts.ResolveAsync(file, [new ConflictHunkResolution(0, ConflictChoice.OursThenTheirs), ConflictHunkResolution.Custom(1, "last")]);

        Assert.AreEqual(ConflictResolutionStatus.Resolved, result.Status, result.Message);
        Assert.AreEqual("uno\r\none\r\n2\r\n3\r\n4\r\n5\r\n6\r\n7\r\n8\r\n9\r\nlast\r\n", GitSandbox.ReadFile(repo.WorkingDirectory, Path));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetFile_Diff3Style_ReturnsBase()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.GitAsync(repo.WorkingDirectory, "config", "merge.conflictStyle", "diff3");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, "a.txt", "theirs\n", "Theirs");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "a.txt", "ours\n", "Ours");
        await repo.MergeAsync("other");

        var hunk = (await repo.Conflicts.GetFileAsync("a.txt")).Hunks.Single();

        Assert.AreEqual("ours\n", hunk.Ours);
        Assert.AreEqual("base\n", hunk.Base);
        Assert.IsNotNull(hunk.BaseLabel);
        Assert.AreEqual("theirs\n", hunk.Theirs);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetFile_ConflictMarkerSizeAttribute_IsRespected()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, ".gitattributes", "*.md conflict-marker-size=12\n", "Attributes");
        await sandbox.CommitFileAsync(repo, "doc.md", "Title\n", "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, "doc.md", "Theirs\n", "Theirs");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "doc.md", "Ours\n=======\n", "Ours");
        await repo.MergeAsync("other");

        var file = await repo.Conflicts.GetFileAsync("doc.md");

        Assert.IsFalse(file.HasMalformedMarkers);
        Assert.AreEqual("Ours\n=======\n", file.Hunks.Single().Ours);
        Assert.AreEqual("Theirs\n", file.Hunks.Single().Theirs);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetOverview_ReportsTextBinaryAndDeleteConflicts()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "text.txt", "base\n", "Base text");
        await sandbox.CommitFileAsync(repo, "image.bin", "\0\u0001base", "Base binary");
        await sandbox.CommitFileAsync(repo, "deleted-by-us.txt", "base\n", "Base delete");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, "text.txt", "theirs\n", "Theirs text");
        await sandbox.CommitFileAsync(repo, "image.bin", "\0\u0001theirs", "Theirs binary");
        await sandbox.CommitFileAsync(repo, "deleted-by-us.txt", "modified by them\n", "Theirs modify");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "text.txt", "ours\n", "Ours text");
        await sandbox.CommitFileAsync(repo, "image.bin", "\0\u0001ours", "Ours binary");
        await sandbox.GitAsync(repo.WorkingDirectory, "rm", "--quiet", "deleted-by-us.txt");
        await sandbox.GitAsync(repo.WorkingDirectory, "commit", "--quiet", "-m", "Ours delete");
        await repo.MergeAsync("other");

        var overview = await repo.Conflicts.GetOverviewAsync();

        Assert.AreEqual(RepositoryState.Merging, overview.Operation);
        Assert.IsTrue(overview.HasConflicts);
        Assert.AreEqual(3, overview.Files.Count);
        Assert.AreEqual(1, overview.TotalHunks);

        var text = overview.Files.Single(f => f.Path == "text.txt");
        Assert.AreEqual(1, text.HunkCount);
        Assert.IsTrue(text.CanResolveHunks);
        Assert.IsFalse(text.IsBinary);

        var binary = overview.Files.Single(f => f.Path == "image.bin");
        Assert.IsTrue(binary.IsBinary);
        Assert.AreEqual(0, binary.HunkCount);
        Assert.IsFalse(binary.CanResolveHunks);

        var deleted = overview.Files.Single(f => f.Path == "deleted-by-us.txt");
        Assert.AreEqual(ConflictKind.DeletedByUs, deleted.Kind);
        Assert.AreEqual(0, deleted.HunkCount);

        var notConflicted = await repo.Conflicts.GetFileAsync("missing.txt");
        Assert.AreEqual(ConflictKind.None, notConflicted.Kind);
        Assert.IsFalse(notConflicted.ExistsInWorkingTree);
        Assert.AreEqual(0, notConflicted.Hunks.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetOverview_NoConflicts_IsEmpty()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "a\n", "Initial");

        var overview = await repo.Conflicts.GetOverviewAsync();

        Assert.AreEqual(RepositoryState.None, overview.Operation);
        Assert.IsFalse(overview.HasConflicts);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RestoreConflict_AfterPartialResolve_BringsBackAllHunks()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);
        await repo.Conflicts.ResolveAsync(await repo.Conflicts.GetFileAsync(Path), [new ConflictHunkResolution(0, ConflictChoice.Ours)]);
        Assert.AreEqual(1, (await repo.Conflicts.GetFileAsync(Path)).Hunks.Count);

        await repo.Conflicts.RestoreConflictAsync([Path]);
        var restored = await repo.Conflicts.GetFileAsync(Path);
        Assert.AreEqual(2, restored.Hunks.Count);
        Assert.IsNull(restored.Hunks[0].Base);

        await repo.Conflicts.RestoreConflictAsync([Path], ConflictMarkerStyle.Diff3);
        var diff3 = await repo.Conflicts.GetFileAsync(Path);
        Assert.AreEqual(2, diff3.Hunks.Count);
        Assert.AreEqual("1\n", diff3.Hunks[0].Base);
        Assert.AreEqual("end\n", diff3.Hunks[1].Base);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RestoreConflict_PathNotConflicted_Throws()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateTwoHunkMergeConflictAsync(sandbox);

        var exception = await Assert.ThrowsExceptionAsync<GitCommandException>(() => repo.Conflicts.RestoreConflictAsync(["other.txt"]));
        Assert.AreEqual(GitErrorKind.NotFound, exception.Error.Kind);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task StashPopConflict_HunksHaveStashLabelsAndResolve()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Initial");
        GitSandbox.WriteFile(repo, "a.txt", "stashed\n");
        await repo.Stash.PushAsync();
        await sandbox.CommitFileAsync(repo, "a.txt", "committed\n", "Conflicting change");
        Assert.AreEqual(StashStatus.Conflicts, (await repo.Stash.PopAsync()).Status);

        var overview = await repo.Conflicts.GetOverviewAsync();
        Assert.AreEqual(RepositoryState.None, overview.Operation);
        var file = await repo.Conflicts.GetFileAsync("a.txt");
        Assert.AreEqual("Updated upstream", file.OursLabel);
        Assert.AreEqual("Stashed changes", file.TheirsLabel);

        var result = await repo.Conflicts.ResolveAsync(file, ConflictChoice.Theirs);

        Assert.AreEqual(ConflictResolutionStatus.Resolved, result.Status, result.Message);
        Assert.AreEqual("stashed\n", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));
        Assert.AreEqual(0, (await repo.Conflicts.ListAsync()).Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RebaseConflict_ResolvedThroughHunks_Continues()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "feature");
        await sandbox.CommitFileAsync(repo, "a.txt", "feature\n", "Feature change");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "a.txt", "main\n", "Main change");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "feature");
        Assert.AreEqual(RebaseStatus.Conflicts, (await repo.Rebase.StartAsync("main")).Status);

        var file = await repo.Conflicts.GetFileAsync("a.txt");
        StringAssert.Contains(file.TheirsLabel, "Feature change");
        var result = await repo.Conflicts.ResolveAsync(file, ConflictChoice.OursThenTheirs);
        Assert.AreEqual(ConflictResolutionStatus.Resolved, result.Status, result.Message);

        var continued = await repo.Rebase.ContinueAsync();
        Assert.AreEqual(RebaseStatus.Completed, continued.Status, continued.Message);
        Assert.AreEqual("main\nfeature\n", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));
    }
}
