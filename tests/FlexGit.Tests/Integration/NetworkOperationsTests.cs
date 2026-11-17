namespace FlexGit.Tests.Integration;

/// <summary>Two clones ("alice" and "bob") of a bare remote, the classic team workflow.</summary>
[TestClass]
public class NetworkOperationsTests
{
    private static async Task<(GitSandbox Sandbox, string Remote, GitRepository Alice, GitRepository Bob)> CreateTeamAsync()
    {
        var sandbox = GitSandbox.Create();
        var remote = await sandbox.InitBareRemoteAsync();
        var alice = await sandbox.CloneRepositoryAsync(remote, "alice");
        await sandbox.CommitFileAsync(alice, "shared.txt", "base\n", "Initial");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet", "-u", "origin", "main");
        var bob = await sandbox.CloneRepositoryAsync(remote, "bob");
        return (sandbox, remote, alice, bob);
    }

    private static string FileUrl(string path) => "file:///" + path.Replace('\\', '/');

    [TestMethod]
    [Timeout(120_000)]
    public async Task Clone_ReportsProgress_AndHandlesErrors()
    {
        var (sandbox, remote, _, _) = await CreateTeamAsync();
        using var _ = sandbox;

        var reports = new List<GitProgress>();
        var cloned = await GitRepository.CloneAsync(FileUrl(remote), sandbox.PathOf("carol"), progress: new SyncProgress<GitProgress>(reports.Add), options: sandbox.Options);
        Assert.AreEqual(CloneStatus.Cloned, cloned.Status, cloned.Message);
        Assert.IsTrue(File.Exists(Path.Combine(cloned.Repository!.WorkingDirectory, "shared.txt")));
        Assert.IsTrue(reports.Count > 0, "clone over file:// must report progress");
        Assert.IsTrue(reports.All(r => r.Percent is >= 0 and <= 100));

        var notEmpty = await GitRepository.CloneAsync(remote, sandbox.PathOf("carol"), options: sandbox.Options);
        Assert.AreEqual(CloneStatus.DirectoryNotEmpty, notEmpty.Status);

        var missing = await GitRepository.CloneAsync(sandbox.PathOf("no-such-remote"), sandbox.PathOf("dave"), options: sandbox.Options);
        Assert.AreEqual(CloneStatus.Failed, missing.Status);
        Assert.AreEqual(GitErrorKind.RemoteNotFound, missing.Error!.Kind);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task FetchAndPull_FastForward_ThenUpToDate()
    {
        var (sandbox, _, alice, bob) = await CreateTeamAsync();
        using var _ = sandbox;
        var pushed = await sandbox.CommitFileAsync(alice, "alice.txt", "hi", "Alice change");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");

        var fetched = await bob.FetchAsync();
        Assert.AreEqual(FetchStatus.Succeeded, fetched.Status, fetched.Message);
        Assert.AreEqual(1, (await bob.GetStatusAsync()).Behind);

        var pulled = await bob.PullAsync();
        Assert.AreEqual(PullStatus.FastForwarded, pulled.Status, pulled.Message);
        Assert.AreEqual(pushed, pulled.HeadSha);

        var again = await bob.PullAsync();
        Assert.AreEqual(PullStatus.UpToDate, again.Status, again.Message);

        var badRemote = await bob.FetchAsync(new FetchOptions { Remote = "nope" });
        Assert.AreEqual(FetchStatus.Failed, badRemote.Status);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Pull_DivergedBranches_MergeRebaseAndFastForwardOnly()
    {
        var (sandbox, _, alice, bob) = await CreateTeamAsync();
        using var _ = sandbox;
        await sandbox.CommitFileAsync(alice, "alice.txt", "a", "Alice");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");
        await sandbox.CommitFileAsync(bob, "bob.txt", "b", "Bob");

        var ffOnly = await bob.PullAsync(new PullOptions { Mode = PullMode.FastForwardOnly });
        Assert.AreEqual(PullStatus.CannotFastForward, ffOnly.Status, ffOnly.Message);

        var rebased = await bob.PullAsync(new PullOptions { Mode = PullMode.Rebase });
        Assert.AreEqual(PullStatus.Rebased, rebased.Status, rebased.Message);
        Assert.IsFalse((await bob.History.GetLogAsync())[0].IsMerge);

        await sandbox.CommitFileAsync(alice, "alice2.txt", "a", "Alice 2");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");
        await sandbox.CommitFileAsync(bob, "bob2.txt", "b", "Bob 2");
        var merged = await bob.PullAsync();
        Assert.AreEqual(PullStatus.Merged, merged.Status, merged.Message);
        Assert.IsTrue((await bob.History.GetLogAsync())[0].IsMerge);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Pull_WithConflicts_ResolveAndContinue()
    {
        var (sandbox, _, alice, bob) = await CreateTeamAsync();
        using var _ = sandbox;
        await sandbox.CommitFileAsync(alice, "shared.txt", "alice\n", "Alice edit");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");
        await sandbox.CommitFileAsync(bob, "shared.txt", "bob\n", "Bob edit");

        var pulled = await bob.PullAsync();
        Assert.AreEqual(PullStatus.Conflicts, pulled.Status, pulled.Message);
        Assert.AreEqual(RepositoryState.Merging, bob.GetState());

        var notYet = await bob.ContinueMergeAsync();
        Assert.AreEqual(MergeStatus.Conflicts, notYet.Status);

        await bob.Conflicts.TakeOursAsync(["shared.txt"]);
        var continued = await bob.ContinueMergeAsync();
        Assert.AreEqual(MergeStatus.Merged, continued.Status, continued.Message);
        Assert.AreEqual("bob\n", GitSandbox.ReadFile(bob.WorkingDirectory, "shared.txt"));
        Assert.AreEqual(RepositoryState.None, bob.GetState());

        var pushed = await bob.PushAsync();
        Assert.AreEqual(PushStatus.Pushed, pushed.Status, pushed.Message);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Pull_ReportsLocalChangesAndMissingUpstream()
    {
        var (sandbox, _, alice, bob) = await CreateTeamAsync();
        using var _ = sandbox;
        await sandbox.CommitFileAsync(alice, "shared.txt", "alice\n", "Alice edit");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");
        GitSandbox.WriteFile(bob, "shared.txt", "uncommitted\n");

        var blocked = await bob.PullAsync();
        Assert.AreEqual(PullStatus.LocalChangesWouldBeOverwritten, blocked.Status, blocked.Message);
        Assert.AreEqual("uncommitted\n", GitSandbox.ReadFile(bob.WorkingDirectory, "shared.txt"));

        var autoStashed = await bob.PullAsync(new PullOptions { Mode = PullMode.Rebase, AutoStash = true });
        Assert.AreNotEqual(PullStatus.Failed, autoStashed.Status, autoStashed.Message);

        await bob.Index.DiscardAllAsync();
        await bob.Branches.SwitchAsync("local-only", new SwitchOptions { Create = true });
        var noUpstream = await bob.PullAsync();
        Assert.AreEqual(PullStatus.NoUpstream, noUpstream.Status, noUpstream.Message);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Push_NewBranchSetsUpstream_ThenUpToDate_ThenRejected()
    {
        var (sandbox, remote, alice, bob) = await CreateTeamAsync();
        using var _ = sandbox;

        await bob.Branches.SwitchAsync("feature/bob", new SwitchOptions { Create = true });
        await sandbox.CommitFileAsync(bob, "bob.txt", "b", "Bob feature");
        var reports = new List<GitProgress>();
        var pushed = await bob.PushAsync(progress: new SyncProgress<GitProgress>(reports.Add));
        Assert.AreEqual(PushStatus.Pushed, pushed.Status, pushed.Message);
        Assert.AreEqual(PushRefStatus.New, pushed.RefUpdates.Single().Status);
        Assert.AreEqual("refs/heads/feature/bob", pushed.RefUpdates.Single().Destination);
        Assert.AreEqual("origin/feature/bob", (await bob.Branches.GetCurrentAsync())!.Upstream);

        var again = await bob.PushAsync();
        Assert.AreEqual(PushStatus.UpToDate, again.Status, again.Message);

        await sandbox.CommitFileAsync(alice, "shared.txt", "alice\n", "Alice");
        await sandbox.GitAsync(alice.WorkingDirectory, "push", "--quiet");
        await bob.Branches.SwitchAsync("main");
        await sandbox.CommitFileAsync(bob, "shared.txt", "bob\n", "Bob");
        var rejected = await bob.PushAsync();
        Assert.AreEqual(PushStatus.Rejected, rejected.Status, rejected.Message);
        Assert.AreEqual(GitErrorKind.Rejected, rejected.Error!.Kind);
        Assert.AreEqual(PushRefStatus.Rejected, rejected.RefUpdates.Single().Status);

        var deleted = await bob.Branches.DeleteRemoteAsync("feature/bob");
        Assert.AreEqual(PushStatus.Pushed, deleted.Status, deleted.Message);
        Assert.AreEqual(PushRefStatus.Deleted, deleted.RefUpdates.Single().Status);
        var remoteRefs = await sandbox.GitAsync(remote, "for-each-ref", "--format=%(refname)");
        Assert.IsFalse(remoteRefs.Contains("feature/bob"));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Tags_PushAndDeleteRemote()
    {
        var (sandbox, remote, alice, _) = await CreateTeamAsync();
        using var _ = sandbox;

        await alice.Tags.CreateAsync("v1.0.0", message: "First release");
        var pushed = await alice.Tags.PushAsync("v1.0.0");
        Assert.AreEqual(PushStatus.Pushed, pushed.Status, pushed.Message);
        StringAssert.Contains(await sandbox.GitAsync(remote, "tag", "--list"), "v1.0.0");

        await alice.Tags.CreateAsync("v1.0.1");
        var all = await alice.Tags.PushAllAsync();
        Assert.AreEqual(PushStatus.Pushed, all.Status, all.Message);

        var deleted = await alice.Tags.DeleteRemoteAsync("v1.0.0");
        Assert.AreEqual(PushStatus.Pushed, deleted.Status, deleted.Message);
        var remoteTags = await sandbox.GitAsync(remote, "tag", "--list");
        Assert.IsFalse(remoteTags.Contains("v1.0.0"));
        StringAssert.Contains(remoteTags, "v1.0.1");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Merge_AllOutcomes()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Base");

        await sandbox.GitAsync(repo.WorkingDirectory, "branch", "ff");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "ff");
        await sandbox.CommitFileAsync(repo, "ff.txt", "ff", "FF");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");

        Assert.AreEqual(MergeStatus.UpToDate, (await repo.MergeAsync("main")).Status);
        Assert.AreEqual(MergeStatus.NoMergeInProgress, (await repo.AbortMergeAsync()).Status);
        Assert.AreEqual(MergeStatus.NoMergeInProgress, (await repo.ContinueMergeAsync()).Status);

        var ff = await repo.MergeAsync("ff");
        Assert.AreEqual(MergeStatus.FastForwarded, ff.Status, ff.Message);

        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "side", "HEAD~1");
        await sandbox.CommitFileAsync(repo, "side.txt", "side", "Side");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        Assert.AreEqual(MergeStatus.CannotFastForward, (await repo.MergeAsync("side", new MergeOptions { FastForward = FastForwardMode.Only })).Status);

        var squashed = await repo.MergeAsync("side", new MergeOptions { Squash = true });
        Assert.AreEqual(MergeStatus.Squashed, squashed.Status, squashed.Message);
        Assert.IsTrue((await repo.GetStatusAsync()).Staged.Any());
        await repo.Index.DiscardAllAsync(deleteUntracked: true);

        var merged = await repo.MergeAsync("side", new MergeOptions { Message = "Merge side \"branch\"" });
        Assert.AreEqual(MergeStatus.Merged, merged.Status, merged.Message);
        Assert.AreEqual("Merge side \"branch\"", (await repo.History.GetLogAsync())[0].Subject);

        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "conflict", "HEAD~1");
        await sandbox.CommitFileAsync(repo, "a.txt", "conflict\n", "Conflict side");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "a.txt", "main\n", "Main side");
        var conflicted = await repo.MergeAsync("conflict");
        Assert.AreEqual(MergeStatus.Conflicts, conflicted.Status, conflicted.Message);

        var aborted = await repo.AbortMergeAsync();
        Assert.AreEqual(MergeStatus.Aborted, aborted.Status, aborted.Message);
        Assert.AreEqual(RepositoryState.None, repo.GetState());
        Assert.AreEqual("main\n", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));

        Assert.AreEqual(MergeStatus.Failed, (await repo.MergeAsync("no-such-branch")).Status);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Rebase_ConflictContinueSkipAbort()
    {
        using var sandbox = GitSandbox.Create();
        var repo = await sandbox.InitRepositoryAsync("repo");
        await sandbox.CommitFileAsync(repo, "a.txt", "base\n", "Base");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "topic");
        await sandbox.CommitFileAsync(repo, "a.txt", "topic\n", "Topic edit");
        await sandbox.CommitFileAsync(repo, "topic.txt", "t", "Topic file");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "main");
        await sandbox.CommitFileAsync(repo, "a.txt", "main\n", "Main edit");
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "topic");

        Assert.AreEqual(RebaseStatus.NoRebaseInProgress, (await repo.Rebase.ContinueAsync()).Status);
        Assert.AreEqual(RebaseStatus.UpToDate, (await repo.Rebase.StartAsync("topic")).Status);

        // Conflict -> abort restores the branch.
        var started = await repo.Rebase.StartAsync("main");
        Assert.AreEqual(RebaseStatus.Conflicts, started.Status, started.Message);
        Assert.IsTrue(repo.Rebase.IsInProgress);
        Assert.AreEqual(RebaseStatus.Conflicts, (await repo.Rebase.ContinueAsync()).Status, "unresolved conflicts block continue");
        var aborted = await repo.Rebase.AbortAsync();
        Assert.AreEqual(RebaseStatus.Aborted, aborted.Status, aborted.Message);
        Assert.AreEqual("topic\n", GitSandbox.ReadFile(repo.WorkingDirectory, "a.txt"));

        // Conflict -> resolve -> continue.
        await repo.Rebase.StartAsync("main");
        GitSandbox.WriteFile(repo, "a.txt", "resolved\n");
        await repo.Conflicts.MarkAllResolvedAsync();
        var continued = await repo.Rebase.ContinueAsync();
        Assert.AreEqual(RebaseStatus.Completed, continued.Status, continued.Message);
        var log = await repo.History.GetLogAsync();
        CollectionAssert.AreEqual(new[] { "Topic file", "Topic edit", "Main edit", "Base" }, log.Select(c => c.Subject).ToArray());

        // Conflict -> skip drops the conflicting commit.
        await sandbox.GitAsync(repo.WorkingDirectory, "switch", "--quiet", "-c", "topic2", "main~1");
        await sandbox.CommitFileAsync(repo, "a.txt", "topic2\n", "Topic2 edit");
        Assert.AreEqual(RebaseStatus.Conflicts, (await repo.Rebase.StartAsync("main")).Status);
        var skipped = await repo.Rebase.SkipAsync();
        Assert.AreEqual(RebaseStatus.Completed, skipped.Status, skipped.Message);
        Assert.AreEqual("Main edit", (await repo.History.GetLogAsync())[0].Subject);
    }

    [TestMethod]
    public void PushPorcelainParser_ParsesAllFlags()
    {
        var output = "To file:///remote\n" +
                     " \trefs/heads/main:refs/heads/main\tabc..def\n" +
                     "+\trefs/heads/f:refs/heads/f\tabc...def (forced update)\n" +
                     "-\t:refs/heads/old\t[deleted]\n" +
                     "*\trefs/heads/new:refs/heads/new\t[new branch]\n" +
                     "!\trefs/heads/x:refs/heads/x\t[rejected] (fetch first)\n" +
                     "=\trefs/tags/v1:refs/tags/v1\t[up to date]\n" +
                     "Done\n";

        var updates = PushPorcelainParser.Parse(output);

        CollectionAssert.AreEqual(
            new[] { PushRefStatus.FastForward, PushRefStatus.Forced, PushRefStatus.Deleted, PushRefStatus.New, PushRefStatus.Rejected, PushRefStatus.UpToDate },
            updates.Select(u => u.Status).ToArray());
        Assert.AreEqual("", updates[2].Source);
        Assert.AreEqual("refs/heads/old", updates[2].Destination);
        Assert.AreEqual("[rejected] (fetch first)", updates[4].Summary);
    }
}
