namespace FlexGit.Tests.Unit;

[TestClass]
public class ParserTests
{
    [TestMethod]
    public void StatusParser_ParsesHeadersAndAllEntryTypes()
    {
        var output = string.Join("\0",
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "1 M. N... 100644 100644 100644 aaaa bbbb data/Scene 1.txt",
            "1 .M N... 100644 100644 100644 aaaa aaaa data/Тест.cs",
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 data/New Name.png",
            "data/Old Name.png",
            "u UU N... 100644 100644 100644 100644 h1 h2 h3 data/Conflict.psd",
            "u DU N... 100644 000000 100644 100644 h1 h2 h3 deleted-by-us.txt",
            "? data/Texture [1].png",
            "! Library/cache.bin",
            "");

        var status = StatusParser.Parse(output);

        Assert.AreEqual("feature/x", status.Branch);
        Assert.AreEqual("1111111111111111111111111111111111111111", status.HeadSha);
        Assert.AreEqual("origin/feature/x", status.Upstream);
        Assert.AreEqual(2, status.Ahead);
        Assert.AreEqual(3, status.Behind);
        Assert.AreEqual(7, status.Entries.Count);

        var staged = status.Entries[0];
        Assert.AreEqual("data/Scene 1.txt", staged.Path);
        Assert.AreEqual(FileStatus.Modified, staged.IndexStatus);
        Assert.IsTrue(staged.IsStaged);
        Assert.IsFalse(staged.HasUnstagedChanges);

        var unstaged = status.Entries[1];
        Assert.AreEqual("data/Тест.cs", unstaged.Path);
        Assert.IsTrue(unstaged.HasUnstagedChanges);
        Assert.IsFalse(unstaged.IsStaged);

        var renamed = status.Entries[2];
        Assert.AreEqual("data/New Name.png", renamed.Path);
        Assert.AreEqual("data/Old Name.png", renamed.OriginalPath);
        Assert.AreEqual(FileStatus.Renamed, renamed.IndexStatus);

        Assert.AreEqual(ConflictKind.BothModified, status.Entries[3].Conflict);
        Assert.AreEqual(ConflictKind.DeletedByUs, status.Entries[4].Conflict);
        Assert.IsTrue(status.HasConflicts);

        Assert.AreEqual("data/Texture [1].png", status.Entries[5].Path);
        Assert.IsTrue(status.Entries[5].IsUntracked);
        Assert.IsTrue(status.Entries[6].IsIgnored);
        Assert.IsFalse(status.IsClean);
    }

    [TestMethod]
    public void StatusParser_HandlesUnbornAndDetached()
    {
        var unborn = StatusParser.Parse("# branch.oid (initial)\0# branch.head main\0");
        Assert.IsTrue(unborn.IsUnborn);
        Assert.AreEqual("main", unborn.Branch);
        Assert.IsTrue(unborn.IsClean);

        var detached = StatusParser.Parse("# branch.oid abc\0# branch.head (detached)\0");
        Assert.IsTrue(detached.IsDetachedHead);
        Assert.IsNull(detached.Branch);
    }

    [TestMethod]
    public void RefParser_ParsesBranches()
    {
        var output =
            "refs/heads/main\0main\0aaa\0origin/main\0ahead 1, behind 2\0origin\0*\0\02026-11-14T10:00:00+01:00\0Initial commit\n" +
            "refs/heads/gone\0gone\0bbb\0origin/gone\0gone\0origin\0 \0\02026-11-13T10:00:00+00:00\0Old\n" +
            "refs/remotes/origin/HEAD\0origin\0aaa\0\0\0\0 \0refs/remotes/origin/main\02026-11-14T10:00:00+01:00\0Initial commit\n" +
            "refs/remotes/my.fork/feature/x\0my.fork/feature/x\0ccc\0\0\0\0 \0\02026-11-14T10:00:00+01:00\0Feature\n";

        var branches = RefParser.ParseBranches(output, ["origin", "my.fork"]);

        Assert.AreEqual(3, branches.Count, "symbolic origin/HEAD must be skipped");

        var main = branches[0];
        Assert.IsTrue(main.IsCurrent);
        Assert.IsFalse(main.IsRemote);
        Assert.AreEqual("origin/main", main.Upstream);
        Assert.AreEqual("origin", main.RemoteName);
        Assert.AreEqual(1, main.Ahead);
        Assert.AreEqual(2, main.Behind);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(1)), main.LastCommitDate);

        Assert.IsTrue(branches[1].IsUpstreamGone);

        var remote = branches[2];
        Assert.IsTrue(remote.IsRemote);
        Assert.AreEqual("my.fork", remote.RemoteName);
        Assert.AreEqual("feature/x", remote.FriendlyName);
    }

    [TestMethod]
    public void RefParser_ParsesTags()
    {
        var output =
            "refs/tags/v1\0v1\0commit\0aaa\0\0\0\0\0commit subject\n" +
            "refs/tags/v2\0v2\0tag\0tagobj\0bbb\0Jane\0<jane@x.org>\02026-11-14T10:00:00+00:00\0Release 2\n";

        var tags = RefParser.ParseTags(output);

        Assert.IsFalse(tags[0].IsAnnotated);
        Assert.AreEqual("aaa", tags[0].TargetSha);
        Assert.IsNull(tags[0].Message);

        Assert.IsTrue(tags[1].IsAnnotated);
        Assert.AreEqual("bbb", tags[1].TargetSha);
        Assert.AreEqual("jane@x.org", tags[1].Tagger!.Email);
        Assert.AreEqual("Release 2", tags[1].Message);
    }

    [TestMethod]
    public void RefParser_ParsesRemotes()
    {
        var output = "remote.origin.url\nhttps://github.com/a/b.git\0remote.my.fork.url\nC:/repos/fork\0remote.origin.pushurl\ngit@github.com:a/b.git\0";

        var remotes = RefParser.ParseRemotes(output);

        Assert.AreEqual(2, remotes.Count);
        Assert.AreEqual("origin", remotes[0].Name);
        Assert.AreEqual("https://github.com/a/b.git", remotes[0].FetchUrl);
        Assert.AreEqual("git@github.com:a/b.git", remotes[0].PushUrl);
        Assert.AreEqual("my.fork", remotes[1].Name);
        Assert.AreEqual("C:/repos/fork", remotes[1].PushUrl);
    }

    [TestMethod]
    public void LogParser_ParsesMultilineBodies()
    {
        var output =
            "c2\0c1\0Ann\0ann@x.org\02026-11-14T10:00:00+00:00\0Bob\0bob@x.org\02026-11-14T11:00:00+00:00\0Second\0Line one\nLine two\n\u001e\n" +
            "c1\0\0Ann\0ann@x.org\02026-11-13T10:00:00+00:00\0Ann\0ann@x.org\02026-11-13T10:00:00+00:00\0First\0\u001e\n";

        var commits = LogParser.Parse(output);

        Assert.AreEqual(2, commits.Count);
        Assert.AreEqual("c2", commits[0].Sha);
        CollectionAssert.AreEqual(new[] { "c1" }, commits[0].ParentShas.ToArray());
        Assert.AreEqual("Bob", commits[0].Committer.Name);
        Assert.AreEqual("Second\n\nLine one\nLine two", commits[0].Message);
        Assert.AreEqual(0, commits[1].ParentShas.Count);
        Assert.AreEqual("First", commits[1].Message);
    }
}
