using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class LayoutContentSharingTests
{
    [Fact]
    public void FeatureBodiesShareByContentWithoutMergingIndicesOrParameters()
    {
        static LayoutFeature[] Features() =>
        [
            Feature(0x73733031, [0, 1, 0], [0, 0, 1, 44]),
            Feature(0x73733032, [0, 1, 0], [0, 0, 1, 44], 27),
            Feature(0x73733033, [0, 1, 0], [0, 0, 1, 45]),
            Feature(0x73733034, [1, 0, 1], [0, 0, 1, 44]),
            Feature(0x73733035, [0, 1, 0], null)
        ];
        Ok(LayoutListsTableCompiler.TryCompileFeatures(Features(), 2, out var bytes, out _));
        Ok(LayoutListsTableCompiler.TryCompileFeatures(Features(), 2, out var repeated, out _));

        Assert.Equal(bytes, repeated);
        Assert.Equal(5, Word(bytes!, 0));
        int first = TagTarget(bytes!, 0), same = TagTarget(bytes!, 1);
        int differentParams = TagTarget(bytes!, 2), differentIndices = TagTarget(bytes!, 3);
        int absentParams = TagTarget(bytes!, 4);
        Assert.Equal(first, same);
        Assert.NotEqual(first, differentParams);
        Assert.NotEqual(first, differentIndices);
        Assert.NotEqual(first, absentParams);
        Assert.Equal(first + Word(bytes!, first),
            differentIndices + Word(bytes!, differentIndices));
        Assert.NotEqual(first + Word(bytes!, first),
            differentParams + Word(bytes!, differentParams));
        Assert.Equal(0, Word(bytes!, absentParams));
        Assert.Equal(80, bytes!.Length);

        Ok(LayoutTagListView.TryOpen(bytes, out var view, out _));
        Ok(view.TryReadFeatures(2, out var features, out _));
        Assert.NotNull(features);
        Assert.Equal(new uint[] { 0x73733031, 0x73733032, 0x73733033, 0x73733034, 0x73733035 },
            features!.Select(feature => feature.Tag).ToArray());
        Assert.Equal(new ushort[] { 0, 1, 0 }, features[0].LookupIndices.ToArray());
        Assert.Equal(new ushort[] { 0, 1, 0 }, features[1].LookupIndices.ToArray());
        Assert.Equal(new ushort[] { 1, 0, 1 }, features[3].LookupIndices.ToArray());
        Assert.Equal(new byte[] { 0, 0, 1, 44 }, features[0].ParamsPayload);
        Assert.Equal(new byte[] { 0, 0, 1, 45 }, features[2].ParamsPayload);
        Assert.Null(features[4].ParamsPayload);
    }

    [Fact]
    public void ScriptAndLanguageBodiesShareWithoutChangingTagsOrRelativeOrigins()
    {
        LayoutScript[] scripts =
        [
            Script(0x44464C54, Language(ushort.MaxValue, [0, 1]),
                Language(ushort.MaxValue, [0, 1]), Language(0, [0, 1])),
            Script(0x6379726C, Language(ushort.MaxValue, [0, 1]),
                Language(ushort.MaxValue, [1, 0]), Language(0, [0, 1])),
            Script(0x6C61746E, Language(ushort.MaxValue, [0, 1]),
                Language(ushort.MaxValue, [0, 1]), Language(0, [0, 1]))
        ];
        Ok(LayoutListsTableCompiler.TryCompileScripts(scripts, 2, out var bytes, out _));

        Assert.Equal(3, Word(bytes!, 0));
        int first = TagTarget(bytes!, 0), different = TagTarget(bytes!, 1);
        Assert.Equal(first, TagTarget(bytes!, 2));
        Assert.NotEqual(first, different);
        int firstDefault = first + Word(bytes!, first);
        int differentDefault = different + Word(bytes!, different);
        Assert.Equal(firstDefault, differentDefault);
        Assert.Equal(firstDefault, first + Word(bytes!, first + 8));
        Assert.NotEqual(firstDefault, different + Word(bytes!, different + 8));
        Assert.NotEqual(firstDefault, first + Word(bytes!, first + 14));
        Assert.Equal(first + Word(bytes!, first + 14),
            different + Word(bytes!, different + 14));

        Ok(LayoutTagListView.TryOpen(bytes, out var view, out _));
        Ok(view.TryReadScripts(2, out var output, out _));
        Assert.NotNull(output);
        Assert.Equal(new uint[] { 0x44464C54, 0x6379726C, 0x6C61746E },
            output!.Select(script => script.Tag).ToArray());
        Assert.Equal(new ushort[] { 0, 1 }, output[0].DefaultLanguage!.FeatureIndices.ToArray());
        Assert.Equal(new ushort[] { 1, 0 }, output[1].Languages[0].Language.FeatureIndices.ToArray());
        Assert.Equal(0, output[0].Languages[1].Language.RequiredFeatureIndex);
        Assert.Equal(ushort.MaxValue, output[0].DefaultLanguage!.RequiredFeatureIndex);
        Assert.Equal(new uint[] { 0x454E4720, 0x46524120 },
            output[2].Languages.ToArray().Select(language => language.Tag).ToArray());
    }

    [Fact]
    public void LargeSharedLanguageBodyStaysReachableFromBothScriptRecords()
    {
        Ok(LayoutListsTableCompiler.TryCreateScript(0x44464C54,
            Language(ushort.MaxValue, new ushort[32768]), [], 2, out var first, out _));
        Ok(LayoutListsTableCompiler.TryCreateScript(0x6C61746E,
            Language(ushort.MaxValue, new ushort[32768]), [], 2, out var second, out _));

        Ok(LayoutListsTableCompiler.TryCompileScripts([first!, second!], 2,
            out var bytes, out _));

        Assert.Equal(65560, bytes!.Length);
        Assert.Equal(TagTarget(bytes, 0), TagTarget(bytes, 1));
        Ok(LayoutTagListView.TryOpen(bytes, out var view, out _));
        Ok(view.TryReadScripts(2, out var scripts, out _));
        Assert.Equal(2, scripts!.Length);
        foreach (var script in scripts)
        {
            Assert.Equal(32768, script.DefaultLanguage!.FeatureIndices.Length);
            Assert.All(script.DefaultLanguage.FeatureIndices.ToArray(), index => Assert.Equal(0, index));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContentSharingBudgetFailureDoesNotWriteDestination(bool workLimit)
    {
        var features = new[]
        {
            Feature(0x73733031, [0, 1, 0], [0, 0, 1, 44]),
            Feature(0x73733032, [0, 1, 0], [0, 0, 1, 44])
        };
        var destination = Enumerable.Repeat((byte)0xA5, 128).ToArray();
        var limits = workLimit
            ? new BuildLimits { MaxWorkUnits = 0 }
            : new BuildLimits { MaxTemporaryBytes = 0 };

        var status = LayoutListsTableCompiler.TryWriteFeatures(features, 2,
            destination, out var diagnostic, limits);

        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(workLimit ? DataField.WorkUnits : DataField.TemporaryBytes,
            diagnostic.Location.Field);
        Assert.All(destination, value => Assert.Equal((byte)0xA5, value));
    }

    private static LayoutFeature Feature(uint tag, ushort[] lookups, byte[]? payload,
        ushort rawOffset = 1)
    {
        Ok(LayoutListsTableCompiler.TryCreateFeature(tag,
            payload is null ? (ushort)0 : rawOffset, lookups, 2, out var feature,
            out _, new BuildBudget(), default, payload));
        return feature!;
    }

    private static LayoutLangSys Language(ushort required, ushort[] features)
    {
        Ok(LayoutListsTableCompiler.TryCreateLangSys(required, features, 2,
            out var language, out _));
        return language!;
    }

    private static LayoutScript Script(uint tag, LayoutLangSys language,
        LayoutLangSys english, LayoutLangSys french)
    {
        Ok(LayoutListsTableCompiler.TryCreateScript(tag, language,
            [new(0x454E4720, english), new(0x46524120, french)], 2,
            out var script, out _));
        return script!;
    }

    private static ushort Word(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    private static int TagTarget(byte[] bytes, int ordinal) => Word(bytes, 6 + 6 * ordinal);
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
