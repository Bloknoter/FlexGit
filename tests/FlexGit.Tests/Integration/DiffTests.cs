namespace FlexGit.Tests.Integration;

[TestClass]
public class DiffTests
{
    private const string Path = "data/Read me [1].txt";

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string[] Describe(DiffHunk hunk) =>
        hunk.Lines.Select(l => $"{l} [{l.OldLineNumber?.ToString() ?? "-"}|{l.NewLineNumber?.ToString() ?? "-"}]").ToArray();

    /// <summary>Compares joined text so a failure shows both sequences.</summary>
    private static void AssertSequence<T>(T[] expected, T[] actual) => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", actual));

    private static async Task<GitRepository> CreateRepositoryAsync(GitSandbox sandbox, string content)
    {
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, Path, content, "Base");
        return repo;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_ModifiedFile_ReturnsHunkWithLineNumbers()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("1", "2", "3", "4", "5"));
        GitSandbox.WriteFile(repo, Path, Lines("1", "2", "three", "4", "5", "6"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path);

        Assert.AreEqual(Path, diff.Path);
        Assert.IsNull(diff.OldPath);
        Assert.AreEqual(DiffChangeKind.Modified, diff.Change);
        Assert.IsFalse(diff.IsUntracked);
        Assert.IsFalse(diff.IsBinary);
        Assert.IsFalse(diff.IsLfs);
        Assert.AreEqual(10L, diff.OldSize);
        Assert.AreEqual(16L, diff.NewSize);
        var hunk = diff.Hunks.Single();
        Assert.AreEqual((1, 5, 1, 6), (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount));
        AssertSequence(
            new[] { " 1 [1|1]", " 2 [2|2]", "-3 [3|-]", "+three [-|3]", " 4 [4|4]", " 5 [5|5]", "+6 [-|6]" },
            Describe(hunk));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_ContextLines_LimitsUnchangedLines()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("1", "2", "3", "4", "5", "6", "7", "8", "9"));
        GitSandbox.WriteFile(repo, Path, Lines("1", "2", "3", "4", "five", "6", "7", "8", "9"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(repo.GetFullPath(Path), contextLines: 1);

        AssertSequence(new[] { " 4 [4|4]", "-5 [5|-]", "+five [-|5]", " 6 [6|6]" }, Describe(diff.Hunks.Single()));

        var noContext = await repo.Diff.GetWorkingTreeDiffAsync(Path, contextLines: 0);
        AssertSequence(new[] { "-5 [5|-]", "+five [-|5]" }, Describe(noContext.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_StagedAndUnstagedEdits_AreBothComparedWithHead()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("1", "2", "3", "4", "5", "6", "7", "8", "9"));
        GitSandbox.WriteFile(repo, Path, Lines("one", "2", "3", "4", "5", "6", "7", "8", "9"));
        await repo.Index.StageAsync([Path]);
        GitSandbox.WriteFile(repo, Path, Lines("one", "2", "3", "4", "5", "6", "7", "8", "nine"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path, contextLines: 0);

        Assert.AreEqual(2, diff.Hunks.Count);
        AssertSequence(new[] { "-1 [1|-]", "+one [-|1]" }, Describe(diff.Hunks[0]));
        AssertSequence(new[] { "-9 [9|-]", "+nine [-|9]" }, Describe(diff.Hunks[1]));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_UntrackedFile_IsAddedWithEveryLine()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("base"));
        GitSandbox.WriteFile(repo, "data/new file.txt", Lines("hello", "world"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/new file.txt");

        Assert.AreEqual(DiffChangeKind.Added, diff.Change);
        Assert.IsTrue(diff.IsUntracked);
        Assert.IsFalse(diff.IsBinary);
        Assert.IsNull(diff.OldSize);
        Assert.AreEqual(12L, diff.NewSize);
        AssertSequence(new[] { "+hello [-|1]", "+world [-|2]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_StagedNewFile_IsAddedAndTracked()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("base"));
        GitSandbox.WriteFile(repo, "data/new.txt", Lines("hello"));
        await repo.Index.StageAsync(["data/new.txt"]);

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/new.txt");

        Assert.AreEqual(DiffChangeKind.Added, diff.Change);
        Assert.IsFalse(diff.IsUntracked);
        Assert.IsNull(diff.OldSize);
        Assert.AreEqual(6L, diff.NewSize);
        AssertSequence(new[] { "+hello [-|1]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_DeletedFile_IsDeletedWithEveryOldLine()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a", "b"));
        File.Delete(repo.GetFullPath(Path));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path);

        Assert.AreEqual(DiffChangeKind.Deleted, diff.Change);
        Assert.AreEqual(4L, diff.OldSize);
        Assert.IsNull(diff.NewSize);
        AssertSequence(new[] { "-a [1|-]", "-b [2|-]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_StagedDeletion_IsDeleted()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a", "b"));
        await sandbox.GitAsync(repo.WorkingDirectory, "rm", "--quiet", "--", Path);

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path);

        Assert.AreEqual(DiffChangeKind.Deleted, diff.Change);
        Assert.IsFalse(diff.IsUntracked);
        AssertSequence(new[] { "-a [1|-]", "-b [2|-]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_StagedRenameWithOriginalPath_IsRenamedWithOnlyTheEdit()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("1", "2", "3", "4", "5", "6"));
        await sandbox.GitAsync(repo.WorkingDirectory, "mv", "--", Path, "data/moved.txt");
        GitSandbox.WriteFile(repo, "data/moved.txt", Lines("1", "2", "3", "4", "5", "6", "7"));
        await repo.Index.StageAsync(["data/moved.txt"]);
        var entry = (await repo.GetStatusAsync()).Entries.Single();
        Assert.AreEqual(Path, entry.OriginalPath, "status reports the staged rename");

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(entry.Path, contextLines: 0, originalPath: entry.OriginalPath);

        Assert.AreEqual(DiffChangeKind.Renamed, diff.Change);
        Assert.AreEqual("data/moved.txt", diff.Path);
        Assert.AreEqual(Path, diff.OldPath);
        Assert.AreEqual(12L, diff.OldSize);
        Assert.AreEqual(14L, diff.NewSize);
        AssertSequence(new[] { "+7 [-|7]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_BinaryFile_HasSizesAndNoHunks()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        Directory.CreateDirectory(repo.GetFullPath("data"));
        await File.WriteAllBytesAsync(repo.GetFullPath("data/image.dat"), [1, 0, 2, 3]);
        await sandbox.GitAsync(repo.WorkingDirectory, "add", "--all");
        await sandbox.GitAsync(repo.WorkingDirectory, "commit", "--quiet", "-m", "Binary");
        await File.WriteAllBytesAsync(repo.GetFullPath("data/image.dat"), [1, 0, 2, 3, 4, 5]);

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/image.dat");

        Assert.AreEqual(DiffChangeKind.Modified, diff.Change);
        Assert.IsTrue(diff.IsBinary);
        Assert.AreEqual(0, diff.Hunks.Count);
        Assert.AreEqual(4L, diff.OldSize);
        Assert.AreEqual(6L, diff.NewSize);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_UntrackedBinaryFile_IsBinary()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("base"));
        await File.WriteAllBytesAsync(repo.GetFullPath("data/image.dat"), [1, 0, 2]);

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/image.dat");

        Assert.AreEqual(DiffChangeKind.Added, diff.Change);
        Assert.IsTrue(diff.IsUntracked);
        Assert.IsTrue(diff.IsBinary);
        Assert.AreEqual(0, diff.Hunks.Count);
        Assert.AreEqual(3L, diff.NewSize);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_UntrackedFileMarkedBinaryByAttributes_IsBinary()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("base"));
        GitSandbox.WriteFile(repo, ".gitattributes", "*.asset binary\n");
        GitSandbox.WriteFile(repo, "data/level.asset", Lines("plain", "text"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/level.asset");

        Assert.IsTrue(diff.IsUntracked);
        Assert.IsTrue(diff.IsBinary);
        Assert.AreEqual(0, diff.Hunks.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_UnchangedFile_HasNoChanges()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path);

        Assert.AreEqual(DiffChangeKind.None, diff.Change);
        Assert.IsFalse(diff.HasChanges);
        Assert.IsFalse(diff.IsUntracked);
        Assert.AreEqual(0, diff.Hunks.Count);
        Assert.AreEqual(2L, diff.OldSize);
        Assert.AreEqual(2L, diff.NewSize);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_MissingPath_HasNoChangesAndNoSizes()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/nothing.txt");

        Assert.AreEqual(DiffChangeKind.None, diff.Change);
        Assert.IsNull(diff.OldSize);
        Assert.IsNull(diff.NewSize);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_BranchWithoutCommits_ComparesWithEmptyTree()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        GitSandbox.WriteFile(repo, "data/first.txt", Lines("hi"));
        await repo.Index.StageAsync(["data/first.txt"]);

        var diff = await repo.Diff.GetWorkingTreeDiffAsync("data/first.txt");

        Assert.AreEqual(DiffChangeKind.Added, diff.Change);
        Assert.IsFalse(diff.IsUntracked);
        AssertSequence(new[] { "+hi [-|1]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_AutoCrLf_OnlyTheEditedLineDiffers()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.GitAsync(repo.WorkingDirectory, "config", "core.autocrlf", "true");
        await sandbox.CommitFileAsync(repo, Path, "a\nb\nc\n", "LF in the repository");
        GitSandbox.WriteFile(repo, Path, "a\r\nB\r\nc\r\n");

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path, contextLines: 0);

        AssertSequence(new[] { "-b [2|-]", "+B [-|2]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_UserDiffConfig_DoesNotChangeTheResult()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a", "", "b"));
        foreach (var (key, value) in new[]
                 {
                     ("diff.noprefix", "true"), ("diff.mnemonicPrefix", "true"), ("color.diff", "always"),
                     ("color.ui", "always"), ("diff.external", "this-tool-does-not-exist"), ("diff.suppressBlankEmpty", "true"),
                     ("diff.context", "0"), ("diff.renames", "false")
                 })
            await sandbox.GitAsync(repo.WorkingDirectory, "config", key, value);
        GitSandbox.WriteFile(repo, Path, Lines("a", "", "B"));

        var diff = await repo.Diff.GetWorkingTreeDiffAsync(Path);

        Assert.AreEqual(Path, diff.Path);
        AssertSequence(new[] { " a [1|1]", "  [2|2]", "-b [3|-]", "+B [-|3]" }, Describe(diff.Hunks.Single()));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task GetWorkingTreeDiff_NegativeContextLines_Throws()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await CreateRepositoryAsync(sandbox, Lines("a"));

        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => repo.Diff.GetWorkingTreeDiffAsync(Path, contextLines: -1));
    }

    [TestMethod]
    [TestCategory("Lfs")]
    [Timeout(120_000)]
    public async Task GetWorkingTreeDiff_LfsFile_ReportsLfsWithRealSizesAndNoHunks()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        if (!repo.Lfs.IsInstalled)
            Assert.Inconclusive("git-lfs is not installed on this machine.");
        await repo.Lfs.EnsureInstalledAsync();
        await repo.Lfs.TrackAsync("*.bin");
        Directory.CreateDirectory(repo.GetFullPath("data"));
        await File.WriteAllBytesAsync(repo.GetFullPath("data/big.bin"), new byte[3000]);
        await File.WriteAllBytesAsync(repo.GetFullPath("data/gone.bin"), new byte[500]);
        await sandbox.GitAsync(repo.WorkingDirectory, "add", "--all");
        await sandbox.GitAsync(repo.WorkingDirectory, "commit", "--quiet", "-m", "LFS files");

        await File.WriteAllBytesAsync(repo.GetFullPath("data/big.bin"), new byte[4000]);
        File.Delete(repo.GetFullPath("data/gone.bin"));
        await File.WriteAllBytesAsync(repo.GetFullPath("data/new.bin"), new byte[700]);

        var modified = await repo.Diff.GetWorkingTreeDiffAsync("data/big.bin", contextLines: 0);
        Assert.AreEqual(DiffChangeKind.Modified, modified.Change);
        Assert.IsTrue(modified.IsLfs);
        Assert.AreEqual(0, modified.Hunks.Count, "pointer lines are not shown");
        Assert.AreEqual(3000L, modified.OldSize);
        Assert.AreEqual(4000L, modified.NewSize);

        var deleted = await repo.Diff.GetWorkingTreeDiffAsync("data/gone.bin");
        Assert.AreEqual(DiffChangeKind.Deleted, deleted.Change);
        Assert.IsTrue(deleted.IsLfs);
        Assert.AreEqual(500L, deleted.OldSize);
        Assert.IsNull(deleted.NewSize);

        var untracked = await repo.Diff.GetWorkingTreeDiffAsync("data/new.bin");
        Assert.AreEqual(DiffChangeKind.Added, untracked.Change);
        Assert.IsTrue(untracked.IsUntracked);
        Assert.IsTrue(untracked.IsLfs);
        Assert.AreEqual(0, untracked.Hunks.Count);
        Assert.AreEqual(700L, untracked.NewSize);
    }
}
