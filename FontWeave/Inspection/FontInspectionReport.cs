using FontWeave.Containers;
using FontWeave.Core;

namespace FontWeave.Inspection;

/// <summary>A dependency actually considered by one table dispatch. Status describes
/// the bounded-presence or typed-context resolution selected by the owner. Done for
/// raw presence does not mean the dependency body was validated; the owner's selected
/// formal validator checks only the structures described by its ValidationScope.
/// Missing optional context is absence, not a successful cross-table check.</summary>
public readonly record struct FontTableDependency(uint Tag, bool Required,
    ReadStatus Status, DataLocation Location, FontDiagnostic Diagnostic);

/// <summary>Validation posture for <see cref="FontInspection.TryValidate"/>. Strict is
/// the default and the frozen spec-audit shape; TolerateLegacyDialects admits the
/// documented OS/2 commercial-font deviations and reports each one as a finding.</summary>
public sealed record FontValidationOptions
{
    public static FontValidationOptions Strict { get; } = new();

    public bool TolerateLegacyDialects { get; init; }
}

/// <summary>One wire-order directory item, or an explicitly requested missing tag.
/// A null tag/record means the directory record itself could not be read. A null
/// validation status/diagnostic means typed validation was not attempted.</summary>
public readonly record struct FontTableInspection
{
    internal FontTableInspection(int ordinal, uint? tag, SfntDirectoryRecord? record,
        DataLocation location, ReadStatus status, FontDiagnostic diagnostic)
    {
        Ordinal = ordinal; Tag = tag; Record = record; Location = location;
        InspectionStatus = status; InspectionDiagnostic = diagnostic;
    }

    public int Ordinal { get; }
    public uint? Tag { get; }
    public SfntDirectoryRecord? Record { get; }
    public DataLocation Location { get; }
    public ReadStatus InspectionStatus { get; }
    public FontDiagnostic InspectionDiagnostic { get; }
    public ReadStatus? ValidationStatus { get; internal init; }
    public FontDiagnostic? ValidationDiagnostic { get; internal init; }
    /// <summary>Header value read by the official View. Split major/minor headers
    /// are combined as (major &lt;&lt; 16) | minor; null means not observed.</summary>
    public uint? RawVersion { get; internal init; }
    public string? Profile { get; internal init; }
    /// <summary>Actual dependency resolutions used by the owner's selected formal
    /// entry point. Raw-presence resolutions do not claim dependency-body validation.</summary>
    public IReadOnlyList<FontTableDependency> Dependencies { get; internal init; } =
        Array.Empty<FontTableDependency>();
    public int MetadataOnlyLookupCount { get; internal init; }
    /// <summary>The dispatched table/context scope, not a full specification claim.</summary>
    public string? ValidationScope { get; internal init; }
}

/// <summary>Immutable numeric inspection evidence; does not expose or copy table
/// payloads. Directory checksum values are wire metadata, not checksum verification.</summary>
public sealed class FontInspectionReport
{
    internal FontInspectionReport(SfntDirectoryView directory, uint? requestedTag,
        ReadStatus containerStatus, FontDiagnostic containerDiagnostic,
        BuildUsage admissionUsage, BuildLimits limits, FontTableInspection[] tables,
        FontToleranceFinding[]? toleratedFindings = null)
    {
        SourceId = directory.Face.SourceId; FaceIndex = directory.Face.FaceIndex;
        IsCollection = directory.Face.IsCollection; ContainerVersion = directory.Version;
        DeclaredTableCount = directory.DeclaredCount; RequestedTableTag = requestedTag;
        ContainerValidationStatus = containerStatus; ContainerDiagnostic = containerDiagnostic;
        AdmissionUsage = admissionUsage; PerTableLimits = limits;
        Tables = Array.AsReadOnly(tables);
        ToleratedFindings = Array.AsReadOnly(toleratedFindings ?? []);
    }

    public long SourceId { get; }
    public int FaceIndex { get; }
    public bool IsCollection { get; }
    public uint ContainerVersion { get; }
    public ushort DeclaredTableCount { get; }
    public uint? RequestedTableTag { get; }
    public ReadStatus ContainerValidationStatus { get; }
    public FontDiagnostic ContainerDiagnostic { get; }
    /// <summary>Outer logical work reservations and charged owned-array payloads only.
    /// Opaque directory/table/child validator work and allocations are excluded.
    /// This is neither GC allocation measurement nor an aggregate inner-budget total.</summary>
    public BuildUsage AdmissionUsage { get; }
    /// <summary>Official table entry points that accept BuildLimits receive this
    /// configuration independently; entries with dedicated/default limits retain
    /// those contracts. Child budgets are not aggregated, and these values do not
    /// bound process memory or total whole-font work.</summary>
    public BuildLimits PerTableLimits { get; }
    public IReadOnlyList<FontTableInspection> Tables { get; }
    /// <summary>Deviations the tolerant posture admitted; always empty under Strict.</summary>
    public IReadOnlyList<FontToleranceFinding> ToleratedFindings { get; }
}
