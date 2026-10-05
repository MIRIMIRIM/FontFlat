using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// One fixed-depth traversal over one root window. Separate typed caches prevent
// address-only reuse across layouts; feature/lookup counts are fixed by the caller.
internal sealed class LayoutReadState
{
    internal LayoutReadState(DataLocation origin, int length, bool materialize)
    {
        Origin = origin; Length = length; Materialize = materialize;
        if (materialize)
        {
            Scripts = new(); Languages = new(); Features = new(); Lookups = new();
        }
    }

    internal DataLocation Origin { get; }
    internal int Length { get; }
    internal bool Materialize { get; }
    internal Dictionary<int, LayoutScript>? Scripts { get; }
    internal Dictionary<int, LayoutLangSys>? Languages { get; }
    internal Dictionary<int, LayoutFeature>? Features { get; }
    internal Dictionary<int, LookupTable>? Lookups { get; }

    internal DataLocation At(int position) => Origin.AtLocal(DataField.None, (ulong)position);

    internal static ReadStatus Store<T>(Dictionary<int, T>? cache, int position, T model,
        DataLocation location, BuildBudget budget, out FontDiagnostic diagnostic) where T : class
    {
        if (cache is not null)
        {
            if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
            cache.Add(position, model);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

internal static class LayoutReadWire
{
    internal static ReadStatus Index(ushort value, int count, DataLocation location, ulong position,
        int ordinal, out FontDiagnostic diagnostic)
    {
        if (value >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.Payload, position, ordinal), out diagnostic, value, (ulong)count);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
