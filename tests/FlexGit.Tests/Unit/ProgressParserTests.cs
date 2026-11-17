namespace FlexGit.Tests.Unit;

[TestClass]
public class ProgressParserTests
{
    [DataTestMethod]
    [DataRow("Receiving objects:  45% (450/1000), 1.20 MiB | 1.00 MiB/s", "Receiving objects", 45, 450L, 1000L, false)]
    [DataRow("Resolving deltas: 100% (12/12), done.", "Resolving deltas", 100, 12L, 12L, false)]
    [DataRow("remote: Counting objects:  50% (1/2)", "Counting objects", 50, 1L, 2L, true)]
    [DataRow("remote: Compressing objects: 100% (3/3), done.", "Compressing objects", 100, 3L, 3L, true)]
    [DataRow("Updating files:   7% (7/100)", "Updating files", 7, 7L, 100L, false)]
    [DataRow("Writing objects: 100% (3/3), 250 bytes | 250.00 KiB/s, done.", "Writing objects", 100, 3L, 3L, false)]
    [DataRow("Downloading LFS objects:  50% (1/2), 1.2 MB | 1.0 MB/s", "Downloading LFS objects", 50, 1L, 2L, false)]
    [DataRow("Filtering content: 100% (2/2), 3.00 KiB | 0 bytes/s, done.", "Filtering content", 100, 2L, 2L, false)]
    public void TryParse_ParsesProgressLines(string line, string phase, int percent, long current, long total, bool remote)
    {
        Assert.IsTrue(ProgressParser.TryParse(line, out var progress));
        Assert.IsNotNull(progress);
        Assert.AreEqual(phase, progress.Phase);
        Assert.AreEqual(percent, progress.Percent);
        Assert.AreEqual(current, progress.Current);
        Assert.AreEqual(total, progress.Total);
        Assert.AreEqual(remote, progress.IsRemote);
    }

    [DataTestMethod]
    [DataRow("Cloning into 'repo'...")]
    [DataRow("To https://github.com/a/b.git")]
    [DataRow("")]
    [DataRow("fatal: repository not found")]
    public void TryParse_IgnoresOtherLines(string line)
    {
        Assert.IsFalse(ProgressParser.TryParse(line, out _));
    }
}
