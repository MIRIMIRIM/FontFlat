using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Rewrites one charset entry, keeping the charset's wire format.</summary>
public sealed record CidCharsetEdit(int GlyphId, int Cid);

/// <summary>Rewrites one FDSelect format 3 run. A null member keeps the current value.</summary>
public sealed record CidFdRangeEdit(int Ordinal, int? FirstGlyph, int? FdIndex);

/// <summary>Rewrites an existing scalar in one FD's Private DICT. Only keys already present
/// can be changed; adding a key would change the Private DICT size and is refused.</summary>
public sealed record CidPrivateValueEdit(int FdIndex, int Operator, int Value);

/// <summary>Replaces one CharStrings program. The bytes are stored verbatim and never
/// interpreted.</summary>
public sealed record CidCharStringEdit(int GlyphId, byte[] Program);

/// <summary>Replaces one Local Subrs program for one FD. The bytes are stored verbatim.</summary>
public sealed record CidLocalSubrEdit(int FdIndex, int Ordinal, byte[] Program);

/// <summary>Edits applied when recompiling a CID-keyed table.
///
/// Deliberately excluded, and refused rather than approximated: adding or removing FDs
/// (which would renumber every glyph's FD), converting between FDSelect formats, converting
/// charset formats, remapping GIDs and renumbering CIDs.</summary>
public sealed record CidEditPlan
{
    public IReadOnlyList<CidCharsetEdit> CharsetItems { get; init; } = [];
    public IReadOnlyList<CidFdRangeEdit> FdRanges { get; init; } = [];
    public IReadOnlyList<CidPrivateValueEdit> PrivateValues { get; init; } = [];
    public IReadOnlyList<CidCharStringEdit> CharStrings { get; init; } = [];
    public IReadOnlyList<CidLocalSubrEdit> LocalSubrs { get; init; } = [];
}

/// <summary>Canonical recompilation of a CID-keyed CFF1 table.
///
/// The emitted layout is fixed: header, Name INDEX, Top DICT INDEX, String INDEX, Global
/// Subrs, then charset, FDSelect, CharStrings and FDArray, then every FD's Private DICT and
/// finally the Local Subrs INDEXes. That is not how every real font is laid out — one
/// witness places FDArray before CharStrings and another puts CharStrings last — so a
/// recompiled table is required to preserve the model and the payload bytes, not the
/// original byte order of the offset targets.
///
/// Everything the plan does not touch is re-emitted from the model verbatim: programs and
/// Private DICT bytes are copied, never re-encoded.</summary>
public static class CidCff1TableCompiler
{
    public static ReadStatus TryCompile(CidCff1Table model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, new CidEditPlan(), out bytes, out diagnostic, limits);

    public static ReadStatus TryCompile(CidCff1Table model, CidEditPlan plan,
        out byte[]? bytes, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(1L + model.NumGlyphs + model.FdCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        ReadStatus status = Prepare(model, plan, out var layout, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Output(layout.TotalLength, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        var output = new byte[layout.TotalLength];
        status = Emit(model, plan, layout, output, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(CidCff1Table model, CidEditPlan plan,
        Span<byte> destination, out int written, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        written = 0;
        ReadStatus status = TryCompile(model, plan, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location, out diagnostic, (ulong)bytes.Length,
                (ulong)destination.Length);
        bytes.CopyTo(destination);
        written = bytes.Length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Compiles, then reads the result back, so the returned model is the one the
    /// strict reader accepts rather than an unverified claim.</summary>
    public static ReadStatus TryApply(CidCff1Table model, CidEditPlan plan,
        out CidCff1Table? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = TryCompile(model, plan, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        status = Cff1View.TryOpen(bytes!, out var view, out diagnostic, model.Location);
        if (status != ReadStatus.Done) return status;
        return view.TryReadCid(out updated, out diagnostic, limits);
    }

    /// <summary>Everything needed to lay the table out, resolved before a byte is written.</summary>
    private sealed class Layout
    {
        /// <summary>Charset, FDSelect, CharStrings and FDArray offsets, in that order.</summary>
        internal int[] TopReplacements = new int[4];
        internal byte[] TopDict = [];
        internal byte[] Charset = [];
        internal byte[] FdSelect = [];
        internal byte[][] CharStrings = [];
        internal byte[] CharStringsIndex = [];
        internal byte[][] FontDicts = [];
        internal byte[] FdArrayIndex = [];
        internal byte[][] PrivateDicts = [];
        /// <summary>Local Subrs programs, one array per FD.</summary>
        internal byte[][][] LocalSubrs = [];
        internal int CharsetAt, FdSelectAt, CharStringsAt, FdArrayAt;
        internal int[] PrivateAt = [];
        internal int[] LocalSubrsAt = [];
        internal int HeaderLength, NameAt, TopAt, StringAt, GlobalAt, TailAt;
        internal int TotalLength;
    }

    private static ReadStatus Prepare(CidCff1Table model, CidEditPlan plan, out Layout layout,
        out FontDiagnostic diagnostic)
    {
        layout = new Layout();
        diagnostic = default;

        // --- charset ---------------------------------------------------------------
        var cids = model.CharsetCids.ToArray();
        foreach (var edit in plan.CharsetItems)
        {
            if (model.CharsetForm.Predefined)
                return Unsupported(model.Location, out diagnostic,
                    "charset edits need a custom charset");
            if (edit.GlyphId < 1 || edit.GlyphId >= cids.Length)
                return ReadWire.Fail(ReadStatus.InvalidIndex,
                    DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, edit.GlyphId), out diagnostic,
                    unchecked((ulong)(long)edit.GlyphId), (ulong)cids.Length);
            if (edit.Cid < 0 || (model.CidCount >= 0 && edit.Cid >= model.CidCount))
                return ReadWire.Fail(ReadStatus.InvalidIndex,
                    DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Cid, 0, edit.GlyphId), out diagnostic,
                    (uint)Math.Max(0, edit.Cid),
                    (uint)(model.CidCount >= 0 ? model.CidCount : CidLayout.MaxCidCount));
            cids[edit.GlyphId] = edit.Cid;
        }
        int charsetLength = (int)Cff1TableCompiler.CharsetLength(model.CharsetForm, cids);
        if (charsetLength > 0)
        {
            layout.Charset = new byte[charsetLength];
            int at = 0;
            Cff1TableCompiler.EmitCharset(layout.Charset, ref at, model.CharsetForm, cids);
        }

        // --- FDSelect --------------------------------------------------------------
        layout.FdSelect = BuildFdSelect(model, plan, out diagnostic);
        if (layout.FdSelect.Length == 0 && diagnostic.Code != DiagnosticCode.None)
            return ReadStatus.InvalidFormat;

        // --- CharStrings -----------------------------------------------------------
        layout.CharStrings = model.CharStrings.ToArray().Select(static p => p).ToArray();
        foreach (var edit in plan.CharStrings)
        {
            if (edit.GlyphId < 0 || edit.GlyphId >= layout.CharStrings.Length)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, edit.GlyphId), out diagnostic,
                    unchecked((ulong)(long)edit.GlyphId), (ulong)layout.CharStrings.Length);
            layout.CharStrings[edit.GlyphId] = edit.Program
                ?? throw new ArgumentNullException(nameof(plan));
        }
        layout.CharStringsIndex = new byte[Cff1TableCompiler.IndexLength(layout.CharStrings)];

        // --- Local Subrs -----------------------------------------------------------
        layout.LocalSubrs = new byte[model.FdCount][][];
        for (int fd = 0; fd < model.FdCount; fd++)
        {
            var subrs = model[fd].LocalSubrs.ToArray();
            var edits = plan.LocalSubrs.Where(e => e.FdIndex == fd).ToArray();
            if (edits.Length > 0 && !model[fd].HasLocalSubrs)
                return Unsupported(model.Location, out diagnostic,
                    $"FD {fd} has no Local Subrs to edit");
            foreach (var edit in edits)
            {
                if (edit.Ordinal < 0 || edit.Ordinal >= subrs.Length)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange,
                        model.Location.At(DataField.Count, 0, edit.Ordinal), out diagnostic,
                        unchecked((ulong)(long)edit.Ordinal), (ulong)subrs.Length);
                subrs[edit.Ordinal] = edit.Program
                    ?? throw new ArgumentNullException(nameof(plan));
            }
            layout.LocalSubrs[fd] = subrs;
        }
        foreach (var edit in plan.LocalSubrs)
        {
            if (edit.FdIndex < 0 || edit.FdIndex >= model.FdCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.FdIndex, 0, edit.FdIndex), out diagnostic,
                    unchecked((ulong)(long)edit.FdIndex), (ulong)model.FdCount);
        }

        // --- Private DICT bytes ----------------------------------------------------
        // A Private DICT stores its Subrs offset relative to its own start, so moving the
        // Private DICT invalidates that operand. The replacement is filled in by the layout
        // loop below, once each Subrs target is known.
        layout.PrivateDicts = new byte[model.FdCount][];
        for (int fd = 0; fd < model.FdCount; fd++)
        {
            var fontDict = model[fd];
            if (!fontDict.HasPrivate) { layout.PrivateDicts[fd] = []; continue; }
            ReadStatus privateStatus = BuildPrivateDict(model, plan, fd, 0,
                out var dict, out diagnostic);
            if (privateStatus != ReadStatus.Done) return privateStatus;
            layout.PrivateDicts[fd] = dict;
        }
        foreach (var edit in plan.PrivateValues)
        {
            if (edit.FdIndex < 0 || edit.FdIndex >= model.FdCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.FdIndex, 0, edit.FdIndex), out diagnostic,
                    unchecked((ulong)(long)edit.FdIndex), (ulong)model.FdCount);
        }
        var localSubrsLength = new int[model.FdCount];
        for (int fd = 0; fd < model.FdCount; fd++)
            localSubrsLength[fd] = model[fd].HasLocalSubrs
                ? (int)Cff1TableCompiler.IndexLength(layout.LocalSubrs[fd])
                : 0;

        // --- offset arithmetic -----------------------------------------------------
        // Three lengths feed back into themselves: the Top DICT re-encodes four offsets, the
        // FDArray encodes each FD's Private offset, and each Private DICT encodes its Subrs
        // offset. All are resolved jointly, starting from an upper bound; moving the targets
        // earlier can only shorten the operands, so the iteration is monotone.
        bool customCharset = !model.CharsetForm.Predefined;
        layout.HeaderLength = model.HeaderHdrSize;
        layout.NameAt = layout.HeaderLength;
        layout.TopAt = layout.NameAt + (int)Cff1TableCompiler.SingleItemIndexLength(
            model.NameBytes.Length);

        int topDictLength = TopDictLengthGuess(model);
        for (int pass = 0; pass < 8; pass++)
        {
            int stringAt = layout.TopAt
                + (int)Cff1TableCompiler.SingleItemIndexLength(topDictLength);
            int globalAt = stringAt + (int)Cff1TableCompiler.IndexLength(model.Strings.ToArray());
            int afterGlobal = globalAt
                + (int)Cff1TableCompiler.IndexLength(model.GlobalSubrs.ToArray());

            layout.StringAt = stringAt;
            layout.GlobalAt = globalAt;
            layout.CharsetAt = customCharset ? afterGlobal : (int)model.CharsetOffset;
            layout.FdSelectAt = afterGlobal + (customCharset ? charsetLength : 0);
            layout.CharStringsAt = layout.FdSelectAt + layout.FdSelect.Length;
            layout.FdArrayAt = layout.CharStringsAt + layout.CharStringsIndex.Length;

            layout.PrivateAt = new int[model.FdCount];
            layout.FontDicts = new byte[model.FdCount][];
            layout.LocalSubrsAt = new int[model.FdCount];
            int fdArrayLength = 0;
            for (int inner = 0; inner < 12; inner++)
            {
                int cursor = layout.FdArrayAt + fdArrayLength;
                for (int fd = 0; fd < model.FdCount; fd++)
                {
                    if (!model[fd].HasPrivate) { layout.PrivateAt[fd] = -1; continue; }
                    layout.PrivateAt[fd] = cursor;
                    cursor += layout.PrivateDicts[fd].Length;
                }
                int subrsCursor = cursor;
                for (int fd = 0; fd < model.FdCount; fd++)
                {
                    if (localSubrsLength[fd] == 0) { layout.LocalSubrsAt[fd] = -1; continue; }
                    layout.LocalSubrsAt[fd] = subrsCursor;
                    subrsCursor += localSubrsLength[fd];
                }

                bool changed = false;
                for (int fd = 0; fd < model.FdCount; fd++)
                {
                    if (!model[fd].HasPrivate || layout.LocalSubrsAt[fd] < 0) continue;
                    int relative = layout.LocalSubrsAt[fd] - layout.PrivateAt[fd];
                    ReadStatus privateStatus = BuildPrivateDict(model, plan, fd, relative,
                        out var dict, out diagnostic);
                    if (privateStatus != ReadStatus.Done) return privateStatus;
                    if (dict.Length != layout.PrivateDicts[fd].Length) changed = true;
                    layout.PrivateDicts[fd] = dict;
                }
                for (int fd = 0; fd < model.FdCount; fd++)
                {
                    layout.FontDicts[fd] = model[fd].HasPrivate
                        ? BuildFontDict(model[fd], layout.PrivateAt[fd],
                            layout.PrivateDicts[fd].Length)
                        : model[fd].RawBytes.ToArray();
                }
                int candidate = (int)Cff1TableCompiler.IndexLength(layout.FontDicts);
                if (candidate != fdArrayLength) { fdArrayLength = candidate; changed = true; }
                if (!changed) break;
            }
            layout.FdArrayIndex = new byte[fdArrayLength];

            layout.TailAt = layout.FdArrayAt + fdArrayLength;
            for (int fd = 0; fd < model.FdCount; fd++)
                if (layout.PrivateAt[fd] >= 0) layout.TailAt += layout.PrivateDicts[fd].Length;
            for (int fd = 0; fd < model.FdCount; fd++)
                if (layout.LocalSubrsAt[fd] >= 0) layout.TailAt += localSubrsLength[fd];
            layout.TotalLength = layout.TailAt;

            layout.TopReplacements[0] = customCharset ? layout.CharsetAt
                : (int)model.CharsetOffset;
            layout.TopReplacements[1] = layout.FdSelectAt;
            layout.TopReplacements[2] = layout.CharStringsAt;
            layout.TopReplacements[3] = layout.FdArrayAt;
            var topDict = new byte[TopDictLength(model, layout)];
            int topAt = 0;
            Cff1TableCompiler.EmitDict(topDict, ref topAt, model.TopDict,
                TopReplacements(layout));
            layout.TopDict = topDict;
            if (topDict.Length == topDictLength) break;
            topDictLength = topDict.Length;
        }
        return ReadStatus.Done;
    }

    /// <summary>Rebuilds one FD's Private DICT, applying plan edits and pointing Subrs at the
    /// Local Subrs INDEX the layout decided on.</summary>
    private static ReadStatus BuildPrivateDict(CidCff1Table model, CidEditPlan plan, int fd,
        int subrsRelative, out byte[] dict, out FontDiagnostic diagnostic)
    {
        dict = [];
        var fontDict = model[fd];
        var replacements = new Dictionary<int, int[]>();
        foreach (var edit in plan.PrivateValues)
        {
            if (edit.FdIndex != fd) continue;
            int index = IndexOfEntry(fontDict.PrivateEntries, edit.Operator);
            if (index < 0)
                return Unsupported(model.Location, out diagnostic,
                    $"FD {fd} Private DICT has no operator {edit.Operator}");
            var entry = fontDict.PrivateEntries[index];
            var values = new int[entry.Operands.Count];
            for (int i = 0; i < values.Length; i++) values[i] = entry.Operands[i].Value;
            values[0] = edit.Value;
            replacements[edit.Operator] = values;
        }
        if (fontDict.HasLocalSubrs)
        {
            int index = IndexOfEntry(fontDict.PrivateEntries, Cff1Layout.OpSubrs);
            if (index >= 0)
            {
                // Never overwrite an explicit edit of the Subrs operand: the layout owns it.
                replacements[Cff1Layout.OpSubrs] = [subrsRelative];
            }
        }
        int length = Cff1TableCompiler.DictLength(fontDict.PrivateEntries, replacements);
        dict = new byte[length];
        int at = 0;
        Cff1TableCompiler.EmitDict(dict, ref at, fontDict.PrivateEntries, replacements);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static Dictionary<int, int[]> TopReplacements(Layout layout) => new()
    {
        [Cff1Layout.OpCharset] = [layout.TopReplacements[0]],
        [Cff1Layout.OpFdSelect] = [layout.TopReplacements[1]],
        [Cff1Layout.OpCharStrings] = [layout.TopReplacements[2]],
        [Cff1Layout.OpFdArray] = [layout.TopReplacements[3]],
    };

    private static ReadStatus Unsupported(DataLocation location, out FontDiagnostic diagnostic,
        string detail) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation, location,
            out diagnostic, 0, 0);

    private static int IndexOfEntry(ReadOnlySpan<Cff1DictEntry> entries, int op)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Operator == op) return i;
        return -1;
    }

    private static int TopDictLengthGuess(CidCff1Table model)
    {
        // The Top DICT carries four offsets that are re-encoded, so its length can only be
        // known once they are. Five-byte operands are the widest, which bounds the guess.
        var replacements = new Dictionary<int, int[]>
        {
            [Cff1Layout.OpCharset] = [int.MaxValue],
            [Cff1Layout.OpFdSelect] = [int.MaxValue],
            [Cff1Layout.OpCharStrings] = [int.MaxValue],
            [Cff1Layout.OpFdArray] = [int.MaxValue],
        };
        return Cff1TableCompiler.DictLength(model.TopDict, replacements);
    }

    private static int TopDictLength(CidCff1Table model, Layout layout)
    {
        var replacements = new Dictionary<int, int[]>
        {
            [Cff1Layout.OpCharset] = [layout.TopReplacements[0]],
            [Cff1Layout.OpFdSelect] = [layout.TopReplacements[1]],
            [Cff1Layout.OpCharStrings] = [layout.TopReplacements[2]],
            [Cff1Layout.OpFdArray] = [layout.TopReplacements[3]],
        };
        return Cff1TableCompiler.DictLength(model.TopDict, replacements);
    }

    private static byte[] BuildFontDict(CidFontDict fontDict, int privateAt, int privateSize)
    {
        if (!fontDict.HasPrivate) return fontDict.RawBytes.ToArray();
        var replacements = new Dictionary<int, int[]>
        {
            [Cff1Layout.OpPrivate] = [privateSize, privateAt],
        };
        var dict = new byte[Cff1TableCompiler.DictLength(fontDict.Entries, replacements)];
        int at = 0;
        Cff1TableCompiler.EmitDict(dict, ref at, fontDict.Entries, replacements);
        return dict;
    }

    /// <summary>Rebuilds FDSelect, keeping its wire format and applying range edits.
    ///
    /// An empty array with a set diagnostic means the plan was refused.</summary>
    private static byte[] BuildFdSelect(CidCff1Table model, CidEditPlan plan,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var map = model.FdSelect;
        if (map.Format == CidFdSelectFormat.PerGlyph)
        {
            if (plan.FdRanges.Count > 0)
            {
                ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location, out diagnostic, 0, 0);
                return [];
            }
            var buffer = new byte[1 + map.NumGlyphs];
            buffer[0] = (byte)CidLayout.FdSelectFormat0;
            for (int gid = 0; gid < map.NumGlyphs; gid++)
                buffer[1 + gid] = unchecked((byte)map.PerGlyph[gid]);
            return buffer;
        }

        int rangeCount = map.RangeCount + plan.FdRanges.Count(e => e.Ordinal >= map.RangeCount);
        var first = new int[rangeCount];
        var fd = new int[rangeCount];
        for (int i = 0; i < map.RangeCount; i++)
        {
            first[i] = map.Ranges[i].FirstGlyph;
            fd[i] = map.Ranges[i].FdIndex;
        }
        for (int i = map.RangeCount; i < rangeCount; i++)
        {
            first[i] = map.NumGlyphs;       // placeholder, replaced by the edit below
            fd[i] = 0;
        }
        foreach (var edit in plan.FdRanges)
        {
            if (edit.Ordinal < 0 || edit.Ordinal >= rangeCount)
            {
                ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, 0, edit.Ordinal), out diagnostic,
                    unchecked((ulong)(long)edit.Ordinal), (ulong)rangeCount);
                return [];
            }
            if (edit.FirstGlyph is int f) first[edit.Ordinal] = f;
            if (edit.FdIndex is int v) fd[edit.Ordinal] = v;
        }
        // The sentinel must remain the glyph count, and runs must stay ordered and in range,
        // so the edited list is re-checked here rather than trusted.
        for (int i = 0; i < rangeCount; i++)
        {
            if (i == 0 && first[i] != 0)
            {
                ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Count, 0, i), out diagnostic, (uint)first[i], 0);
                return [];
            }
            if (i > 0 && first[i] <= first[i - 1])
            {
                ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Count, 0, i), out diagnostic, (uint)first[i],
                    (uint)first[i - 1] + 1);
                return [];
            }
            if (fd[i] < 0 || fd[i] >= model.FdCount)
            {
                ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.FdIndex, 0, i), out diagnostic, (uint)fd[i],
                    (uint)model.FdCount);
                return [];
            }
        }
        var buffer2 = new byte[1 + 2 + rangeCount * CidLayout.FdSelectRangeStride + 2];
        int at = 0;
        buffer2[at++] = (byte)CidLayout.FdSelectFormat3;
        BinaryPrimitives.WriteUInt16BigEndian(buffer2.AsSpan(at), (ushort)rangeCount);
        at += 2;
        for (int i = 0; i < rangeCount; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer2.AsSpan(at), (ushort)first[i]);
            at += 2;
            buffer2[at++] = unchecked((byte)fd[i]);
        }
        BinaryPrimitives.WriteUInt16BigEndian(buffer2.AsSpan(at), (ushort)map.NumGlyphs);
        return buffer2;
    }

    private static ReadStatus Emit(CidCff1Table model, CidEditPlan plan, Layout layout,
        byte[] output, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        int at = 0;
        output[at++] = model.HeaderMajor;
        output[at++] = model.HeaderMinor;
        output[at++] = model.HeaderHdrSize;
        output[at++] = model.HeaderOffSize;     // preserved, so an unedited header stays identical

        // The four INDEXes that precede the offset targets.
        at = layout.NameAt;
        Cff1TableCompiler.EmitSingleItemIndex(output, ref at, model.NameBytes);
        Cff1TableCompiler.EmitSingleItemIndex(output, ref at, layout.TopDict);
        Cff1TableCompiler.EmitIndex(output, ref at, model.Strings.ToArray());
        Cff1TableCompiler.EmitIndex(output, ref at, model.GlobalSubrs.ToArray());

        // The offset targets, in canonical order.
        if (!model.CharsetForm.Predefined && layout.Charset.Length > 0)
            layout.Charset.CopyTo(output.AsSpan(layout.CharsetAt));
        layout.FdSelect.CopyTo(output.AsSpan(layout.FdSelectAt));
        at = layout.CharStringsAt;
        Cff1TableCompiler.EmitIndex(output, ref at, layout.CharStrings);
        at = layout.FdArrayAt;
        Cff1TableCompiler.EmitIndex(output, ref at, layout.FontDicts);

        // Every FD's Private DICT, then every FD's Local Subrs INDEX.
        for (int fd = 0; fd < model.FdCount; fd++)
        {
            if (!model[fd].HasPrivate) continue;
            int privateAt = layout.PrivateAt[fd];
            if (privateAt < 0 || privateAt + layout.PrivateDicts[fd].Length > output.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset,
                    DiagnosticCode.RangeOutsideSource, model.Location, out diagnostic,
                    (uint)Math.Max(0, privateAt), (ulong)output.Length);
            layout.PrivateDicts[fd].CopyTo(output.AsSpan(privateAt));
        }
        for (int fd = 0; fd < model.FdCount; fd++)
        {
            if (!model[fd].HasLocalSubrs) continue;
            at = layout.LocalSubrsAt[fd];
            Cff1TableCompiler.EmitIndex(output, ref at, layout.LocalSubrs[fd]);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
