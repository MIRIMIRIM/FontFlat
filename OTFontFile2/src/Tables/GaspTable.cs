using System.Runtime.CompilerServices;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("gasp", 4)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("RangeCount", OtFieldKind.UInt16, 2)]
[OtSequentialRecordArray("Range", 4, 4, RecordTypeName = "GaspRange")]
public readonly partial struct GaspTable
{
    [Flags]
    public enum GaspBehavior : ushort
    {
        Gridfit = 0x0001,
        DoGray = 0x0002,
        SymmetricGridfit = 0x0004,
        SymmetricSmoothing = 0x0008
    }

    public readonly struct GaspRange
    {
        public ushort RangeMaxPpem { get; }
        public GaspBehavior Behavior { get; }

        public GaspRange(ushort rangeMaxPpem, GaspBehavior behavior)
        {
            RangeMaxPpem = rangeMaxPpem;
            Behavior = behavior;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GaspBehavior GetBehaviorForPpem(ushort ppem)
    {
        int count = RangeCount;
        for (int i = 0; i < count; i++)
        {
            if (!TryGetRange(i, out var range))
                break;

            if (ppem <= range.RangeMaxPpem)
                return range.Behavior;
        }

        return 0;
    }
}
