namespace FlexGit.Tests.Integration;

[TestClass]
public class LocalOperationsTests
{
    [TestMethod]
    [Timeout(60_000)]
    public async Task Index_StageUnstageDiscard_WithUnusualPaths()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "tracked.txt", "original", "Initial");

        string[] paths = ["data/Texture [1].png", "data/Сцена 2.txt", "data/-dash.txt", "data/[ab].txt"];
        foreach (var path in paths)
            GitSandbox.WriteFile(repo, path, path);
        GitSandbox.WriteFile(repo, "data/other.txt", "not staged");
        GitSandbox.WriteFile(repo, "data/a.txt", "would match the glob [ab].txt");

        await repo.Index.StageAsync(paths);
        var staged = await repo.GetStatusAsync();
        CollectionAssert.AreEquivalent(paths, staged.Staged.Select(e => e.Path).ToArray(), "pathspecs must be literal (no globs, no options)");
        Assert.IsTrue(staged.Entries.Single(e => e.Path == "data/other.txt").IsUntracked);
        Assert.IsTrue(staged.Entries.Single(e => e.Path == "data/a.txt").IsUntracked);

        await repo.Index.UnstageAsync([Path.Combine(repo.WorkingDirectory, "data", "Texture [1].png")]);
        var afterUnstage = await repo.GetStatusAsync();
        Assert.IsTrue(afterUnstage.Entries.Single(e => e.Path == "data/Texture [1].png").IsUntracked);

        await repo.Index.StageAllAsync();
        Assert.IsTrue((await repo.GetStatusAsync()).Entries.All(e => e.IsStaged));
        await repo.Index.UnstageAllAsync();
        Assert.IsTrue((await repo.GetStatusAsync()).Entries.All(e => e.IsUntracked));

        GitSandbox.WriteFile(repo, "tracked.txt", "modified");
        await repo.Index.StageAsync(["tracked.txt"]);
        GitSandbox.WriteFile(repo, "tracked.txt", "modified again");
        await repo.Index.DiscardAsync(["tracked.txt", "data/other.txt"]);
        Assert.AreEqual("original", GitSandbox.ReadFile(repo.WorkingDirectory, "tracked.txt"));
        Assert.IsTrue(File.Exists(repo.GetFullPath("data/other.txt")), "untracked files survive without deleteUntracked");

        await repo.Index.DiscardAsync(["data/other.txt"], deleteUntracked: true);
        Assert.IsFalse(File.Exists(repo.GetFullPath("data/other.txt")));
        Assert.IsTrue(File.Exists(repo.GetFullPath("data/Texture [1].png")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task DiscardAll_KeepsIgnoredFiles()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, ".gitignore", "Library/\n", "Ignore Library");
        GitSandbox.WriteFile(repo, "Library/cache.bin", "cache");
        GitSandbox.WriteFile(repo, "new.txt", "new");
        GitSandbox.WriteFile(repo, ".gitignore", "changed");

        await repo.Index.DiscardAllAsync(deleteUntracked: true);

        var afterStash = await repo.GetStatusAsync();
        Assert.IsTrue(afterStash.IsClean, string.Join(", ", afterStash.Entries));
        Assert.IsTrue(File.Exists(repo.GetFullPath("Library/cache.bin")));
        Assert.IsFalse(File.Exists(repo.GetFullPath("new.txt")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Commit_PreservesMessageAndReportsNothingToCommit()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");

        var nothing = await repo.CommitAsync("empty");
        Assert.AreEqual(CommitStatus.NothingToCommit, nothing.Status);

        GitSandbox.WriteFile(repo, "a.txt", "a");
        await repo.Index.StageAllAsync();
        const string message = "Fix \"quotes\" & ünïcødé ✓\n\n# not a comment\n- bullet $HOME %PATH%";
        var committed = await repo.CommitAsync(message, new CommitOptions { Author = new UserIdentity("Robot", "robot@example.com") });

        Assert.AreEqual(CommitStatus.Committed, committed.Status, committed.Message);
        Assert.AreEqual(committed.CommitSha, await repo.ResolveRevisionAsync("HEAD"));
        var raw = await sandbox.GitAsync(repo.WorkingDirectory, "log", "-1", "--format=%B%x00%an%x00%cn");
        var parts = raw.Split('\0');
        Assert.AreEqual(message, parts[0].TrimEnd('\n'));
        Assert.AreEqual("Robot", parts[1]);
        Assert.AreEqual("Robot", parts[2].Trim());

        var amended = await repo.CommitAsync("Amended", new CommitOptions { Amend = true });
        Assert.AreEqual(CommitStatus.Committed, amended.Status);
        Assert.AreEqual(1, (await repo.History.GetLogAsync()).Count);
        Assert.AreEqual("Amended", (await repo.History.GetLogAsync())[0].Subject);

        var emptyMessage = await repo.CommitAsync("", new CommitOptions { AllowEmpty = true });
        Assert.AreEqual(CommitStatus.Failed, emptyMessage.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Switch_CreatesBranches_AndProtectsLocalChanges()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "main", "Initial");

        var created = await repo.Branches.SwitchAsync("feature", new SwitchOptions { Create = true });
        Assert.AreEqual(SwitchStatus.Switched, created.Status, created.Message);
        Assert.AreEqual("feature", created.Branch);
        await sandbox.CommitFileAsync(repo, "a.txt", "feature", "Feature change");

        var exists = await repo.Branches.SwitchAsync("feature", new SwitchOptions { Create = true });
        Assert.AreEqual(SwitchStatus.AlreadyExists, exists.Status);

        GitSandbox.WriteFile(repo, "a.txt", "uncommitted work");
        var blocked = await repo.Branches.SwitchAsync("main");
        Assert.AreEqual(SwitchStatus.LocalChangesWouldBeOverwritten, blocked.Status);
        Assert.AreEqual("uncommitted work", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"), "local changes must never be discarded silently");
        Assert.AreEqual("feature", await repo.Branches.GetCurrentNameAsync());

        var missing = await repo.Branches.SwitchAsync("does-not-exist");
        Assert.AreEqual(SwitchStatus.NotFound, missing.Status);

        var progressReports = new List<GitProgress>();
        var forced = await repo.Branches.SwitchAsync("main", new SwitchOptions { DiscardLocalChanges = true }, new SyncProgress<GitProgress>(progressReports.Add));
        Assert.AreEqual(SwitchStatus.Switched, forced.Status);
        Assert.AreEqual("main", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));

        var detached = await repo.Branches.DetachAsync("feature");
        Assert.AreEqual(SwitchStatus.Switched, detached.Status);
        Assert.IsNull(detached.Branch);
        Assert.IsTrue((await repo.GetStatusAsync()).IsDetachedHead);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Switch_ToRemoteOnlyBranch_CreatesTrackingBranch()
    {
        using var sandbox = GitSandbox.Create();
        var remote = await sandbox.InitBareRemoteAsync();
        var alice = await sandbox.CloneRepositoryAsync(remote, "alice");
        await sandbox.CommitFileAsync(alice, "a.txt", "1", "Initial");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet", "origin", "main");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet", "origin", "main:feature");
        var bob = await sandbox.CloneRepositoryAsync(remote, "bob");

        var result = await bob.Branches.SwitchAsync("feature");

        Assert.AreEqual(SwitchStatus.Switched, result.Status, result.Message);
        var feature = (await bob.Branches.ListAsync()).Single(b => b.Name == "feature");
        Assert.AreEqual("origin/feature", feature.Upstream);
        Assert.AreEqual("origin", feature.RemoteName);
        var remoteBranches = await bob.Branches.ListAsync(BranchScope.Remote);
        CollectionAssert.AreEquivalent(new[] { "origin/main", "origin/feature" }, remoteBranches.Select(b => b.Name).ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Stash_PushListPop()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "1", "Initial");

        Assert.AreEqual(StashStatus.NothingToStash, (await repo.Stash.PushAsync()).Status);

        GitSandbox.WriteFile(repo, "a.txt", "changed");
        GitSandbox.WriteFile(repo, "new file.txt", "untracked");
        var pushed = await repo.Stash.PushAsync("work in progress");
        Assert.AreEqual(StashStatus.Succeeded, pushed.Status, pushed.Message);
        var afterStash = await repo.GetStatusAsync();
        Assert.IsTrue(afterStash.IsClean, string.Join(", ", afterStash.Entries));

        var list = await repo.Stash.ListAsync();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(0, list[0].Index);
        Assert.AreEqual("stash@{0}", list[0].Name);
        StringAssert.Contains(list[0].Message, "work in progress");

        var popped = await repo.Stash.PopAsync();
        Assert.AreEqual(StashStatus.Succeeded, popped.Status, popped.Message);
        Assert.AreEqual("changed", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));
        Assert.IsTrue(File.Exists(repo.GetFullPath("new file.txt")));
        Assert.AreEqual(0, (await repo.Stash.ListAsync()).Count);

        var missingStash = await repo.Stash.ApplyAsync(3);
        Assert.AreEqual(StashStatus.NotFound, missingStash.Status, missingStash.Error?.ToString());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Stash_PopWithConflicts_KeepsStash()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Initial");
        GitSandbox.WriteFile(repo, "a.txt", "stashed\n");
        await repo.Stash.PushAsync();
        await sandbox.CommitFileAsync(repo, "a.txt", "committed\n", "Conflicting change");

        var popped = await repo.Stash.PopAsync();

        Assert.AreEqual(StashStatus.Conflicts, popped.Status, popped.Message);
        Assert.AreEqual(1, (await repo.Stash.ListAsync()).Count);
        Assert.AreEqual(ConflictKind.BothModified, (await repo.Conflicts.ListAsync()).Single().Conflict);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Conflicts_ResolveAndConcludeMerge()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "both.txt", "base\n", "Base");
        await sandbox.CommitFileAsync(repo, "deleted-by-them.txt", "base\n", "Base 2");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, "both.txt", "theirs\n", "Theirs");
        await sandbox.GitAsync(repo.WorkingDirectory, "rm", "--quiet", "deleted-by-them.txt");
        await sandbox.GitAsync(repo.WorkingDirectory, "commit", "--quiet", "-m", "Delete");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "both.txt", "ours\n", "Ours");
        await sandbox.CommitFileAsync(repo, "deleted-by-them.txt", "modified by us\n", "Modify");
        var merge = await sandbox.RunAsync(repo.WorkingDirectory, "merge", "other");
        Assert.IsFalse(merge.Succeeded);

        var conflicts = await repo.Conflicts.ListAsync();
        Assert.AreEqual(ConflictKind.BothModified, conflicts.Single(c => c.Path == "both.txt").Conflict);
        Assert.AreEqual(ConflictKind.DeletedByThem, conflicts.Single(c => c.Path == "deleted-by-them.txt").Conflict);
        Assert.IsTrue(await repo.Conflicts.HasConflictMarkersAsync("both.txt"));
        Assert.AreEqual(RepositoryState.Merging, repo.GetState());

        var rejected = await repo.Conflicts.MarkResolvedAsync(["both.txt"]);
        Assert.AreEqual(ConflictResolutionStatus.ConflictMarkersFound, rejected.Status);
        CollectionAssert.AreEqual(new[] { "both.txt" }, rejected.FilesWithMarkers.ToArray());

        await repo.Conflicts.TakeTheirsAsync(["both.txt", "deleted-by-them.txt"]);
        Assert.AreEqual("theirs\n", GitSandbox.ReadFile(repo.WorkingDirectory, "both.txt"));
        Assert.IsFalse(File.Exists(repo.GetFullPath("deleted-by-them.txt")));
        Assert.AreEqual(0, (await repo.Conflicts.ListAsync()).Count);

        var concluded = await repo.CommitAsync(null);
        Assert.AreEqual(CommitStatus.Committed, concluded.Status, concluded.Message);
        Assert.IsTrue((await repo.History.GetLogAsync())[0].IsMerge);
        Assert.AreEqual(RepositoryState.None, repo.GetState());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Conflicts_MarkResolvedAfterManualEdit_AllowsMergeEqualToHead()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "other");
        await sandbox.CommitFileAsync(repo, "a.txt", "theirs\n", "Theirs");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "a.txt", "ours\n", "Ours");
        await sandbox.RunAsync(repo.WorkingDirectory, "merge", "other");

        // Resolve to exactly our version: the merge must still be concludable (v1 failed here).
        GitSandbox.WriteFile(repo, "a.txt", "ours\n");
        var resolved = await repo.Conflicts.MarkAllResolvedAsync();
        Assert.AreEqual(ConflictResolutionStatus.Resolved, resolved.Status, resolved.Message);

        var concluded = await repo.CommitAsync(null);
        Assert.AreEqual(CommitStatus.Committed, concluded.Status, concluded.Message);
        Assert.IsTrue((await repo.History.GetLogAsync())[0].IsMerge);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Reset_SoftAndHard()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        var first = await sandbox.CommitFileAsync(repo, "a.txt", "1", "First");
        await sandbox.CommitFileAsync(repo, "a.txt", "2", "Second");

        await repo.ResetAsync("HEAD~1", ResetMode.Soft);
        Assert.AreEqual(first, await repo.ResolveRevisionAsync("HEAD"));
        Assert.IsTrue((await repo.GetStatusAsync()).Entries.Single().IsStaged);

        await repo.ResetAsync("HEAD", ResetMode.Hard);
        var afterStash = await repo.GetStatusAsync();
        Assert.IsTrue(afterStash.IsClean, string.Join(", ", afterStash.Entries));
        Assert.AreEqual("1", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));
    }

    [TestMethod]
    public void ConflictMarkers_AreDetectedOnlyInText()
    {
        Assert.IsTrue(ConflictCommands.ContainsConflictMarkers("a\r\n<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b\r\n"u8.ToArray()));
        Assert.IsFalse(ConflictCommands.ContainsConflictMarkers("just =======\n text"u8.ToArray()));
        Assert.IsFalse(ConflictCommands.ContainsConflictMarkers([0, 1, 2, .. "<<<<<<< a\n=======\n>>>>>>> b\n"u8.ToArray()]));
    }
}

/// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value)
    {
        lock (this)
            handler(value);
    }
}
