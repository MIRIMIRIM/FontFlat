using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>maxp</c> table.
/// </summary>
[OtTableBuilder("maxp")]
public sealed partial class MaxpTableBuilder : ISfntTableSource
{
    private const uint Version05 = 0x00005000u;
    private const uint Version10 = 0x00010000u;

    private Fixed1616 _tableVersionNumber = new(Version10);
    private ushort _numGlyphs;

    // Version 1.0 fields.
    private ushort _maxPoints;
    private ushort _maxContours;
    private ushort _maxCompositePoints;
    private ushort _maxCompositeContours;
    private ushort _maxZones;
    private ushort _maxTwilightPoints;
    private ushort _maxStorage;
    private ushort _maxFunctionDefs;
    private ushort _maxInstructionDefs;
    private ushort _maxStackElements;
    private ushort _maxSizeOfInstructions;
    private ushort _maxComponentElements;
    private ushort _maxComponentDepth;

    public Fixed1616 TableVersionNumber
    {
        get => _tableVersionNumber;
        set
        {
            uint raw = value.RawValue;
            if (raw is not (Version05 or Version10))
                throw new ArgumentOutOfRangeException(nameof(value), "maxp version must be 0x00005000 (0.5) or 0x00010000 (1.0).");

            if (value == _tableVersionNumber)
                return;

            _tableVersionNumber = value;
            MarkDirty();
        }
    }

    public bool IsTrueTypeMaxp => TableVersionNumber.RawValue == Version10;

    public ushort NumGlyphs
    {
        get => _numGlyphs;
        set
        {
            if (value == _numGlyphs)
                return;

            _numGlyphs = value;
            MarkDirty();
        }
    }

    public ushort MaxPoints
    {
        get => _maxPoints;
        set
        {
            if (value == _maxPoints)
                return;

            _maxPoints = value;
            MarkDirty();
        }
    }

    public ushort MaxContours
    {
        get => _maxContours;
        set
        {
            if (value == _maxContours)
                return;

            _maxContours = value;
            MarkDirty();
        }
    }

    public ushort MaxCompositePoints
    {
        get => _maxCompositePoints;
        set
        {
            if (value == _maxCompositePoints)
                return;

            _maxCompositePoints = value;
            MarkDirty();
        }
    }

    public ushort MaxCompositeContours
    {
        get => _maxCompositeContours;
        set
        {
            if (value == _maxCompositeContours)
                return;

            _maxCompositeContours = value;
            MarkDirty();
        }
    }

    public ushort MaxZones
    {
        get => _maxZones;
        set
        {
            if (value == _maxZones)
                return;

            _maxZones = value;
            MarkDirty();
        }
    }

    public ushort MaxTwilightPoints
    {
        get => _maxTwilightPoints;
        set
        {
            if (value == _maxTwilightPoints)
                return;

            _maxTwilightPoints = value;
            MarkDirty();
        }
    }

    public ushort MaxStorage
    {
        get => _maxStorage;
        set
        {
            if (value == _maxStorage)
                return;

            _maxStorage = value;
            MarkDirty();
        }
    }

    public ushort MaxFunctionDefs
    {
        get => _maxFunctionDefs;
        set
        {
            if (value == _maxFunctionDefs)
                return;

            _maxFunctionDefs = value;
            MarkDirty();
        }
    }

    public ushort MaxInstructionDefs
    {
        get => _maxInstructionDefs;
        set
        {
            if (value == _maxInstructionDefs)
                return;

            _maxInstructionDefs = value;
            MarkDirty();
        }
    }

    public ushort MaxStackElements
    {
        get => _maxStackElements;
        set
        {
            if (value == _maxStackElements)
                return;

            _maxStackElements = value;
            MarkDirty();
        }
    }

    public ushort MaxSizeOfInstructions
    {
        get => _maxSizeOfInstructions;
        set
        {
            if (value == _maxSizeOfInstructions)
                return;

            _maxSizeOfInstructions = value;
            MarkDirty();
        }
    }

    public ushort MaxComponentElements
    {
        get => _maxComponentElements;
        set
        {
            if (value == _maxComponentElements)
                return;

            _maxComponentElements = value;
            MarkDirty();
        }
    }

    public ushort MaxComponentDepth
    {
        get => _maxComponentDepth;
        set
        {
            if (value == _maxComponentDepth)
                return;

            _maxComponentDepth = value;
            MarkDirty();
        }
    }

    public static bool TryFrom(MaxpTable maxp, out MaxpTableBuilder builder)
    {
        builder = null!;

        uint version = maxp.TableVersionNumber.RawValue;
        if (version is not (Version05 or Version10))
            return false;

        if (version == Version10 && maxp.Table.Length < 32)
            return false;

        var b = new MaxpTableBuilder
        {
            TableVersionNumber = maxp.TableVersionNumber,
            NumGlyphs = maxp.NumGlyphs
        };

        if (version == Version10)
        {
            if (!maxp.TryGetTrueTypeFields(out var tt))
                return false;

            b.MaxPoints = tt.MaxPoints;
            b.MaxContours = tt.MaxContours;
            b.MaxCompositePoints = tt.MaxCompositePoints;
            b.MaxCompositeContours = tt.MaxCompositeContours;
            b.MaxZones = tt.MaxZones;
            b.MaxTwilightPoints = tt.MaxTwilightPoints;
            b.MaxStorage = tt.MaxStorage;
            b.MaxFunctionDefs = tt.MaxFunctionDefs;
            b.MaxInstructionDefs = tt.MaxInstructionDefs;
            b.MaxStackElements = tt.MaxStackElements;
            b.MaxSizeOfInstructions = tt.MaxSizeOfInstructions;
            b.MaxComponentElements = tt.MaxComponentElements;
            b.MaxComponentDepth = tt.MaxComponentDepth;
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        uint version = TableVersionNumber.RawValue;
        int length = version == Version10 ? 32 : 6;
        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, version);
        BigEndian.WriteUInt16(span, 4, NumGlyphs);

        if (version == Version10)
        {
            BigEndian.WriteUInt16(span, 6, MaxPoints);
            BigEndian.WriteUInt16(span, 8, MaxContours);
            BigEndian.WriteUInt16(span, 10, MaxCompositePoints);
            BigEndian.WriteUInt16(span, 12, MaxCompositeContours);
            BigEndian.WriteUInt16(span, 14, MaxZones);
            BigEndian.WriteUInt16(span, 16, MaxTwilightPoints);
            BigEndian.WriteUInt16(span, 18, MaxStorage);
            BigEndian.WriteUInt16(span, 20, MaxFunctionDefs);
            BigEndian.WriteUInt16(span, 22, MaxInstructionDefs);
            BigEndian.WriteUInt16(span, 24, MaxStackElements);
            BigEndian.WriteUInt16(span, 26, MaxSizeOfInstructions);
            BigEndian.WriteUInt16(span, 28, MaxComponentElements);
            BigEndian.WriteUInt16(span, 30, MaxComponentDepth);
        }

        return table;
    }
}
