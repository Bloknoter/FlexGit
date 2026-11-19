using System.Text;

namespace FlexGit.Tests.Unit;

[TestClass]
public class ConflictMarkerParserTests
{
    private const string TwoHunks =
        "<<<<<<< HEAD\n" +
        "uno\n" +
        "=======\n" +
        "one\n" +
        ">>>>>>> other\n" +
        "2\n" +
        "3\n" +
        "<<<<<<< HEAD\n" +
        "FIN\n" +
        "=======\n" +
        "END\n" +
        ">>>>>>> other\n";

    private static ConflictDocument Parse(string text, int markerSize = 7) => ConflictMarkerParser.Parse(Encoding.UTF8.GetBytes(text), markerSize);

    private static string Compose(ConflictDocument document, params ConflictHunkResolution[] resolutions)
        => Encoding.UTF8.GetString(ConflictMarkerParser.Compose(document, resolutions).Content);

    [TestMethod]
    public void Parse_TwoWayMarkers_ReturnsHunksWithSidesLabelsAndLines()
    {
        var document = Parse(TwoHunks);

        Assert.IsFalse(document.HasMalformedMarkers);
        Assert.AreEqual(2, document.Hunks.Count);

        var first = document.Hunks[0];
        Assert.AreEqual(0, first.Index);
        Assert.AreEqual("uno\n", first.Ours);
        Assert.AreEqual("one\n", first.Theirs);
        Assert.IsNull(first.Base);
        Assert.AreEqual("HEAD", first.OursLabel);
        Assert.AreEqual("other", first.TheirsLabel);
        Assert.AreEqual(1, first.StartLine);
        Assert.AreEqual(5, first.EndLine);

        var second = document.Hunks[1];
        Assert.AreEqual(1, second.Index);
        Assert.AreEqual("FIN\n", second.Ours);
        Assert.AreEqual("END\n", second.Theirs);
        Assert.AreEqual(8, second.StartLine);
        Assert.AreEqual(12, second.EndLine);
    }

    [TestMethod]
    public void Parse_Segments_AlternateTextAndHunksInFileOrder()
    {
        var document = Parse("top\n" + TwoHunks + "bottom\n");

        Assert.AreEqual(5, document.Segments.Count);
        Assert.AreEqual("top\n", document.Segments[0].Text);
        Assert.AreSame(document.Hunks[0], document.Segments[1].Hunk);
        Assert.AreEqual("2\n3\n", document.Segments[2].Text);
        Assert.AreSame(document.Hunks[1], document.Segments[3].Hunk);
        Assert.AreEqual("bottom\n", document.Segments[4].Text);
        Assert.IsTrue(document.Segments[1].IsConflict);
        Assert.IsFalse(document.Segments[0].IsConflict);
    }

    [TestMethod]
    public void Parse_Diff3Markers_ReturnsBaseAndBaseLabel()
    {
        var document = Parse("<<<<<<< ours\nuno\n||||||| base\n1\n=======\none\n>>>>>>> theirs\n");

        var hunk = document.Hunks.Single();
        Assert.AreEqual("uno\n", hunk.Ours);
        Assert.AreEqual("1\n", hunk.Base);
        Assert.AreEqual("base", hunk.BaseLabel);
        Assert.AreEqual("one\n", hunk.Theirs);
    }

    [TestMethod]
    public void Parse_MarkersWithoutLabels_ReturnsNullLabels()
    {
        var hunk = Parse("<<<<<<<\na\n=======\nb\n>>>>>>>\n").Hunks.Single();

        Assert.IsNull(hunk.OursLabel);
        Assert.IsNull(hunk.TheirsLabel);
        Assert.AreEqual("a\n", hunk.Ours);
        Assert.AreEqual("b\n", hunk.Theirs);
    }

    [TestMethod]
    public void Parse_EmptySide_ReturnsEmptyString()
    {
        var hunk = Parse("<<<<<<< HEAD\n=======\nadded\n>>>>>>> other\n").Hunks.Single();

        Assert.AreEqual("", hunk.Ours);
        Assert.AreEqual("added\n", hunk.Theirs);
    }

    [TestMethod]
    public void Parse_LongerMarkerRuns_AreContent()
    {
        var document = Parse("<<<<<<< HEAD\n<<<<<<<< not a marker\n========\n=======\n>>>>>>>>\n>>>>>>> other\n");

        var hunk = document.Hunks.Single();
        Assert.AreEqual("<<<<<<<< not a marker\n========\n", hunk.Ours);
        Assert.AreEqual(">>>>>>>>\n", hunk.Theirs);
    }

    [TestMethod]
    public void Parse_CustomMarkerSize_TreatsDefaultSizeLinesAsContent()
    {
        var document = Parse("<<<<<<<<<<<< HEAD\nTitle\n=======\n============\nOther\n>>>>>>>>>>>> other\n", markerSize: 12);

        var hunk = document.Hunks.Single();
        Assert.AreEqual("Title\n=======\n", hunk.Ours);
        Assert.AreEqual("Other\n", hunk.Theirs);
        Assert.IsFalse(document.HasMalformedMarkers);
    }

    [TestMethod]
    public void Parse_OrphanMarkersOutsideHunks_AreText()
    {
        var document = Parse("Title\n=======\n>>>>>>> nothing\n");

        Assert.AreEqual(0, document.Hunks.Count);
        Assert.IsFalse(document.HasMalformedMarkers);
        Assert.AreEqual("Title\n=======\n>>>>>>> nothing\n", document.Segments.Single().Text);
    }

    [DataTestMethod]
    [DataRow("<<<<<<< HEAD\na\n=======\nb\n=======\nc\n>>>>>>> other\n", DisplayName = "second separator")]
    [DataRow("<<<<<<< HEAD\na\n=======\nb\n", DisplayName = "no end marker")]
    [DataRow("<<<<<<< HEAD\na\n<<<<<<< HEAD\nb\n=======\nc\n>>>>>>> other\n", DisplayName = "start inside hunk")]
    [DataRow("<<<<<<< HEAD\na\n>>>>>>> other\n", DisplayName = "end before separator")]
    public void Parse_MalformedMarkers_ReportsNoHunks(string text)
    {
        var document = Parse("ok\n" + TwoHunks + text);

        Assert.IsTrue(document.HasMalformedMarkers);
        Assert.AreEqual(0, document.Hunks.Count);
        Assert.AreEqual("ok\n" + TwoHunks + text, document.Segments.Single().Text);
    }

    [TestMethod]
    public void Parse_CrLfFile_StripsCrFromLabelsAndKeepsCrLfInSides()
    {
        var hunk = Parse("<<<<<<< HEAD\r\nuno\r\n=======\r\none\r\n>>>>>>> other\r\n").Hunks.Single();

        Assert.AreEqual("HEAD", hunk.OursLabel);
        Assert.AreEqual("other", hunk.TheirsLabel);
        Assert.AreEqual("uno\r\n", hunk.Ours);
        Assert.AreEqual("one\r\n", hunk.Theirs);
    }

    [TestMethod]
    public void Parse_Bom_IsSkippedForMarkersAndDisplay()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<<<<<<< HEAD\na\n=======\nb\n>>>>>>> other\nrest\n")).ToArray();

        var document = ConflictMarkerParser.Parse(bytes, 7);

        Assert.AreEqual(1, document.Hunks.Count);
        Assert.AreEqual("rest\n", document.Segments[1].Text);
        var composed = ConflictMarkerParser.Compose(document, [new ConflictHunkResolution(0, ConflictChoice.Theirs)]).Content;
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }.Concat("b\nrest\n"u8.ToArray()).ToArray(), composed);
    }

    [TestMethod]
    public void Parse_BomBeforeText_IsStrippedFromFirstSegment()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("top\n"u8.ToArray()).ToArray();

        Assert.AreEqual("top\n", ConflictMarkerParser.Parse(bytes, 7).Segments.Single().Text);
    }

    [DataTestMethod]
    [DataRow(ConflictChoice.Ours, "top\nuno\n2\n3\nFIN\n")]
    [DataRow(ConflictChoice.Theirs, "top\none\n2\n3\nEND\n")]
    [DataRow(ConflictChoice.OursThenTheirs, "top\nuno\none\n2\n3\nFIN\nEND\n")]
    [DataRow(ConflictChoice.TheirsThenOurs, "top\none\nuno\n2\n3\nEND\nFIN\n")]
    public void Compose_SameChoiceForAllHunks_ReplacesEachMarkerBlock(ConflictChoice choice, string expected)
    {
        var document = Parse("top\n" + TwoHunks);

        var result = ConflictMarkerParser.Compose(document, [new ConflictHunkResolution(0, choice), new ConflictHunkResolution(1, choice)]);

        Assert.AreEqual(expected, Encoding.UTF8.GetString(result.Content));
        Assert.AreEqual(0, result.RemainingHunks);
    }

    [TestMethod]
    public void Compose_PerHunkChoices_ResolvesEachHunkItsOwnWay()
    {
        var document = Parse(TwoHunks);

        var text = Compose(document, new ConflictHunkResolution(1, ConflictChoice.TheirsThenOurs), new ConflictHunkResolution(0, ConflictChoice.Ours));

        Assert.AreEqual("uno\n2\n3\nEND\nFIN\n", text);
    }

    [TestMethod]
    public void Compose_UnlistedHunks_KeepTheirMarkers()
    {
        var document = Parse(TwoHunks);

        var result = ConflictMarkerParser.Compose(document, [new ConflictHunkResolution(0, ConflictChoice.Theirs)]);

        Assert.AreEqual(1, result.RemainingHunks);
        Assert.AreEqual("one\n2\n3\n<<<<<<< HEAD\nFIN\n=======\nEND\n>>>>>>> other\n", Encoding.UTF8.GetString(result.Content));
    }

    [TestMethod]
    public void Compose_CustomText_AddsMissingLineBreakInHunkStyle()
    {
        var lf = Parse(TwoHunks);
        Assert.AreEqual("merged\n2\n3\nFIN\n", Compose(lf, ConflictHunkResolution.Custom(0, "merged"), new ConflictHunkResolution(1, ConflictChoice.Ours)));

        var crlf = Parse("<<<<<<< HEAD\r\na\r\n=======\r\nb\r\n>>>>>>> other\r\nrest\r\n");
        Assert.AreEqual("x\r\nrest\r\n", Compose(crlf, ConflictHunkResolution.Custom(0, "x")));
    }

    [TestMethod]
    public void Compose_EmptyCustomText_RemovesTheHunk()
    {
        Assert.AreEqual("2\n3\nFIN\n", Compose(Parse(TwoHunks), ConflictHunkResolution.Custom(0, ""), new ConflictHunkResolution(1, ConflictChoice.Ours)));
    }

    [TestMethod]
    public void Compose_NonUtf8Bytes_AreCopiedUnchanged()
    {
        // Windows-1251 "Привет" outside and inside the hunk: invalid UTF-8 that must survive byte for byte.
        byte[] cyrillic = [0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2];
        var content = cyrillic.Concat("\n<<<<<<< HEAD\n"u8.ToArray()).Concat(cyrillic).Concat("\n=======\nb\n>>>>>>> other\n"u8.ToArray()).ToArray();

        var composed = ConflictMarkerParser.Compose(ConflictMarkerParser.Parse(content, 7), [new ConflictHunkResolution(0, ConflictChoice.OursThenTheirs)]).Content;

        CollectionAssert.AreEqual(cyrillic.Concat("\n"u8.ToArray()).Concat(cyrillic).Concat("\nb\n"u8.ToArray()).ToArray(), composed);
    }

    [TestMethod]
    public void Compose_InvalidResolutions_Throw()
    {
        var document = Parse(TwoHunks);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ConflictMarkerParser.Compose(document, [new ConflictHunkResolution(2, ConflictChoice.Ours)]));
        Assert.ThrowsException<ArgumentException>(() => ConflictMarkerParser.Compose(document,
            [new ConflictHunkResolution(0, ConflictChoice.Ours), new ConflictHunkResolution(0, ConflictChoice.Theirs)]));
        Assert.ThrowsException<ArgumentException>(() => new ConflictHunkResolution(0, ConflictChoice.Custom));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ConflictHunkResolution(-1, ConflictChoice.Ours));
        Assert.ThrowsException<ArgumentNullException>(() => ConflictHunkResolution.Custom(0, null!));
    }

    [TestMethod]
    public void GetChoiceLabel_UsesMarkerLabelsWithFallback()
    {
        var hunk = Parse(TwoHunks).Hunks[0];
        Assert.AreEqual("Use HEAD", hunk.GetChoiceLabel(ConflictChoice.Ours));
        Assert.AreEqual("Use other", hunk.GetChoiceLabel(ConflictChoice.Theirs));
        Assert.AreEqual("Use HEAD then other", hunk.GetChoiceLabel(ConflictChoice.OursThenTheirs));
        Assert.AreEqual("Use other then HEAD", hunk.GetChoiceLabel(ConflictChoice.TheirsThenOurs));

        var unlabeled = Parse("<<<<<<<\na\n=======\nb\n>>>>>>>\n").Hunks[0];
        Assert.AreEqual("Use ours then theirs", unlabeled.GetChoiceLabel(ConflictChoice.OursThenTheirs));
    }

    [TestMethod]
    public void IsBinary_DetectsNulInFirst8000Bytes()
    {
        Assert.IsTrue(ConflictMarkerParser.IsBinary([0x41, 0x00, 0x42]));
        Assert.IsFalse(ConflictMarkerParser.IsBinary("text\n"u8.ToArray()));
        Assert.IsFalse(ConflictMarkerParser.IsBinary([.. Enumerable.Repeat((byte)'a', 8000), 0x00]));
    }

    [TestMethod]
    public void ParseMarkerSizes_ReadsCheckAttrNulOutput()
    {
        var output = "a.txt\0conflict-marker-size\0unspecified\0docs/Read me.md\0conflict-marker-size\012\0bad\0conflict-marker-size\0-3\0set\0conflict-marker-size\0set\0";

        var sizes = ConflictMarkerParser.ParseMarkerSizes(output);

        Assert.AreEqual(7, sizes["a.txt"]);
        Assert.AreEqual(12, sizes["docs/Read me.md"]);
        Assert.AreEqual(7, sizes["bad"]);
        Assert.AreEqual(7, sizes["set"]);
    }
}
