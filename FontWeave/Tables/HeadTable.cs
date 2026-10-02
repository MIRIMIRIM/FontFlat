using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Lossless head 1.0 scalar values; time fields are signed seconds since 1904-01-01 UTC.</summary>
public readonly record struct HeadTableValues(
    int FontRevisionRaw, uint ChecksumAdjustment, uint MagicNumber, ushort Flags,
    ushort UnitsPerEm, long CreatedSeconds, long ModifiedSeconds,
    short XMin, short YMin, short XMax, short YMax, ushort MacStyle,
    ushort LowestRecPpem, short FontDirectionHint, short IndexToLocFormat,
    short GlyphDataFormat);

/// <summary>Validated, independently materialized head 1.0 table.</summary>
public sealed class HeadTable
{
    internal HeadTable(HeadTableValues values, DataLocation location)
        : this(values, location, hasOriginalIndexToLocFormat: true) { }

    internal HeadTable(HeadTableValues values, DataLocation location,
        bool hasOriginalIndexToLocFormat)
    {
        Values = values; Location = location;
        HasOriginalIndexToLocFormat = hasOriginalIndexToLocFormat;
    }

    public ushort MajorVersion => (ushort)(HeadLayout.Version >> 16);
    public ushort MinorVersion => (ushort)(HeadLayout.Version & 0xFFFF);
    public HeadTableValues Values { get; }
    public DataLocation Location { get; }

    /// <summary>True only while IndexToLocFormat still matches the bytes this model was read
    /// from. Source-bound loca/glyf contexts require the original relation, not an edited one.</summary>
    internal bool HasOriginalIndexToLocFormat { get; }

    /// <summary>The original wire format selector, narrowed after strict validation.</summary>
    public short IndexToLocFormat => Values.IndexToLocFormat;
    public FontDiagnostic DeprecationDiagnostic => Values.FontDirectionHint == 2
        ? default
        : new(DiagnosticCode.DeprecatedValue, Location.At(DataField.FontDirectionHint, HeadLayout.FontDirectionHintAt),
            unchecked((ushort)Values.FontDirectionHint), 2);

    /// <summary>Construct another validated model; a changed fontDirectionHint must be 2.</summary>
    public ReadStatus TryWith(HeadTableValues values, out HeadTable? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        if (values.FontDirectionHint != Values.FontDirectionHint && values.FontDirectionHint != 2)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.FontDirectionHint, HeadLayout.FontDirectionHintAt), out diagnostic,
                unchecked((ushort)values.FontDirectionHint), 2);
        ReadStatus status = HeadView.ValidateValues(values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new HeadTable(values, Location,
            HasOriginalIndexToLocFormat && values.IndexToLocFormat == Values.IndexToLocFormat);
        return ReadStatus.Done;
    }
}
