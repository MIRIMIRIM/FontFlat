using System.Buffers.Binary;

namespace FontWeave.Tables.Variation;

/// <summary>Big-endian accessors for the variation line's hand-written boundaries -
/// the dynamic rows the schema declarations cannot express (fvar instance records,
/// avar segment maps, STAT format 4 tails). Read side only: the compilers keep their
/// own emission helpers next to their plans (card 2).</summary>
internal static class VariationWire
{
    internal static short I16(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(at, 2));

    internal static int I32(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(at, 4));

    internal static ushort U16(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));

    /// <summary>The F2DOT14 raw value of one segment-map coordinate.</summary>
    internal static short F2Dot14(ReadOnlySpan<byte> bytes, int at) => I16(bytes, at);
}

/// <summary>The wire value domains of the name-ID fields (card 1.2-II): the spec's
/// hard text for fvar's three fields, and the lean OQ-T5-02-2 reading for STAT's
/// conditional "defined name ID" wording (a small set of predefined IDs plus the
/// font-defined [256,32768) band; real witnesses elided=2 and valueNameID=2 pin the
/// ambiguity). Existence is never consulted - no name table is opened.</summary>
internal static class NameIDDomain
{
    internal const ushort NameIdUpperBound = 32768;
    internal const ushort FontDefinedLowerBound = 256;

    /// <summary>fvar axisNameID: strictly the font-defined band (spec hard text).</summary>
    internal static bool FvarAxisRefused(ushort value) =>
        value is < FontDefinedLowerBound or >= NameIdUpperBound;

    /// <summary>fvar subfamilyNameID: {2, 17} or the font-defined band.</summary>
    internal static bool SubfamilyRefused(ushort value) =>
        value is not (2 or 17) && (value is < FontDefinedLowerBound or >= NameIdUpperBound);

    /// <summary>fvar postScriptNameID: 6, 0xFFFF, or the font-defined band.</summary>
    internal static bool PostScriptRefused(ushort value) =>
        value is not (6 or 0xFFFF) && (value is < FontDefinedLowerBound or >= NameIdUpperBound);

    /// <summary>STAT axisNameID / elidedFallbackNameID: {2, 17} or the font-defined
    /// band (the lean wide form, OQ-T5-02-2).</summary>
    internal static bool StatAxisRefused(ushort value) => SubfamilyRefused(value);

    /// <summary>STAT valueNameID: {2, 17, 22} or the font-defined band
    /// (OQ-T5-02-2's lean reading; NotoDev's valueNameID=2 witness pins the width).</summary>
    internal static bool StatValueRefused(ushort value) =>
        value is not (2 or 17 or 22) && (value is < FontDefinedLowerBound or >= NameIdUpperBound);
}
