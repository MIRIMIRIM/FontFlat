namespace FontWeave.Core;

// Pure primitive (design 3.3): one operation's root structure-scan scope — the completed
// facts-slot table plus the items fixed for the whole operation (the root identity and
// the requested proof category, which is the structural scan contract itself and is
// therefore not part of any per-entry key, spec 6.2). Only closure types and BCL
// collections; no FontFile/SfntWriter/Containers/Tables/CFF or glyph semantics. The
// dictionary is allocated lazily, so a root without Follow children never creates one
// (spec 6.3); only completed DeclaredClosure/Partial facts are ever stored — a failed or
// unfinished node has no completed slot to publish and never enters the table.
internal sealed class StructureScanScope
{
    // Per-entry key dimensions (spec 6.2): the physical target, its effective window
    // length, the View kind and the role/profile. Root identity and the requested proof
    // category live on the scope, so they are not repeated per entry.
    private readonly DataLocation _rootIdentity;
    private Dictionary<(ulong Target, int WindowLength, int KindId, long RoleProfile),
        global::FontWeave.Schema.StructuralScanCoverage>? _completed;

    internal StructureScanScope(DataLocation rootIdentity) => _rootIdentity = rootIdentity;

    /// <summary>The fixed explanation boundary of this operation (spec 6.2).</summary>
    internal DataLocation RootIdentity => _rootIdentity;

    // First publication wins: a cache miss is the only path that scans a child, so a
    // second Publish for one key cannot occur inside a single operation's scope.
    internal void Publish(ulong target, int windowLength, int kindId, long roleProfile,
        global::FontWeave.Schema.StructuralScanCoverage coverage)
    {
        var key = (Target: target, WindowLength: windowLength, KindId: kindId, RoleProfile: roleProfile);
        var completed = _completed ??= new Dictionary<(ulong Target, int WindowLength, int KindId, long RoleProfile),
            global::FontWeave.Schema.StructuralScanCoverage>();
        if (!completed.ContainsKey(key)) completed[key] = coverage;
    }

    internal bool TryGetCompleted(ulong target, int windowLength, int kindId, long roleProfile,
        out global::FontWeave.Schema.StructuralScanCoverage coverage)
    {
        if (_completed is { } completed &&
            completed.TryGetValue((target, windowLength, kindId, roleProfile), out coverage)) return true;
        coverage = default;
        return false;
    }
}
