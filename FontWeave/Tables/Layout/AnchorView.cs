using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct AnchorView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(new(0, Location, _data.Length, false), 0, out _, out _, out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(new(0, Location, _data.Length, false), 0, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(out AnchorTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out AnchorTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget) =>
        Process(new(0, Location, _data.Length, true), 0, out model, out _, out diagnostic, budget);

    // The cursive chain's entry: device facts dedupe through the caller's state, keyed
    // by absolute Lookup-local positions (anchorStart + deviceOffset); the standalone
    // face uses its own state. The anchor body itself is 6 bytes (formats 1/2) or the
    // full 10-byte header (format 3).
    internal ReadStatus TryRead(out AnchorTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, CursivePosReadState state, int anchorStart) =>
        Process(state, anchorStart, out model, out _, out diagnostic, budget);

    internal ReadStatus TryRead(out AnchorTable? model, out int consumedLength,
        out FontDiagnostic diagnostic, BuildBudget budget, CursivePosReadState state,
        int anchorStart) =>
        Process(state, anchorStart, out model, out consumedLength, out diagnostic, budget);

    private ReadStatus Process(CursivePosReadState state, int anchorStart,
        out AnchorTable? model, out int consumedLength, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) { consumedLength = 0; return status; }
        status = TryGetXCoordinate(out short x, out diagnostic);
        if (status != ReadStatus.Done) { consumedLength = 0; return status; }
        status = TryGetYCoordinate(out short y, out diagnostic);
        if (status != ReadStatus.Done) { consumedLength = 0; return status; }
        int consumed = AnchorLayout.MinimumHeader;
        ushort anchorPoint = 0;
        if (AnchorFormat == 2)
        {
            // Opaque UInt16 (OQ-T4-05-5): the contour point's existence domain is the
            // owning glyph's outline, a cross-table domain this view does not check.
            status = TryReadFormat2HeaderAt(AnchorLayout.Format2HeaderField, -1,
                out var format2Header, out diagnostic);
            if (status != ReadStatus.Done) { consumedLength = 0; return status; }
            anchorPoint = format2Header.AnchorPoint;
        }
        StaticDeviceTable? xDevice = null, yDevice = null;
        if (AnchorFormat == 3)
        {
            consumed = AnchorLayout.Format3Length;
            // Either Device slot may be NULL while the other resolves (spec
            // gpos.md:761, W6's yDevice=0 positive pin); offset 0 is the present-null
            // None of the T4-01 mask/offset decoupling, never an absent field.
            status = TryReadFormat3HeaderAt(AnchorLayout.Format3HeaderField, -1,
                out var format3Header, out diagnostic);
            if (status != ReadStatus.Done) { consumedLength = 0; return status; }
            ushort xDeviceOffset = format3Header.XDeviceOffset, yDeviceOffset = format3Header.YDeviceOffset;
            status = TryOpenXDeviceAt(AnchorLayout.Format3HeaderField, -1, out var xView,
                out bool xPresent, out diagnostic);
            if (status != ReadStatus.Done) { consumedLength = 0; return status; }
            if (xPresent)
            {
                status = ReadDevice(anchorStart + xDeviceOffset, xView, state,
                    out xDevice, out int xLength, out diagnostic, budget);
                if (status != ReadStatus.Done) { consumedLength = 0; return status; }
                if (xDeviceOffset + xLength > consumed) consumed = xDeviceOffset + xLength;
            }
            status = TryOpenYDeviceAt(AnchorLayout.Format3HeaderField, -1, out var yView,
                out bool yPresent, out diagnostic);
            if (status != ReadStatus.Done) { consumedLength = 0; return status; }
            if (yPresent)
            {
                status = ReadDevice(anchorStart + yDeviceOffset, yView, state,
                    out yDevice, out int yLength, out diagnostic, budget);
                if (status != ReadStatus.Done) { consumedLength = 0; return status; }
                if (yDeviceOffset + yLength > consumed) consumed = yDeviceOffset + yLength;
            }
        }
        consumedLength = consumed;
        if (state.Materialize)
            model = new(AnchorFormat, x, y, anchorPoint, xDevice, yDevice, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadDevice(int target, StaticDeviceView view, CursivePosReadState state,
        out StaticDeviceTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; length = 0;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.DeviceFacts.TryGetValue(target, out var fact))
        { length = fact.Length; if (state.Materialize) state.Devices.TryGetValue(target, out model); diagnostic = default; return ReadStatus.Done; }
        ReadStatus status = view.InspectWords(out int count, out int bits, out length, out ushort lastWord, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            // Expansion consumes the facts InspectWords just proved (spec 6.2).
            status = view.Materialize(count, bits, length, lastWord, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        state.DeviceFacts.Add(target, new(count, length));
        if (state.Materialize) state.Devices.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
