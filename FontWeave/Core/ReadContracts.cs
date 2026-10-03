using System.Buffers.Binary;

namespace FontWeave.Core;

// Explicit numeric values are part of the R1 diagnostic wire contract.
public enum ReadStatus : byte
{
    Uninitialized = 0, Done = 1, Truncated = 2, UnsupportedFormat = 3,
    InvalidOffset = 4, InvalidIndex = 5, DuplicateTable = 6, MissingTable = 7,
    InvalidFormat = 8, InvalidEncoding = 9, ResourceLimit = 10,
    InvalidSource = 11, NotFound = 12, DestinationTooSmall = 13
}

public enum DiagnosticCode : ushort
{
    None = 0, UnboundView = 1, TruncatedData = 2, UnsupportedVersion = 3,
    RangeOutsideSource = 4, IndexOutOfRange = 5, DuplicateTag = 6,
    TableNotFound = 7, InvalidValue = 8, InvalidUtf16 = 9, BudgetExceeded = 10,
    OverlappingRange = 11, StorageBeforeRecords = 12, WrongSourceFace = 13,
    GlyphNotMapped = 14, DuplicateGlyph = 15, DestinationTooSmall = 16,
    UnsupportedOperation = 17, UnknownTable = 18, SignatureRequiresDrop = 19,
    UnexpectedTrailingData = 20, DeprecatedValue = 21
}

public enum DataField : ushort
{
    None = 0, Signature = 1, Version = 2, Count = 3, Offset = 4, Length = 5,
    Tag = 6, StorageOffset = 7, Payload = 8, UnitsPerEm = 9, MagicNumber = 10,
    LanguageId = 11, GlyphId = 12, EncodingId = 13, Cid = 14, FdIndex = 15,
    InputBytes = 16, OutputBytes = 17, TemporaryBytes = 18, WorkUnits = 19,
    FontRevision = 20, ChecksumAdjustment = 21, Flags = 22,
    Created = 23, Modified = 24, XMin = 25, YMin = 26, XMax = 27,
    YMax = 28, MacStyle = 29, LowestRecPpem = 30,
    FontDirectionHint = 31, IndexToLocFormat = 32, GlyphDataFormat = 33,
    NumGlyphs = 34, MaxPoints = 35, MaxContours = 36,
    MaxCompositePoints = 37, MaxCompositeContours = 38, MaxZones = 39,
    MaxTwilightPoints = 40, MaxStorage = 41, MaxFunctionDefs = 42,
    MaxInstructionDefs = 43, MaxStackElements = 44, MaxSizeOfInstructions = 45,
    MaxComponentElements = 46, MaxComponentDepth = 47,
    PlatformId = 48, CharCode = 49
}

public enum OffsetBasis : byte { None = 0, File = 1, Table = 2, NameStorage = 3 }

/// <summary>Numeric provenance; does not keep an input snapshot alive. SourceId 0 is caller-owned bytes.</summary>
public readonly record struct DataLocation(long SourceId, int FaceIndex, uint TableTag,
    int RecordOrdinal, DataField Field, OffsetBasis Basis, ulong Offset, int TableOrdinal = -1)
{
    /// <summary>File offset of the table payload when this location is source-bound.</summary>
    public ulong? TableFileOffset { get; init; }

    /// <summary>File-coordinate counterpart of Offset, when provenance permits it.</summary>
    public ulong? AbsoluteOffset => Basis switch
    {
        OffsetBasis.File => Offset,
        OffsetBasis.Table when TableFileOffset is ulong start && Offset <= ulong.MaxValue - start => start + Offset,
        _ => null
    };

    public static DataLocation Anonymous(uint tableTag = 0) => new(0, -1, tableTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    public DataLocation At(DataField field, ulong offset, int recordOrdinal = -1) =>
        this with { Field = field, Offset = offset, RecordOrdinal = recordOrdinal };

    /// <summary>Translates a window-local position from this location's origin. If the
    /// origin is unknown or the coordinate cannot be represented, keeps source identity
    /// and the local position but does not claim a table or file coordinate.</summary>
    public DataLocation AtLocal(DataField field, ulong localOffset, int recordOrdinal = -1) =>
        Basis == OffsetBasis.None || localOffset > ulong.MaxValue - Offset
            ? this with { Field = field, Basis = OffsetBasis.None, Offset = localOffset,
                RecordOrdinal = recordOrdinal }
            : this with { Field = field, Offset = Offset + localOffset,
                RecordOrdinal = recordOrdinal };
}

/// <summary>Detail/Expected meaning is defined per code and field, never formatted in Core.</summary>
public readonly record struct FontDiagnostic(DiagnosticCode Code, DataLocation Location,
    ulong Detail = 0, ulong Expected = 0);

internal static class ReadWire
{
    internal static ushort U16(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));
    internal static uint U32(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
    internal static bool Slice(ReadOnlySpan<byte> bytes, ulong offset, ulong length,
        out ReadOnlySpan<byte> value)
    {
        value = default;
        if (offset > (ulong)bytes.Length || length > (ulong)bytes.Length - offset) return false;
        value = bytes.Slice((int)offset, (int)length);
        return true;
    }
    internal static ReadStatus Fail(ReadStatus status, DiagnosticCode code, DataLocation location,
        out FontDiagnostic diagnostic, ulong detail = 0, ulong expected = 0)
    {
        diagnostic = new(code, location, detail, expected);
        return status;
    }
}
