// CFF2 factory domain faces: wire counts are uint16 fields, and the plan owns their
// refusal — never the writer's narrowing.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class Cff2FactoryDomainTests
{
    private static Cff2FontDict EmptyFontDict() => new(
        new Cff2DictEntry(0x12,
            [new Cff2Operand(0, null), new Cff2Operand(0, null)], [], 0),
        null);

    // FDSelect format 4 states each fd as a uint16. The factory bounded formats 0 and 3
    // to 256 Font DICTs but left format 4's domain unchecked, so a model past the word
    // reached the writer and threw in a checked build.
    [Fact]
    public void Format4FontDictsPastTheWordAreRefused()
    {
        var fontDicts = Enumerable.Range(0, 65_537).Select(_ => EmptyFontDict()).ToArray();
        var topDict = new Cff2DictEntry[]
        {
            new(17, [new Cff2Operand(0, null)], [], 0),
            new(0x0C24, [new Cff2Operand(0, null)], [], 0),
        };

        var status = Cff2TableCompiler.TryCreate(topDict, [], [[14]], fontDicts, [65_536], 4,
            null, out var model, out _);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(model);
    }
}
