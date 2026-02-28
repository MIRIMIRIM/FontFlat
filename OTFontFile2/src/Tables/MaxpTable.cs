using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("maxp", 6)]
[OtField("TableVersionNumber", OtFieldKind.Fixed1616, 0)]
[OtField("NumGlyphs", OtFieldKind.UInt16, 4)]
[OtField("MaxPoints", OtFieldKind.UInt16, 6, InView = false)]
[OtField("MaxContours", OtFieldKind.UInt16, 8, InView = false)]
[OtField("MaxCompositePoints", OtFieldKind.UInt16, 10, InView = false)]
[OtField("MaxCompositeContours", OtFieldKind.UInt16, 12, InView = false)]
[OtField("MaxZones", OtFieldKind.UInt16, 14, InView = false)]
[OtField("MaxTwilightPoints", OtFieldKind.UInt16, 16, InView = false)]
[OtField("MaxStorage", OtFieldKind.UInt16, 18, InView = false)]
[OtField("MaxFunctionDefs", OtFieldKind.UInt16, 20, InView = false)]
[OtField("MaxInstructionDefs", OtFieldKind.UInt16, 22, InView = false)]
[OtField("MaxStackElements", OtFieldKind.UInt16, 24, InView = false)]
[OtField("MaxSizeOfInstructions", OtFieldKind.UInt16, 26, InView = false)]
[OtField("MaxComponentElements", OtFieldKind.UInt16, 28, InView = false)]
[OtField("MaxComponentDepth", OtFieldKind.UInt16, 30, InView = false)]
public readonly partial struct MaxpTable
{
    public bool IsTrueTypeMaxp => TableVersionNumber.RawValue == 0x00010000;

    public readonly struct TrueTypeFields
    {
        public ushort MaxPoints { get; }
        public ushort MaxContours { get; }
        public ushort MaxCompositePoints { get; }
        public ushort MaxCompositeContours { get; }
        public ushort MaxZones { get; }
        public ushort MaxTwilightPoints { get; }
        public ushort MaxStorage { get; }
        public ushort MaxFunctionDefs { get; }
        public ushort MaxInstructionDefs { get; }
        public ushort MaxStackElements { get; }
        public ushort MaxSizeOfInstructions { get; }
        public ushort MaxComponentElements { get; }
        public ushort MaxComponentDepth { get; }

        public TrueTypeFields(
            ushort maxPoints,
            ushort maxContours,
            ushort maxCompositePoints,
            ushort maxCompositeContours,
            ushort maxZones,
            ushort maxTwilightPoints,
            ushort maxStorage,
            ushort maxFunctionDefs,
            ushort maxInstructionDefs,
            ushort maxStackElements,
            ushort maxSizeOfInstructions,
            ushort maxComponentElements,
            ushort maxComponentDepth)
        {
            MaxPoints = maxPoints;
            MaxContours = maxContours;
            MaxCompositePoints = maxCompositePoints;
            MaxCompositeContours = maxCompositeContours;
            MaxZones = maxZones;
            MaxTwilightPoints = maxTwilightPoints;
            MaxStorage = maxStorage;
            MaxFunctionDefs = maxFunctionDefs;
            MaxInstructionDefs = maxInstructionDefs;
            MaxStackElements = maxStackElements;
            MaxSizeOfInstructions = maxSizeOfInstructions;
            MaxComponentElements = maxComponentElements;
            MaxComponentDepth = maxComponentDepth;
        }
    }

    public bool TryGetTrueTypeFields(out TrueTypeFields fields)
    {
        fields = default;

        if (!IsTrueTypeMaxp)
            return false;

        if (_table.Length < 32)
            return false;

        var data = _table.Span;
        fields = new TrueTypeFields(
            maxPoints: BigEndian.ReadUInt16(data, 6),
            maxContours: BigEndian.ReadUInt16(data, 8),
            maxCompositePoints: BigEndian.ReadUInt16(data, 10),
            maxCompositeContours: BigEndian.ReadUInt16(data, 12),
            maxZones: BigEndian.ReadUInt16(data, 14),
            maxTwilightPoints: BigEndian.ReadUInt16(data, 16),
            maxStorage: BigEndian.ReadUInt16(data, 18),
            maxFunctionDefs: BigEndian.ReadUInt16(data, 20),
            maxInstructionDefs: BigEndian.ReadUInt16(data, 22),
            maxStackElements: BigEndian.ReadUInt16(data, 24),
            maxSizeOfInstructions: BigEndian.ReadUInt16(data, 26),
            maxComponentElements: BigEndian.ReadUInt16(data, 28),
            maxComponentDepth: BigEndian.ReadUInt16(data, 30));
        return true;
    }
}
