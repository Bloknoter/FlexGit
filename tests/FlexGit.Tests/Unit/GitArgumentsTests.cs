namespace FlexGit.Tests.Unit;

[TestClass]
public class GitArgumentsTests
{
    [DataTestMethod]
    [DataRow("simple", "simple")]
    [DataRow("", "\"\"")]
    [DataRow("with space", "\"with space\"")]
    [DataRow("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [DataRow(@"C:\path\file", @"C:\path\file")]
    [DataRow(@"C:\with space\", "\"C:\\with space\\\\\"")]
    [DataRow("back\\\"quote", "\"back\\\\\\\"quote\"")]
    [DataRow("tab\there", "\"tab\there\"")]
    public void Quote_FollowsWindowsCommandLineRules(string input, string expected)
    {
        Assert.AreEqual(expected, GitArguments.Quote(input));
    }

    [TestMethod]
    public void Join_SeparatesWithSpaces()
    {
        Assert.AreEqual("commit -m \"a b\" \"\"", GitArguments.Join(["commit", "-m", "a b", ""]));
    }

    [DataTestMethod]
    [DataRow("https://user:token@github.com/a/b.git", "https://***@github.com/a/b.git")]
    [DataRow("fetch https://x-access-token:abc@host/r", "fetch https://***@host/r")]
    [DataRow("https://github.com/a/b.git", "https://github.com/a/b.git")]
    [DataRow("git@github.com:a/b.git", "git@github.com:a/b.git")]
    public void RedactUrls_RemovesCredentials(string input, string expected)
    {
        Assert.AreEqual(expected, GitArguments.RedactUrls(input));
    }

    [DataTestMethod]
    [DataRow("-rf")]
    [DataRow("--upload-pack=evil")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("a\nb")]
    public void EnsureSafeRef_RejectsDangerousValues(string value)
    {
        Assert.ThrowsException<ArgumentException>(() => GitArguments.EnsureSafeRef(value, "ref"));
    }

    [TestMethod]
    public void EnsureSafeRef_AcceptsNormalRefs()
    {
        Assert.AreEqual("feature/x-1", GitArguments.EnsureSafeRef("feature/x-1", "ref"));
        Assert.AreEqual("HEAD~2", GitArguments.EnsureSafeRef("HEAD~2", "ref"));
    }

    [TestMethod]
    public void NulSeparatedPaths_NormalizesSeparatorsAndSkipsEmpty()
    {
        var bytes = GitArguments.NulSeparatedPaths([@"data\A.png", "", "data/B [1].png"]);
        Assert.AreEqual("data/A.png\0data/B [1].png", System.Text.Encoding.UTF8.GetString(bytes));
    }
}
