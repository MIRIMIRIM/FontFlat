using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>CID-keyed CFF1 validation, on the same Cff1View as the name-keyed form.
///
/// CID-keyed CFF1 differs structurally from name-keyed, not merely in a few keys: the
/// Private DICT moves into each Font DICT, Encoding disappears (the sfnt cmap carries
/// encoding for CID fonts), and FDSelect maps glyphs to Font DICTs. The top-level DICT is
/// therefore parsed once by the shared path and branched here.
/// </summary>
public readonly ref partial struct Cff1View
{
    /// <summary>Strict rules for the CID-keyed form.
    ///
    /// Tiling: Header, Name INDEX, Top DICT INDEX, String INDEX, Global Subrs, then the
    /// offset targets charset / FDSelect / CharStrings / FDArray, then every FD's Private
    /// DICT and its Local Subrs. Anything left over is trailing data.
    /// </summary>
    private ReadStatus ValidateCid(List<DictEntryData> entries, int dictStart,
        IndexSpan strings, IndexSpan globals, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        // ROS must carry exactly three operands (registry SID, ordering SID, supplement).
        // Arity is enforced by the shared DICT parser, so the values are read here.
        if (!TryFindEntry(entries, Cff1Layout.OpRos, out var rosEntry))
            return CidMissing(entries, dictStart, Cff1Layout.OpRos, out diagnostic);
        ReadStatus status = EntryIntegers(rosEntry!, out var rosOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (rosOperands.Length != CidLayout.RosArity)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)rosEntry!.EntryStart), out diagnostic,
                (uint)rosOperands.Length, CidLayout.RosArity);
        if (rosOperands[0] < 0 || rosOperands[1] < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Cid, (uint)rosEntry!.OperandStart), out diagnostic,
                (uint)Math.Max(0, Math.Min(rosOperands[0], rosOperands[1])),
                (uint)Cff1Layout.StandardStringCount);

        // FDArray and FDSelect must both be present. One without the other leaves the glyph
        // to Font DICT mapping undefined, so it is refused rather than defaulted.
        bool hasFdArray = TryFindEntry(entries, Cff1Layout.OpFdArray, out var fdArrayEntry);
        bool hasFdSelect = TryFindEntry(entries, Cff1Layout.OpFdSelect, out var fdSelectEntry);
        if (!hasFdArray)
            return CidMissing(entries, dictStart, Cff1Layout.OpFdArray, out diagnostic);
        if (!hasFdSelect)
            return CidMissing(entries, dictStart, Cff1Layout.OpFdSelect, out diagnostic);

        if (!TryFindEntry(entries, Cff1Layout.OpCharStrings, out var charStringsEntry))
            return CidMissing(entries, dictStart, Cff1Layout.OpCharStrings, out diagnostic);
        status = EntryIntegers(charStringsEntry!, out var charStringsOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int charStringsAt = charStringsOperands[0];

        status = EntryIntegers(fdArrayEntry!, out var fdArrayOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int fdArrayAt = fdArrayOperands[0];
        status = EntryIntegers(fdSelectEntry!, out var fdSelectOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int fdSelectAt = fdSelectOperands[0];

        int cidCount = -1;
        if (TryFindEntry(entries, Cff1Layout.OpCidCount, out var cidCountEntry))
        {
            status = EntryIntegers(cidCountEntry!, out var cidCountOperands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            cidCount = cidCountOperands[0];
            if (cidCount < 1)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)cidCountEntry!.OperandStart),
                    out diagnostic, (uint)Math.Max(0, cidCount), 1);
        }

        // A top-level Private DICT is a name-keyed structure; CID keeps it per FD. Accepting
        // both would leave two competing sources for the same data.
        if (TryFindEntry(entries, Cff1Layout.OpPrivate, out var strayPrivate))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)strayPrivate!.EntryStart), out diagnostic,
                (uint)Cff1Layout.OpPrivate, 0);

        int charsetValue = 0;
        if (TryFindEntry(entries, Cff1Layout.OpCharset, out var charsetEntry))
        {
            status = EntryIntegers(charsetEntry!, out var charsetOperands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            charsetValue = charsetOperands[0];
        }

        if (charStringsAt < 0 || charStringsAt > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, charStringsAt)),
                out diagnostic, (uint)Math.Max(0, charStringsAt), (ulong)_data.Length);

        // SID operands in the top DICT (ROS registry/ordering and the string keys) must fit
        // the String INDEX, exactly as in the name-keyed form.
        int sidBound = Cff1Layout.StandardStringCount + strings.Count;
        foreach (var candidate in entries)
        {
            if (!Cff1Layout.IsSidOperator(candidate.Operator)) continue;
            status = EntryIntegers(candidate, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < operands.Length; i++)
            {
                if (operands[i] < 0 || operands[i] >= sidBound)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.Cid, (uint)candidate.OperandStart), out diagnostic,
                        (uint)Math.Max(0, operands[i]), (uint)sidBound);
            }
        }
        if (rosOperands[0] >= sidBound || rosOperands[1] >= sidBound)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Cid, (uint)rosEntry!.OperandStart), out diagnostic,
                (uint)Math.Max(rosOperands[0], rosOperands[1]), (uint)sidBound);
        if (TryFindEntry(entries, Cff1Layout.OpCharstringType, out var typeEntry))
        {
            status = EntryIntegers(typeEntry!, out var typeOperands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (typeOperands[0] != 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)typeEntry!.EntryStart), out diagnostic,
                    (uint)typeOperands[0], 2);
        }

        // --- block extents ---------------------------------------------------------
        // The offset targets of a CID font are NOT in a fixed order. Real witnesses lay
        // them out as charset, FDSelect, then FDArray and CharStrings in EITHER order, with
        // each FD's Private DICT and Local Subrs packed into the same region. So instead of
        // assuming a sequence, every block is located and then required to tile the region
        // after the Global Subrs INDEX exactly: no gaps, no overlaps, whole table consumed.
        var blocks = new List<CidSpan>(8);
        int numGlyphs;
        status = LocateIndex(charStringsAt, out var charStringsIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        numGlyphs = charStringsIndex.Count;
        if (numGlyphs < 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)charStringsAt), out diagnostic, 0, 1);
        if (cidCount >= 0 && cidCount < numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)cidCountEntry!.OperandStart),
                out diagnostic, (uint)cidCount, (uint)numGlyphs);
        blocks.Add(new CidSpan(charStringsAt, charStringsIndex.End));

        // charset: a CID font's charset maps glyphs to CIDs, so the CID space bounds it, and
        // CIDCount is that bound when the font declares it.
        if (charsetValue is 0 or 1 or 2)
        {
            // The predefined charsets are name-keyed constructs; on a CID font they carry no
            // CID mapping. Refused explicitly rather than silently treated as identity.
            if (numGlyphs > Cff1Predefined.CharsetLength(charsetValue))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)globals.End, 0), out diagnostic,
                    (uint)numGlyphs, (uint)(Cff1Predefined.CharsetLength(charsetValue) + 1));
        }
        else
        {
            int cidBound = cidCount >= 0 ? cidCount : CidLayout.MaxCidCount;
            status = ValidateCustomCharset(charsetValue, globals.End, numGlyphs, cidBound,
                budget, out int charsetEnd, out diagnostic, requireTiling: false);
            if (status != ReadStatus.Done) return status;
            blocks.Add(new CidSpan(charsetValue, charsetEnd));
        }

        // FDSelect
        status = ValidateFdSelect(fdSelectAt, numGlyphs, budget,
            out int fdCount, out int fdSelectEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        blocks.Add(new CidSpan(fdSelectAt, fdSelectEnd));

        // FDArray
        status = LocateIndex(fdArrayAt, out var fdArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (fdArray.Count < 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)fdArrayAt), out diagnostic, 0, 1);
        if (fdArray.Count > CidLayout.MaxFdCount)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Count, (uint)fdArrayAt), out diagnostic,
                (uint)fdArray.Count, CidLayout.MaxFdCount);
        // Every FD the mapping reaches must exist in the FDArray. The converse is not
        // required: the FDArray may carry FDs no glyph references, which is legal and occurs
        // as soon as an edit moves the last glyph off an FD.
        if (fdCount > fdArray.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)fdSelectAt, 0), out diagnostic,
                (uint)fdCount, (uint)fdArray.Count);
        blocks.Add(new CidSpan(fdArrayAt, fdArray.End));

        // --- each Font DICT's Private DICT and Local Subrs -------------------------
        long privateBytes = 0, subrBytes = 0;
        for (int fd = 0; fd < fdArray.Count; fd++)
        {
            status = IndexItemBounds(fdArray, fd, out int fontDictStart, out int fontDictLength,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ParseDict(fontDictStart, fontDictLength, Cff1DictScope.FontDict,
                out var fontEntries, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // A Font DICT describes one FD; nested CID keys would make the mapping ambiguous.
            if (TryFindEntry(fontEntries, Cff1Layout.OpRos, out var nested) ||
                TryFindEntry(fontEntries, Cff1Layout.OpFdArray, out nested) ||
                TryFindEntry(fontEntries, Cff1Layout.OpFdSelect, out nested))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)nested!.EntryStart), out diagnostic,
                    (uint)nested.Operator, 0);
            if (!TryFindEntry(fontEntries, Cff1Layout.OpPrivate, out var privateEntry))
            {
                // An FD without a Private DICT is legal: it simply has no local hints.
                continue;
            }
            status = EntryIntegers(privateEntry!, out var privateOperands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (privateOperands.Length != 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)privateEntry!.EntryStart), out diagnostic,
                    (uint)privateOperands.Length, 2);
            int privateSize = privateOperands[0], privateOffset = privateOperands[1];
            if (privateSize < 0 || privateOffset < 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Offset, (uint)privateEntry!.EntryStart), out diagnostic,
                    (ulong)Math.Max(0, Math.Min(privateSize, privateOffset)), 0);
            if ((long)privateOffset + privateSize > _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.Length, (ulong)(uint)(privateOffset + privateSize)),
                    out diagnostic, (ulong)((long)privateOffset + privateSize),
                    (ulong)_data.Length);
            status = ParseDict(privateOffset, privateSize, Cff1DictScope.Private,
                out var privateDictEntries, out diagnostic);
            if (status != ReadStatus.Done) return status;
            privateBytes += privateSize;

            // An FD owns one contiguous block: its Private DICT followed, when the Subrs key
            // is present, by that FD's Local Subrs INDEX. The Subrs offset is relative to the
            // Private DICT start and must equal its size, exactly as in the name-keyed form.
            int fdBlockEnd = privateOffset + privateSize;
            if (TryFindEntry(privateDictEntries, Cff1Layout.OpSubrs, out var subrsEntry))
            {
                status = EntryIntegers(subrsEntry!, out var subrsOperands, out diagnostic);
                if (status != ReadStatus.Done) return status;
                int subrsRelative = subrsOperands[0];
                // Subrs is relative to the Private DICT start, as in the name-keyed form, but
                // unlike that form it need not equal the Private DICT size. The largest real
                // witness packs every FD's Private DICT contiguously and points each Subrs
                // offset at a Local Subrs INDEX in a later region of the table.
                if (subrsRelative < privateSize)
                    return ReadWire.Fail(ReadStatus.InvalidOffset,
                        DiagnosticCode.OverlappingRange,
                        Location.At(DataField.Offset,
                            (uint)(privateOffset + Math.Max(0, subrsRelative)), fd),
                        out diagnostic, (uint)Math.Max(0, subrsRelative), (uint)privateSize);
                long localAt = (long)privateOffset + subrsRelative;
                if (localAt > _data.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset,
                        DiagnosticCode.RangeOutsideSource,
                        Location.At(DataField.Offset, (uint)localAt, fd), out diagnostic,
                        (ulong)localAt, (ulong)_data.Length);
                status = LocateIndex((int)localAt, out var localSubrs, out diagnostic);
                if (status != ReadStatus.Done) return status;
                subrBytes += localSubrs.Length;
                // A Private DICT and its Local Subrs are two separate blocks, not one. The
                // largest real witness packs every FD's Private DICT together and places the
                // Local Subrs INDEXes elsewhere, so they must be recorded and tiled apart.
                blocks.Add(new CidSpan(localSubrs.Start, localSubrs.End));
            }
            // No two FDs may claim the same bytes: sharing a Private DICT or Local Subrs would
            // make the per-FD hint data ambiguous, which is the whole point of the FD split.
            // The overlap test is deferred to the whole-table tiling check below.
            blocks.Add(new CidSpan(privateOffset, privateOffset + privateSize));
        }

        if (!budget.Work(privateBytes + subrBytes, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        // Every block must tile the region after the Global Subrs INDEX: sorted by start
        // position they begin at or after the Global Subrs INDEX end, each begins at or
        // after the previous end, and the last ends at or before the table end. Overlap
        // and non-zero fill still refuse; zero alignment fill is legal.
        blocks.Sort(static (a, b) => a.First.CompareTo(b.First));
        long cursor = globals.End;
        foreach (var block in blocks)
        {
            if (block.First == cursor) { cursor = block.Last; continue; }
            if (block.First < cursor)
                return ReadWire.Fail(ReadStatus.InvalidOffset,
                    DiagnosticCode.OverlappingRange,
                    Location.At(DataField.Offset, (uint)block.First), out diagnostic,
                    (uint)block.First, (uint)cursor);
            if (!IsZeroPadding((int)cursor, (int)block.First))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, (uint)cursor), out diagnostic,
                    (uint)block.First, (uint)cursor);
            cursor = block.Last;
        }
        if (cursor > _data.Length || (cursor < _data.Length && !IsZeroPadding((int)cursor, _data.Length)))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, (uint)cursor), out diagnostic,
                (ulong)_data.Length, (uint)cursor);

        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>FDSelect strict rules for formats 0 and 3.
    ///
    /// TN5176 defines exactly these two formats for CFF1. Format 4 belongs to CFF2 and any
    /// unknown value is refused explicitly, never reinterpreted as a known format.
    /// </summary>
    private ReadStatus ValidateFdSelect(int offset, int numGlyphs,
        BuildBudget budget, out int fdCount, out int blockEnd, out FontDiagnostic diagnostic)
    {
        fdCount = 0;
        blockEnd = offset;
        if (offset < 0 || offset > _data.Length - CidLayout.FdSelectMinimum)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)Math.Max(0, offset)), out diagnostic,
                (uint)Math.Max(0, offset), (ulong)_data.Length);
        int format = _data[offset];
        if (format == 0)
        {
            long end = (long)offset + 1 + numGlyphs;
            if (end > _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)end);
            if (!budget.Work(numGlyphs, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            int max = 0;
            for (int gid = 0; gid < numGlyphs; gid++)
            {
                int fd = _data[offset + 1 + gid];
                if (fd > max) max = fd;
            }
            fdCount = max + 1;
            blockEnd = (int)end;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (format != 3)
        {
            // Includes format 4 (CFF2). The diagnostic names the format so the two cases are
            // distinguishable, and the value is not rewritten into a known form.
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, (uint)offset), out diagnostic, (uint)format,
                CidLayout.FdSelectFormat3);
        }
        if (offset + 3 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)offset), out diagnostic,
                (ulong)_data.Length, (ulong)(offset + 3));
        int nRanges = ReadWire.U16(_data, offset + 1);
        if (nRanges == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)(offset + 1)), out diagnostic, 0, 1);
        if (nRanges > CidLayout.MaxFdSelectRanges)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Count, (uint)(offset + 1)), out diagnostic,
                (uint)nRanges, CidLayout.MaxFdSelectRanges);
        long rangesEnd = (long)offset + 3 + (long)nRanges * CidLayout.FdSelectRangeStride;
        long sentinelEnd = rangesEnd + 2;
        if (sentinelEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)offset), out diagnostic,
                (ulong)_data.Length, (ulong)sentinelEnd);
        if (!budget.Work(nRanges, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int previousFirst = -1, maxFd = 0;
        for (int i = 0; i < nRanges; i++)
        {
            int at = offset + 3 + i * CidLayout.FdSelectRangeStride;
            int first = ReadWire.U16(_data, at);
            int fd = _data[at + 2];
            if (i == 0 && first != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)at, i), out diagnostic,
                    (uint)first, 0);
            if (i > 0 && first <= previousFirst)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)at, i), out diagnostic,
                    (uint)first, (uint)previousFirst + 1);
            previousFirst = first;
            if (fd > maxFd) maxFd = fd;
        }
        int sentinel = ReadWire.U16(_data, (int)rangesEnd);
        // The sentinel closes the last range, so it must equal the glyph count exactly.
        if (sentinel != numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)rangesEnd, nRanges), out diagnostic,
                (uint)sentinel, (uint)numGlyphs);
        // Every run start must lie inside the glyph domain the sentinel closes. Without this
        // a final run could start at or beyond the sentinel and still satisfy the ordering
        // checks, which would make the run table claim glyphs that do not exist and silently
        // move in-domain glyphs onto the wrong FD.
        for (int i = 0; i < nRanges; i++)
        {
            int at = offset + 3 + i * CidLayout.FdSelectRangeStride;
            int first = ReadWire.U16(_data, at);
            if (first >= sentinel)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)at, i), out diagnostic,
                    (uint)first, (uint)(sentinel - 1));
        }
        fdCount = maxFd + 1;
        blockEnd = (int)sentinelEnd;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CidMissing(List<DictEntryData> entries, int dictStart, int op,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.At(DataField.Payload, (uint)dictStart), out diagnostic, (uint)op, 0);
}

/// <summary>CID-keyed layout constants and explicit budgets.</summary>
internal static class CidLayout
{
    /// <summary>ROS carries registry SID, ordering SID and supplement.</summary>
    internal const int RosArity = 3;

    /// <summary>TN5176 defines formats 0 and 3 for CFF1; format 4 is a CFF2 addition.</summary>
    internal const int FdSelectFormat0 = 0, FdSelectFormat3 = 3;

    /// <summary>A format byte plus at least something after it.</summary>
    internal const int FdSelectMinimum = 2;

    /// <summary>first(u16) + fd(u8).</summary>
    internal const int FdSelectRangeStride = 3;

    /// <summary>Explicit FD count ceiling. The largest real witness uses 256 FDs.</summary>
    internal const int MaxFdCount = 1 << 16;

    /// <summary>Explicit FDSelect range ceiling. The largest real witness uses 113.</summary>
    internal const int MaxFdSelectRanges = 1 << 16;

    /// <summary>Upper bound on a CID when the font omits CIDCount. CIDs are 16-bit.</summary>
    internal const int MaxCidCount = 1 << 16;
}

/// <summary>A half-open byte span used for CID overlap checks.</summary>
internal readonly record struct CidSpan(long First, long Last)
{
    internal bool Overlaps(CidSpan other) => First < other.Last && other.First < Last;
}
