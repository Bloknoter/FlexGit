using System.Text;

namespace FlexGit.Tests.Unit;

[TestClass]
public class DiffParserTests
{
    private static List<DiffSection> Parse(string patch) => DiffParser.Parse(Encoding.UTF8.GetBytes(patch));

    private static string Describe(DiffLine line) => $"{line} [{line.OldLineNumber?.ToString() ?? "-"}|{line.NewLineNumber?.ToString() ?? "-"}]";

    /// <summary>Compares joined text so a failure shows both sequences.</summary>
    private static void AssertSequence<T>(T[] expected, T[] actual) => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", actual));

    [TestMethod]
    public void Parse_ModifiedFile_ReturnsHunksWithLineNumbersAndHeading()
    {
        var sections = Parse(
            "diff --git a/data/notes.txt b/data/notes.txt\n" +
            "index 1111111111111111111111111111111111111111..2222222222222222222222222222222222222222 100644\n" +
            "--- a/data/notes.txt\n" +
            "+++ b/data/notes.txt\n" +
            "@@ -2,3 +2,3 @@ Section A\n" +
            " two\n" +
            "-three\n" +
            "+THREE\n" +
            " four\n" +
            "@@ -10,2 +10,3 @@\n" +
            " ten\n" +
            "+ten and a half\n" +
            " eleven\n");

        var section = sections.Single();
        Assert.AreEqual("data/notes.txt", section.OldPath);
        Assert.AreEqual("data/notes.txt", section.NewPath);
        Assert.AreEqual(DiffChangeKind.Modified, section.Change);
        Assert.IsFalse(section.IsBinary);
        Assert.AreEqual("1111111111111111111111111111111111111111", section.OldObjectId);
        Assert.AreEqual(2, section.Hunks.Count);

        var first = section.Hunks[0];
        Assert.AreEqual((2, 3, 2, 3), (first.OldStart, first.OldCount, first.NewStart, first.NewCount));
        Assert.AreEqual("Section A", first.Heading);
        AssertSequence(
            new[] { " two [2|2]", "-three [3|-]", "+THREE [-|3]", " four [4|4]" },
            first.Lines.Select(Describe).ToArray());

        var second = section.Hunks[1];
        Assert.AreEqual("", second.Heading);
        AssertSequence(
            new[] { " ten [10|10]", "+ten and a half [-|11]", " eleven [11|12]" },
            second.Lines.Select(Describe).ToArray());
    }

    [TestMethod]
    public void Parse_HunkHeaderWithoutCounts_MeansOneLine()
    {
        var hunk = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "index 9bda8c3..bc2cbf1 100644\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1,2 @@\n" +
            " Y\n" +
            "+X\n").Single().Hunks.Single();

        Assert.AreEqual((1, 1, 1, 2), (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount));
        Assert.AreEqual(2, hunk.Lines.Count);
    }

    [TestMethod]
    public void Parse_NewFile_ReportsAddedWithoutOldObject()
    {
        var section = Parse(
            "diff --git a/n.txt b/n.txt\n" +
            "new file mode 100644\n" +
            "index 0000000000000000000000000000000000000000..45b983be36b73c0788dc9cbcb76cbb80fc7bb057\n" +
            "--- /dev/null\n" +
            "+++ b/n.txt\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+hi\n" +
            "+there\n").Single();

        Assert.AreEqual(DiffChangeKind.Added, section.Change);
        Assert.IsNull(section.OldObjectId);
        AssertSequence(new[] { "+hi [-|1]", "+there [-|2]" }, section.Hunks.Single().Lines.Select(Describe).ToArray());
    }

    [TestMethod]
    public void Parse_DeletedFile_ReportsDeletedWithOldObject()
    {
        var section = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "deleted file mode 100644\n" +
            "index de980441c3ab03a8c07dda1ad27b8a11f39deb1e..0000000000000000000000000000000000000000\n" +
            "--- a/a.txt\n" +
            "+++ /dev/null\n" +
            "@@ -1,2 +0,0 @@\n" +
            "-a\n" +
            "-b\n").Single();

        Assert.AreEqual(DiffChangeKind.Deleted, section.Change);
        Assert.AreEqual("de980441c3ab03a8c07dda1ad27b8a11f39deb1e", section.OldObjectId);
        AssertSequence(new[] { "-a [1|-]", "-b [2|-]" }, section.Hunks.Single().Lines.Select(Describe).ToArray());
    }

    [TestMethod]
    public void Parse_Rename_ReportsBothPaths()
    {
        var section = Parse(
            "diff --git a/data/old name.txt b/data/new name.txt\n" +
            "similarity index 75%\n" +
            "rename from data/old name.txt\n" +
            "rename to data/new name.txt\n" +
            "index de98044..d68dd40 100644\n" +
            "--- a/data/old name.txt\n" +
            "+++ b/data/new name.txt\n" +
            "@@ -3 +3,2 @@\n" +
            " c\n" +
            "+d\n").Single();

        Assert.AreEqual(DiffChangeKind.Renamed, section.Change);
        Assert.AreEqual("data/old name.txt", section.OldPath);
        Assert.AreEqual("data/new name.txt", section.NewPath);
        Assert.AreEqual(1, section.Hunks.Count);
    }

    [TestMethod]
    public void Parse_PureRename_HasNoHunks()
    {
        var section = Parse(
            "diff --git a/x.txt b/y.txt\n" +
            "similarity index 100%\n" +
            "rename from x.txt\n" +
            "rename to y.txt\n").Single();

        Assert.AreEqual(DiffChangeKind.Renamed, section.Change);
        Assert.AreEqual("x.txt", section.OldPath);
        Assert.AreEqual("y.txt", section.NewPath);
        Assert.AreEqual(0, section.Hunks.Count);
    }

    [TestMethod]
    public void Parse_BinaryFile_ReportsBinaryWithoutHunks()
    {
        var section = Parse(
            "diff --git a/raw.dat b/raw.dat\n" +
            "index d5d0b8b..4a27031 100644\n" +
            "Binary files a/raw.dat and b/raw.dat differ\n").Single();

        Assert.AreEqual(DiffChangeKind.Modified, section.Change);
        Assert.IsTrue(section.IsBinary);
        Assert.AreEqual(0, section.Hunks.Count);
        Assert.AreEqual("raw.dat", section.NewPath);
    }

    [TestMethod]
    public void Parse_NoNewlineMarkerAndCrLf_FlagLineAndStripLineBreak()
    {
        var lines = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "index de98044..910fc02 100644\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,3 +1,3 @@\n" +
            "-a\n" +
            "+a\r\n" +
            " b\n" +
            "-c\n" +
            "+c\n" +
            "\\ No newline at end of file\n").Single().Hunks.Single().Lines;

        AssertSequence(new[] { "a", "a", "b", "c", "c" }, lines.Select(l => l.Text).ToArray());
        AssertSequence(new[] { false, false, false, false, true }, lines.Select(l => l.NoNewlineAtEnd).ToArray());
    }

    [TestMethod]
    public void Parse_ContentLinesThatLookLikeHeaders_StayInTheHunk()
    {
        var sections = Parse(
            "diff --git a/s.sql b/s.sql\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/s.sql\n" +
            "+++ b/s.sql\n" +
            "@@ -1,2 +1,2 @@\n" +
            "--- a/comment\n" +
            "+++ b/comment\n" +
            " diff --git a/x b/x\n");

        var lines = sections.Single().Hunks.Single().Lines;
        AssertSequence(
            new[] { "--- a/comment [1|-]", "+++ b/comment [-|1]", " diff --git a/x b/x [2|2]" },
            lines.Select(Describe).ToArray());
    }

    [TestMethod]
    public void Parse_EmptyLineInsideHunk_IsAnUnchangedEmptyLine()
    {
        // diff.suppressBlankEmpty=true makes git print empty context lines without the leading space.
        var lines = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,2 +1,2 @@\n" +
            "\n" +
            "-x\n" +
            "+y\n").Single().Hunks.Single().Lines;

        AssertSequence(new[] { "  [1|1]", "-x [2|-]", "+y [-|2]" }, lines.Select(Describe).ToArray());
    }

    [TestMethod]
    public void Parse_QuotedPaths_AreUnquoted()
    {
        var sections = Parse(
            "diff --git \"a/we\\\"ird\\tname.txt\" \"b/we\\\"ird\\tname.txt\"\n" +
            "index 1111111..2222222 100644\n" +
            "--- \"a/we\\\"ird\\tname.txt\"\n" +
            "+++ \"b/we\\\"ird\\tname.txt\"\n" +
            "@@ -1 +1 @@\n" +
            "-x\n" +
            "+y\n" +
            "diff --git a/x.txt \"b/\\303\\244 \\\\.txt\"\n" +
            "similarity index 100%\n" +
            "rename from x.txt\n" +
            "rename to \"\\303\\244 \\\\.txt\"\n");

        Assert.AreEqual("we\"ird\tname.txt", sections[0].NewPath);
        Assert.AreEqual("we\"ird\tname.txt", sections[0].OldPath);
        Assert.AreEqual("x.txt", sections[1].OldPath);
        Assert.AreEqual("ä \\.txt", sections[1].NewPath);
    }

    [TestMethod]
    public void Parse_UnquotedNonAsciiPathWithSpaces_IsRead()
    {
        var section = Parse(
            "diff --git a/data/b b/ä.txt b/data/b b/ä.txt\n" +
            "deleted file mode 100644\n" +
            "index 1111111..0000000\n").Single();

        Assert.AreEqual("data/b b/ä.txt", section.NewPath);
        Assert.AreEqual(DiffChangeKind.Deleted, section.Change);
    }

    [TestMethod]
    public void Parse_ModeChangeOnly_IsModifiedWithoutHunks()
    {
        var section = Parse(
            "diff --git a/run.sh b/run.sh\n" +
            "old mode 100644\n" +
            "new mode 100755\n").Single();

        Assert.AreEqual(DiffChangeKind.Modified, section.Change);
        Assert.AreEqual(0, section.Hunks.Count);
    }

    [TestMethod]
    public void Parse_SeveralFiles_ReturnsOneSectionEach()
    {
        var sections = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "deleted file mode 100644\n" +
            "index 1111111..0000000\n" +
            "--- a/a.txt\n" +
            "+++ /dev/null\n" +
            "@@ -1 +0,0 @@\n" +
            "-a\n" +
            "diff --git a/b.txt b/b.txt\n" +
            "new file mode 100644\n" +
            "index 0000000..2222222\n" +
            "--- /dev/null\n" +
            "+++ b/b.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+b\n");

        AssertSequence(new[] { "a.txt", "b.txt" }, sections.Select(s => s.NewPath).ToArray());
        AssertSequence(new[] { DiffChangeKind.Deleted, DiffChangeKind.Added }, sections.Select(s => s.Change).ToArray());
        Assert.AreEqual("-a", sections[0].Hunks.Single().Lines.Single().ToString());
        Assert.AreEqual("+b", sections[1].Hunks.Single().Lines.Single().ToString());
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsNothing()
    {
        Assert.AreEqual(0, Parse("").Count);
    }

    [TestMethod]
    public void CreateAddedHunks_Text_ReturnsOneHunkOfAddedLines()
    {
        var hunk = DiffParser.CreateAddedHunks(Encoding.UTF8.GetBytes("one\r\ntwo\nthree")).Single();

        Assert.AreEqual((0, 0, 1, 3), (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount));
        AssertSequence(new[] { "+one [-|1]", "+two [-|2]", "+three [-|3]" }, hunk.Lines.Select(Describe).ToArray());
        AssertSequence(new[] { false, false, true }, hunk.Lines.Select(l => l.NoNewlineAtEnd).ToArray());
    }

    [TestMethod]
    public void CreateAddedHunks_EmptyFile_ReturnsNoHunks()
    {
        Assert.AreEqual(0, DiffParser.CreateAddedHunks([]).Count);
    }

    [TestMethod]
    public void ParseLfsPointerSize_Pointer_ReturnsSize()
    {
        Assert.AreEqual(3000L, DiffParser.ParseLfsPointerSize([
            "version https://git-lfs.github.com/spec/v1",
            "oid sha256:8e1f0548218a377d0e774b9cd5ece4b5f2f51021a9bac683c31969be1613baf4",
            "size 3000"]));
    }

    [TestMethod]
    public void ParseLfsPointerSize_NotAPointer_ReturnsNull()
    {
        Assert.IsNull(DiffParser.ParseLfsPointerSize(["just text", "size 3000"]));
        Assert.IsNull(DiffParser.ParseLfsPointerSize(["version https://git-lfs.github.com/spec/v1", "oid sha256:abc"]));
        Assert.IsNull(DiffParser.ParseLfsPointerSize([]));
    }

    [TestMethod]
    public void ParseAttributes_ReturnsValuesOfThePath()
    {
        var attributes = DiffParser.ParseAttributes(
            "big.bin\0filter\0lfs\0big.bin\0diff\0lfs\0a.txt\0filter\0unspecified\0", "big.bin");

        Assert.AreEqual("lfs", attributes["filter"]);
        Assert.AreEqual("lfs", attributes["diff"]);
        Assert.AreEqual(2, attributes.Count);
    }
}
