using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class PostTableCompiler
{
    public static ReadStatus TryCompile(PostTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(PostTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        long length = GetLength(model);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[(int)length];
        Write(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(PostTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        long length = GetLength(model);
        if (length > destination.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(ReadOnlySpan<byte> input, PostEditPlan edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        output = null;
        var budget = new BuildBudget(limits);
        location = location == default ? DataLocation.Anonymous(PostLayout.Tag) : location;
        if (!budget.Input(input.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryCompile(input, edit, out output, out diagnostic, budget, location);
    }

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input, PostEditPlan edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        output = null;
        ReadStatus status = PostView.TryOpen(input, out var view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out PostTable? original, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryApply(original!, edit, out PostTable? updated, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return TryCompile(updated!, out output, out diagnostic, budget);
    }

    internal static ReadStatus TryApply(PostTable model, PostEditPlan edit,
        out PostTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Names);
        updated = null;
        var at = model.Location;
        if (edit.Names.Count != 0 && model.NameKind is PostNameKind.Standard258 or PostNameKind.NoNames)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at.At(DataField.Version, 0), out diagnostic, model.Version);
        if (!budget.Work(32L * edit.Names.Count, at, out diagnostic)) return ReadStatus.ResourceLimit;
        long nameChars = 0;
        for (int i = 0; i < edit.Names.Count; i++)
        {
            var item = edit.Names[i];
            if (item.GlyphId < 0 || item.GlyphId >= model.NumGlyphs.GetValueOrDefault())
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at.At(DataField.GlyphId, 34, item.GlyphId), out diagnostic,
                    unchecked((ulong)(long)item.GlyphId), model.NumGlyphs.GetValueOrDefault());
            if (item.StandardIndex.HasValue == (item.CustomName is not null) || item.StandardIndex > 257)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at.At(DataField.GlyphId, 34, item.GlyphId), out diagnostic);
            if (item.CustomName is not null && !PostView.IsValidName(item.CustomName))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    at.At(DataField.Payload, 34, item.GlyphId), out diagnostic);
            if (model.NameKind == PostNameKind.SignedOffsets &&
                (item.CustomName is not null || (int)item.StandardIndex!.Value - item.GlyphId is < -128 or > 127))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    at.At(DataField.GlyphId, 34UL + (uint)item.GlyphId, item.GlyphId), out diagnostic);
            nameChars += item.CustomName?.Length ?? 0;
        }
        foreach (string name in model.CustomNames) nameChars += name.Length;
        long arrayBytes = 2L * model.GlyphNameIndices.Length + model.SignedOffsets.Length +
            40L * edit.Names.Count + 40L * model.CustomNames.Count + 2L * nameChars;
        if (!budget.Work(nameChars, at, out diagnostic) ||
            !budget.Buffer(arrayBytes, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Names.ToArray();
        Array.Sort(requests, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    at.At(DataField.GlyphId, 34, requests[i].GlyphId), out diagnostic,
                    (uint)requests[i].GlyphId);
        ushort[] indices = model.GlyphNameIndices.ToArray();
        sbyte[] offsets = model.SignedOffsets.ToArray();
        var names = new List<string>(model.CustomNames);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++) ordinals.TryAdd(names[i], i);
        bool changed = edit.UnderlinePosition.HasValue && edit.UnderlinePosition != model.Header.UnderlinePosition;
        foreach (var request in requests)
        {
            int index;
            if (request.StandardIndex.HasValue) index = request.StandardIndex.Value;
            else
            {
                string name = request.CustomName!;
                if (!ordinals.TryGetValue(name, out int ordinal))
                {
                    ordinal = names.Count;
                    if (ordinal > ushort.MaxValue - 258)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            at.At(DataField.Count, 34, request.GlyphId), out diagnostic,
                            (ulong)ordinal + 258, ushort.MaxValue);
                    names.Add(name); ordinals.Add(name, ordinal);
                }
                index = 258 + ordinal;
                if (index > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        at.At(DataField.Count, 34, request.GlyphId), out diagnostic,
                        (ulong)index, ushort.MaxValue);
            }
            if (model.NameKind == PostNameKind.IndexedNames)
            { changed |= indices[request.GlyphId] != index; indices[request.GlyphId] = (ushort)index; }
            else
            { int delta = index - request.GlyphId; changed |= offsets[request.GlyphId] != delta; offsets[request.GlyphId] = (sbyte)delta; }
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at, out diagnostic);
        updated = new(model.Version, model.Header with
            { UnderlinePosition = edit.UnderlinePosition ?? model.Header.UnderlinePosition },
            indices, offsets, names.ToArray(), at);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long GetLength(PostTable model)
    {
        if (model.NameKind is PostNameKind.Standard258 or PostNameKind.NoNames) return 32;
        if (model.NameKind == PostNameKind.SignedOffsets) return 34L + model.SignedOffsets.Length;
        long length = 34L + 2L * model.GlyphNameIndices.Length;
        foreach (string name in model.CustomNames) length += name.Length + 1;
        return length;
    }

    private static void Write(PostTable model, Span<byte> bytes)
    {
        // After the capacity preflight these generated writers cannot fail; a failure
        // is a writer contract bug and surfaces loudly. The CustomNames Pascal segment
        // below stays hand-written: variable-length name semantics, not schema layout.
        var h = model.Header;
        DataLocation location = model.Location;
        RequireWritten(PostView.TryWriteVersion(bytes, model.Version, out _, location));
        RequireWritten(PostView.TryWriteItalicAngleRaw(bytes, h.ItalicAngleRaw, out _, location));
        RequireWritten(PostView.TryWriteUnderlinePosition(bytes, h.UnderlinePosition, out _, location));
        RequireWritten(PostView.TryWriteUnderlineThickness(bytes, h.UnderlineThickness, out _, location));
        RequireWritten(PostView.TryWriteIsFixedPitch(bytes, h.IsFixedPitch, out _, location));
        RequireWritten(PostView.TryWriteMinMemType42(bytes, h.MinMemType42, out _, location));
        RequireWritten(PostView.TryWriteMaxMemType42(bytes, h.MaxMemType42, out _, location));
        RequireWritten(PostView.TryWriteMinMemType1(bytes, h.MinMemType1, out _, location));
        RequireWritten(PostView.TryWriteMaxMemType1(bytes, h.MaxMemType1, out _, location));
        if (model.NameKind is PostNameKind.Standard258 or PostNameKind.NoNames) return;
        RequireWritten(PostView.TryWriteNumGlyphs(bytes, model.NumGlyphs!.Value, out _, location));
        if (model.NameKind == PostNameKind.SignedOffsets)
        {
            for (int i = 0; i < model.SignedOffsets.Length; i++)
                RequireWritten(PostView.TryWriteGlyphOffsetAt(bytes, 34UL + (uint)i,
                    new PostView.GlyphOffsetData(i, model.SignedOffsets[i]), out _, location));
            return;
        }
        for (int i = 0; i < model.GlyphNameIndices.Length; i++)
            RequireWritten(PostView.TryWriteGlyphNameIndexAt(bytes, 34UL + 2UL * (uint)i,
                new PostView.GlyphNameIndexData(i, model.GlyphNameIndices[i]), out _, location));
        int at = 34 + 2 * model.GlyphNameIndices.Length;
        foreach (string name in model.CustomNames)
        {
            bytes[at++] = (byte)name.Length;
            at += Encoding.ASCII.GetBytes(name, bytes.Slice(at));
        }
    }

    // Same contract as the meta emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted post write failed.");
    }
}
