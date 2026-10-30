using System.Runtime.CompilerServices;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;

namespace FontWeave.Inspection;

/// <summary>Source-bound raw inspection and finite typed table integration.
/// Official Views own parsing and format validation; this dispatcher supplies
/// actual same-face dependencies. It does not verify checksums or signatures.</summary>
public static class FontInspection
{
    private const uint Head = 0x68656164, Maxp = 0x6D617870, Name = 0x6E616D65;
    private const uint Os2 = 0x4F532F32, Post = 0x706F7374, Hhea = 0x68686561, Hmtx = 0x686D7478;
    private const uint Vhea = 0x76686561, Vmtx = 0x766D7478, Vorg = 0x564F5247;
    private const uint Meta = 0x6D657461, Dsig = 0x44534947, Hdmx = 0x68646D78, Ltsh = 0x4C545348;
    private const uint Pclt = 0x50434C54, Vdmx = 0x56444D58, Cmap = 0x636D6170, Cff = 0x43464620;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361, Cvt = 0x63767420;
    private const uint Fpgm = 0x6670676D, Prep = 0x70726570, Gasp = 0x67617370;
    private const uint Base = 0x42415345, Gdef = 0x47444546, Gpos = 0x47504F53, Gsub = 0x47535542;
    private const uint Jstf = 0x4A535446, Math = 0x4D415448, Kern = 0x6B65726E, Merg = 0x4D455247;
    private const uint Cff2 = 0x43464632, Fvar = 0x66766172, Avar = 0x61766172, Gvar = 0x67766172;
    private const uint Cvar = 0x63766172, Hvar = 0x48564152, Vvar = 0x56564152, Mvar = 0x4D564152;
    private const uint Stat = 0x53544154, Colr = 0x434F4C52, Cpal = 0x4350414C, Svg = 0x53564720;
    private const uint Sbix = 0x73626978, Cbdt = 0x43424454, Cblc = 0x43424C43;
    private const uint Ebdt = 0x45424454, Eblc = 0x45424C43, Ebsc = 0x45425343;

    public static ReadStatus TryInspect(FontFaceRef face, out FontInspectionReport? report,
        out FontDiagnostic diagnostic, uint? tableTag = null, BuildLimits? limits = null)
    {
        report = null;
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        return status == ReadStatus.Done
            ? Run(directory, false, out report, out diagnostic, tableTag, limits) : status;
    }

    public static ReadStatus TryInspect(SfntDirectoryView directory, out FontInspectionReport? report,
        out FontDiagnostic diagnostic, uint? tableTag = null, BuildLimits? limits = null) =>
        Run(directory, false, out report, out diagnostic, tableTag, limits);

    /// <summary>Returns the first failure while retaining a complete admitted report.
    /// Independent table checks continue after table failures. A container refusal
    /// leaves typed statuses null; no table validation is claimed in that case.</summary>
    public static ReadStatus TryValidate(FontFaceRef face, out FontInspectionReport? report,
        out FontDiagnostic diagnostic, uint? tableTag = null, BuildLimits? limits = null) =>
        TryValidate(face, FontValidationOptions.Strict, out report, out diagnostic, tableTag, limits);

    public static ReadStatus TryValidate(FontFaceRef face, FontValidationOptions options,
        out FontInspectionReport? report, out FontDiagnostic diagnostic,
        uint? tableTag = null, BuildLimits? limits = null)
    {
        report = null;
        ArgumentNullException.ThrowIfNull(options);
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        return status == ReadStatus.Done
            ? Run(directory, true, out report, out diagnostic, tableTag, limits, options) : status;
    }

    public static ReadStatus TryValidate(SfntDirectoryView directory, out FontInspectionReport? report,
        out FontDiagnostic diagnostic, uint? tableTag = null, BuildLimits? limits = null) =>
        Run(directory, true, out report, out diagnostic, tableTag, limits);

    public static ReadStatus TryValidate(SfntDirectoryView directory, FontValidationOptions options,
        out FontInspectionReport? report, out FontDiagnostic diagnostic,
        uint? tableTag = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Run(directory, true, out report, out diagnostic, tableTag, limits, options);
    }

    private static ReadStatus Run(SfntDirectoryView directory, bool typed,
        out FontInspectionReport? report, out FontDiagnostic diagnostic,
        uint? requestedTag, BuildLimits? limits, FontValidationOptions? options = null)
    {
        report = null;
        diagnostic = default;
        if (!directory.IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                directory.Face.Location, out diagnostic);
        var budget = new BuildBudget(limits);
        int count = directory.DeclaredCount;
        DataLocation faceLocation = directory.Face.Location;
        // An explicit upper admission envelope for header/records and directory
        // preflight. This is not instrumentation of the directory's internal work.
        if (!budget.Input(directory.SourceByteLength, faceLocation, out diagnostic) ||
            !budget.Work(12L + 16L * count + (long)count * count, faceLocation, out diagnostic) ||
            !budget.Buffer((long)count * Unsafe.SizeOf<FontTableInspection>(), faceLocation, out diagnostic))
            return ReadStatus.ResourceLimit;
        var rows = new FontTableInspection[count];
        ReadStatus firstRaw = ReadStatus.Done;
        FontDiagnostic rawDiagnostic = default;
        int matches = 0;
        for (int i = 0; i < count; i++)
        {
            ReadStatus status = directory.TryGetRecord(i, out var record, out var itemDiagnostic);
            if (status != ReadStatus.Done)
                rows[i] = new(i, null, null, itemDiagnostic.Location, status, itemDiagnostic);
            else
            {
                status = directory.TryGetTableBytes(i, out _, out itemDiagnostic);
                rows[i] = new(i, record.Tag, record, directory.TableLocation(i), status, itemDiagnostic);
                if (requestedTag == record.Tag) matches++;
            }
            if (firstRaw == ReadStatus.Done && status != ReadStatus.Done)
            { firstRaw = status; rawDiagnostic = itemDiagnostic; }
        }
        ReadStatus containerStatus = directory.Validate(out var containerDiagnostic);
        ReadStatus result = typed && containerStatus != ReadStatus.Done ? containerStatus : firstRaw;
        diagnostic = typed && containerStatus != ReadStatus.Done ? containerDiagnostic : rawDiagnostic;
        var findings = new List<FontToleranceFinding>();
        if (typed && containerStatus == ReadStatus.Done)
        {
            if (!budget.Buffer(count, faceLocation, out var cacheDiagnostic))
            { result = ReadStatus.ResourceLimit; diagnostic = cacheDiagnostic; }
            else
            {
                var requested = new bool[count];
                for (int i = 0; i < count; i++)
                    requested[i] = requestedTag is null || rows[i].Tag == requestedTag;
                var session = new ValidationSession(directory, rows, budget, options, findings);
                for (int i = 0; i < count; i++)
                {
                    if (!requested[i]) continue;
                    ReadStatus status = session.Validate(i, out var itemDiagnostic);
                    if (result == ReadStatus.Done && status != ReadStatus.Done)
                    { result = status; diagnostic = itemDiagnostic; }
                }
            }
        }
        FontTableInspection[] output = rows;
        if (requestedTag is uint tag)
        {
            int outputCount = matches == 0 ? 1 : matches;
            if (!budget.Buffer((long)outputCount * Unsafe.SizeOf<FontTableInspection>(), faceLocation, out var selectionDiagnostic))
            {
                // The admitted rows remain available even when selecting an owned
                // result array is refused; the requested tag remains in the report.
                result = ReadStatus.ResourceLimit; diagnostic = selectionDiagnostic;
            }
            else
            {
                output = new FontTableInspection[outputCount];
                if (matches != 0)
                {
                    int at = 0;
                    foreach (var row in rows) if (row.Tag == tag) output[at++] = row;
                }
                else
                {
                    ReadStatus missingStatus;
                    FontDiagnostic missingDiagnostic;
                    if (!budget.Work(count, faceLocation, out missingDiagnostic)) missingStatus = ReadStatus.ResourceLimit;
                    else missingStatus = directory.TryFindUniqueTable(tag, out _, out missingDiagnostic);
                    output[0] = new(-1, tag, null, missingDiagnostic.Location, missingStatus, missingDiagnostic);
                    if (typed && containerStatus == ReadStatus.Done)
                        output[0] = output[0] with { ValidationStatus = missingStatus, ValidationDiagnostic = missingDiagnostic,
                            ValidationScope = "Requested table presence" };
                    if (result == ReadStatus.Done)
                    { result = missingStatus; diagnostic = missingDiagnostic; }
                }
            }
        }
        report = new(directory, requestedTag, containerStatus, containerDiagnostic,
            budget.Usage, budget.Limits, output, typed ? [.. findings] : null);
        return result;
    }

    private ref struct ValidationSession
    {
        private readonly SfntDirectoryView _directory;
        private readonly FontTableInspection[] _rows;
        private readonly BuildBudget _admission;
        private readonly FontValidationOptions? _options;
        private readonly List<FontToleranceFinding> _findings;
        private HeadTable? _head;
        private MaxpTable? _maxp;
        private HheaTable? _hhea;
        private VheaTable? _vhea;
        private FvarTable? _fvar;
        private LocaTable? _loca;
        private GdefLookupContext? _gdef;
        private LayoutListsTable? _gsub, _gpos;
        private int? _cpalEntries, _cvtCount;

        internal ValidationSession(SfntDirectoryView directory, FontTableInspection[] rows,
            BuildBudget admission, FontValidationOptions? options,
            List<FontToleranceFinding> findings)
        {
            _directory = directory; _rows = rows; _admission = admission;
            _options = options; _findings = findings;
            _head = null; _maxp = null; _hhea = null; _vhea = null; _fvar = null;
            _loca = null; _gdef = null; _gsub = null; _gpos = null;
            _cpalEntries = null; _cvtCount = null;
        }

        internal ReadStatus Validate(int ordinal, out FontDiagnostic diagnostic)
        {
            var row = _rows[ordinal];
            if (row.ValidationStatus is ReadStatus cached)
            { diagnostic = row.ValidationDiagnostic.GetValueOrDefault(); return cached; }
            var dependencies = new List<FontTableDependency>();
            ReadStatus status;
            if (row.InspectionStatus != ReadStatus.Done)
            { status = row.InspectionStatus; diagnostic = row.InspectionDiagnostic; }
            else if (!_admission.Work(1, row.Location, out diagnostic)) status = ReadStatus.ResourceLimit;
            else status = ValidateCore(ordinal, dependencies, out diagnostic);
            if (!_admission.Buffer((long)dependencies.Count * Unsafe.SizeOf<FontTableDependency>(),
                row.Location, out var dependencyDiagnostic))
            { status = ReadStatus.ResourceLimit; diagnostic = dependencyDiagnostic; }
            else _rows[ordinal] = _rows[ordinal] with { Dependencies = Array.AsReadOnly(dependencies.ToArray()) };
            _rows[ordinal] = _rows[ordinal] with { ValidationStatus = status, ValidationDiagnostic = diagnostic };
            return status;
        }

        private ReadStatus Dependency(uint tag, bool required, bool validate,
            int owner, List<FontTableDependency> dependencies, out int ordinal, out FontDiagnostic diagnostic)
        {
            ordinal = -1;
            if (!_admission.Work(_rows.Length, _rows[owner].Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            ReadStatus status = _directory.TryFindUniqueTable(tag, out ordinal, out diagnostic);
            DataLocation location = diagnostic.Location;
            if (status == ReadStatus.Done)
            {
                location = _rows[ordinal].Location;
                status = validate ? Validate(ordinal, out diagnostic) : _rows[ordinal].InspectionStatus;
                if (!validate) diagnostic = _rows[ordinal].InspectionDiagnostic;
            }
            if (dependencies.Count == dependencies.Capacity)
            {
                int capacity = dependencies.Capacity == 0 ? 4 : dependencies.Capacity * 2;
                if (!_admission.Buffer((long)capacity * Unsafe.SizeOf<FontTableDependency>(),
                    _rows[owner].Location, out var allocationDiagnostic))
                { diagnostic = allocationDiagnostic; return ReadStatus.ResourceLimit; }
                dependencies.EnsureCapacity(capacity);
            }
            dependencies.Add(new(tag, required, status, location, diagnostic));
            if (!required && status == ReadStatus.MissingTable)
            { diagnostic = default; return ReadStatus.Done; }
            return status;
        }

        private ReadStatus Need(uint tag, int owner, List<FontTableDependency> dependencies,
            out FontDiagnostic diagnostic) => Dependency(tag, true, true, owner, dependencies, out _, out diagnostic);

        private ReadStatus Optional(uint tag, int owner, List<FontTableDependency> dependencies,
            out FontDiagnostic diagnostic) => Dependency(tag, false, true, owner, dependencies, out _, out diagnostic);

        private ReadStatus Presence(uint tag, bool required, int owner, List<FontTableDependency> dependencies,
            out int ordinal, out FontDiagnostic diagnostic) =>
            Dependency(tag, required, false, owner, dependencies, out ordinal, out diagnostic);

        private void Version(int ordinal, uint value) => _rows[ordinal] = _rows[ordinal] with { RawVersion = value };
        private void Scope(int ordinal, string value) => _rows[ordinal] = _rows[ordinal] with { ValidationScope = value };

        private ReadStatus ValidateCore(int ordinal, List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            var row = _rows[ordinal];
            uint tag = row.Tag.GetValueOrDefault();
            DataLocation location = row.Location;
            Scope(ordinal, "Official table validator and listed same-face context");
            ReadStatus status = _directory.TryGetTableBytes(ordinal, out var bytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            switch (tag)
            {
                case Head:
                {
                    status = HeadView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version);
                    return view.TryRead(out _head, out diagnostic, _admission.Limits);
                }
                case Maxp:
                {
                    status = MaxpView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version);
                    return view.TryRead(out _maxp, out diagnostic, _admission.Limits);
                }
                case Name:
                {
                    status = NameView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Format);
                    Scope(ordinal, "Official name validation with UTF-16 interpretation and bounded referenced payload");
                    return view.Validate(out diagnostic, (int)System.Math.Min(int.MaxValue, _admission.Limits.MaxTemporaryBytes));
                }
                case Os2:
                {
                    status = Os2View.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version);
                    // A finding is evidence of an admitted deviation, so it is published
                    // only when the whole table passes: a later strict rule failure must
                    // not leave a "tolerated" entry behind.
                    var os2Findings = new List<FontToleranceFinding>();
                    status = view.Validate(_options?.TolerateLegacyDialects == true,
                        os2Findings, out diagnostic);
                    if (status == ReadStatus.Done && os2Findings.Count > 0)
                        _findings.AddRange(os2Findings);
                    return status;
                }
                case Post: return ValidatePost(ordinal, bytes, deps, out diagnostic);
                case Hhea:
                {
                    status = HheaView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, ((uint)view.MajorVersion << 16) | view.MinorVersion);
                    return view.TryRead(out _hhea, out diagnostic, _admission.Limits);
                }
                case Vhea:
                {
                    status = VheaView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.VersionRaw);
                    return view.TryRead(out _vhea, out diagnostic, _admission.Limits);
                }
                case Hmtx:
                {
                    status = Need(Hhea, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = HmtxContext.TryCreate(_hhea!, _maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = HmtxView.TryOpen(bytes, out var view, out diagnostic, location);
                    return status == ReadStatus.Done ? view.Validate(context!, out diagnostic) : status;
                }
                case Vmtx:
                {
                    status = Need(Vhea, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = VmtxContext.TryCreate(_vhea!, _maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = VmtxView.TryOpen(bytes, out var view, out diagnostic, location);
                    return status == ReadStatus.Done ? view.Validate(context!, out diagnostic) : status;
                }
                case Vorg:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = VorgContext.TryCreate(_maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = VorgView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, ((uint)view.MajorVersion << 16) | view.MinorVersion);
                    return view.Validate(context!, out diagnostic);
                }
                case Meta:
                {
                    status = MetaView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.Validate(out diagnostic, _admission.Limits);
                }
                case Dsig:
                {
                    status = DsigView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); Scope(ordinal, "DSIG structure; cryptographic signature not verified");
                    return view.Validate(out diagnostic, _admission.Limits);
                }
                case Hdmx:
                case Ltsh:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = LegacyMetricsContext.TryCreate(_maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (tag == Hdmx)
                    {
                        status = HdmxView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.Validate(context!, out diagnostic, _admission.Limits);
                    }
                    else
                    {
                        status = LtshView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.Validate(context!, out diagnostic, _admission.Limits);
                    }
                }
                case Pclt:
                {
                    status = PcltView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.VersionRaw); return view.Validate(out diagnostic, _admission.Limits);
                }
                case Vdmx:
                {
                    status = VdmxView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.Validate(out diagnostic, _admission.Limits);
                }
                case Cmap:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = CmapContext.TryCreate(_maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = CmapView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.Validate(context!, out diagnostic, _admission.Limits);
                }
                case Cff:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Cff1View.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    status = view.TryRead(out var named, out var cid, out var flavor, out diagnostic, _admission.Limits);
                    if (status != ReadStatus.Done) return status;
                    int glyphs = named?.NumGlyphs ?? cid!.NumGlyphs;
                    byte major = named?.HeaderMajor ?? cid!.HeaderMajor, minor = named?.HeaderMinor ?? cid!.HeaderMinor;
                    Version(ordinal, ((uint)major << 16) | minor);
                    _rows[ordinal] = _rows[ordinal] with { Profile = flavor == Cff1Flavor.CidKeyed ? "CFF1 CID-keyed" : "CFF1 name-keyed" };
                    if (_maxp!.NumGlyphs != glyphs)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            _maxp.Location.At(DataField.NumGlyphs, MaxpLayout.NumGlyphsAt), out diagnostic,
                            _maxp.NumGlyphs, (ulong)glyphs);
                    return ReadStatus.Done;
                }
                case Loca:
                {
                    status = Need(Head, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Presence(Glyf, true, ordinal, deps, out int glyfOrdinal, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = LocaContext.TryCreate(_head!, _maxp!, location, out var context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = LocaView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    status = view.Validate(context!, (int)_rows[glyfOrdinal].Record!.Value.Length, out diagnostic);
                    return status == ReadStatus.Done ? view.TryRead(context!, out _loca, out diagnostic, _admission.Limits) : status;
                }
                case Glyf:
                {
                    status = Need(Loca, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = GlyfView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Scope(ordinal, "Composite-aware glyph decoding and component graph verification");
                    return view.TryReadComposites(_loca!, out _, out diagnostic, _admission.Limits, true);
                }
                case Cvt:
                {
                    status = CvtView.TryOpenCvt(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    status = view.TryRead(out _, out diagnostic, _admission.Limits);
                    if (status == ReadStatus.Done) _cvtCount = view.ValueCount;
                    return status;
                }
                case Fpgm:
                case Prep:
                {
                    var kind = tag == Fpgm ? InstructionProgramKind.FontProgram : InstructionProgramKind.ControlValueProgram;
                    status = InstructionProgramView.TryOpen(bytes, kind, out var view, out diagnostic, location);
                    return status == ReadStatus.Done ? view.Validate(out diagnostic) : status;
                }
                case Gasp:
                {
                    status = GaspView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.Validate(out diagnostic);
                }
                case Base: return ValidateBase(ordinal, bytes, deps, out diagnostic);
                case Gdef: return ValidateGdef(ordinal, bytes, deps, out diagnostic);
                case Gsub:
                case Gpos: return ValidateLayout(ordinal, bytes, deps, out diagnostic);
                case Jstf:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Optional(Gsub, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Optional(Gpos, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Optional(Gdef, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = JstfView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version);
                    return view.ValidateTable(_maxp!.NumGlyphs, _gsub is null ? 0 : _gsub.Lookups.Length,
                        _gpos is null ? 0 : _gpos.Lookups.Length, _gdef, out diagnostic, _admission.Limits);
                }
                case Math:
                case Kern:
                case Merg:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    if (tag == Math)
                    {
                        status = MathView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.ValidateTable(_maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                    }
                    if (tag == Kern)
                    {
                        status = KernView.TryOpen(bytes, out var view, out diagnostic, location);
                        return status == ReadStatus.Done ? view.ValidateTable(_maxp!.NumGlyphs, out diagnostic, _admission.Limits) : status;
                    }
                    status = MergView.TryOpen(bytes, out var merg, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, merg.Version); return merg.ValidateTable(_maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                }
                case Cff2:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Optional(Fvar, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Cff2View.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, ((uint)view.MajorVersion << 16) | view.MinorVersion);
                    Scope(ordinal, "Official CFF2 static/store-fvar signature contract; absent fvar retained in dependency evidence");
                    return view.ValidateTable(_maxp!.NumGlyphs, _fvar?.AxisCount, out diagnostic, _admission.Limits);
                }
                case Fvar:
                {
                    status = FvarView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.TryReadTable(out _fvar, out diagnostic, _admission.Limits);
                }
                case Avar:
                case Gvar:
                case Cvar:
                case Hvar:
                case Vvar:
                case Mvar:
                {
                    status = Need(Fvar, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    if (tag == Avar)
                    {
                        status = AvarView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.ValidateTable(_fvar!.AxisCount, out diagnostic, _admission.Limits);
                    }
                    if (tag == Cvar)
                    {
                        status = Need(Cvt, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                        status = CvarView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.ValidateTable(_fvar!.AxisCount, _cvtCount!.Value, out diagnostic, _admission.Limits);
                    }
                    if (tag == Mvar)
                    {
                        status = MvarView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.ValidateTable(_fvar!.AxisCount, out diagnostic, _admission.Limits);
                    }
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    if (tag == Gvar)
                    {
                        status = Need(Loca, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                        status = Presence(Glyf, true, ordinal, deps, out _, out diagnostic); if (status != ReadStatus.Done) return status;
                        status = GvarView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); Scope(ordinal, "Official gvar tuple structure/count domains; no variation application");
                        return view.ValidateTable(_fvar!.AxisCount, _maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                    }
                    if (tag == Hvar)
                    {
                        status = Need(Hmtx, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                        status = HvarView.TryOpen(bytes, out var view, out diagnostic, location);
                        if (status != ReadStatus.Done) return status;
                        Version(ordinal, view.Version); return view.ValidateTable(_fvar!.AxisCount, _maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                    }
                    status = Need(Vmtx, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = VvarView.TryOpen(bytes, out var vvar, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, vvar.Version); return vvar.ValidateTable(_fvar!.AxisCount, _maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                }
                case Stat:
                {
                    status = Optional(Fvar, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = StatView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.ValidateTable(_fvar, out diagnostic, _admission.Limits);
                }
                case Cpal:
                {
                    status = CpalView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); status = view.ValidateTable(out diagnostic, _admission.Limits);
                    if (status == ReadStatus.Done) _cpalEntries = view.NumPaletteEntries;
                    return status;
                }
                case Colr:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = Optional(Cpal, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = ColrView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version);
                    bool store = false;
                    if (view.Version == 1)
                    {
                        status = view.TryGetItemVariationStoreOffset(out uint offset, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        store = offset != 0;
                    }
                    status = Dependency(Fvar, store, true, ordinal, deps, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    Scope(ordinal, "Official COLR glyph/paint/store validation; CPAL palette domain conditional on actual CPAL context");
                    return view.ValidateTable(_maxp!.NumGlyphs, _cpalEntries, _fvar?.AxisCount, out diagnostic, _admission.Limits);
                }
                case Svg:
                {
                    status = SvgView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.ValidateTable(out diagnostic, _admission.Limits);
                }
                case Sbix:
                {
                    status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                    status = SbixView.TryOpen(bytes, out var view, out diagnostic, location);
                    if (status != ReadStatus.Done) return status;
                    Version(ordinal, view.Version); return view.ValidateTable(_maxp!.NumGlyphs, out diagnostic, _admission.Limits);
                }
                case Cbdt:
                case Cblc:
                case Ebdt:
                case Eblc:
                case Ebsc: return ValidateBitmap(ordinal, bytes, deps, out diagnostic);
                default:
                    Scope(ordinal, "Raw table only; no official typed table dispatch");
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnknownTable,
                        location, out diagnostic, tag);
            }
        }

        private ReadStatus ValidatePost(int ordinal, ReadOnlySpan<byte> bytes,
            List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            DataLocation location = _rows[ordinal].Location;
            ReadStatus status = PostView.TryOpen(bytes, out var view, out diagnostic, location);
            if (status != ReadStatus.Done) return status;
            Version(ordinal, view.Version);
            status = view.TryRead(out var post, out diagnostic, _admission.Limits);
            if (status != ReadStatus.Done) return status;
            if (post is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location, out diagnostic, 0, 1);
            status = Presence(Glyf, false, ordinal, deps, out int glyf, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Presence(Loca, false, ordinal, deps, out int loca, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Presence(Cff, false, ordinal, deps, out int cff, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Presence(Cff2, false, ordinal, deps, out int cff2, out diagnostic); if (status != ReadStatus.Done) return status;
            bool trueType = glyf >= 0 && loca >= 0 && cff < 0 && cff2 < 0;
            bool compact = glyf < 0 && loca < 0 && (cff >= 0) != (cff2 >= 0);
            Scope(ordinal, "Official post model read, outline-table presence/profile policy, and actual maxp version/glyph-count relations");
            if (!(trueType || compact) || cff >= 0 && post.Version != PostLayout.Version30 ||
                post.Version == PostLayout.Version25 && !trueType)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    post.Location.At(DataField.Version, 0), out diagnostic, post.Version);
            status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            if (trueType && _maxp is not Maxp10Table || compact && _maxp is not Maxp05Table ||
                post.NumGlyphs.HasValue && post.NumGlyphs.Value != _maxp!.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    _maxp!.Location.At(DataField.NumGlyphs, MaxpLayout.NumGlyphsAt), out diagnostic,
                    _maxp.NumGlyphs, post.NumGlyphs.GetValueOrDefault());
            return ReadStatus.Done;
        }

        private ReadStatus ValidateBase(int ordinal, ReadOnlySpan<byte> bytes,
            List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            ReadStatus status = BaseView.TryOpen(bytes, out var view, out diagnostic, _rows[ordinal].Location);
            if (status != ReadStatus.Done) return status;
            Version(ordinal, view.Version);
            bool store = false;
            if (view.Version == BaseLayout.Version11)
            {
                status = view.TryOpenItemVariationStoreAt(BaseLayout.ItemVariationStoreAt, -1,
                    out _, out store, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Dependency(Fvar, store, true, ordinal, deps, out _, out diagnostic); if (status != ReadStatus.Done) return status;
            return view.ValidateTable(_maxp!.NumGlyphs, _fvar?.AxisCount ?? -1, out diagnostic, _admission.Limits);
        }

        private ReadStatus ValidateGdef(int ordinal, ReadOnlySpan<byte> bytes,
            List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            ReadStatus status = GdefView.TryOpen(bytes, out var view, out diagnostic, _rows[ordinal].Location);
            if (status != ReadStatus.Done) return status;
            Version(ordinal, view.Version);
            bool store = false;
            if (view.Version == GdefLayout.Version13)
            {
                status = view.TryOpenItemVariationStoreAt(GdefLayout.VariationStoreAt, -1,
                    out _, out store, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Dependency(Fvar, store, true, ordinal, deps, out _, out diagnostic); if (status != ReadStatus.Done) return status;
            status = view.TryReadTable(_maxp!.NumGlyphs, _fvar?.AxisCount ?? -1, out var model,
                out diagnostic, _admission.Limits);
            if (status != ReadStatus.Done) return status;
            // The whole read proved the optional header and child relationship.
            // Preserve that actual child's table-coordinate position, rather than
            // re-reading the private generated header or inventing an offset.
            ushort markSetsOffset = 0;
            if (model!.MarkGlyphSets is not null)
            {
                DataLocation child = model.MarkGlyphSets.Location;
                if (child.Offset < model.Location.Offset)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        child, out diagnostic, child.Offset, model.Location.Offset);
                ulong relative = child.Offset - model.Location.Offset;
                if (relative > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        child, out diagnostic, relative, ushort.MaxValue);
                markSetsOffset = checked((ushort)relative);
            }
            var classes = new GdefClassDefinitions(model.Version, model.NumGlyphs, model.GlyphClasses,
                model.MarkAttachmentClasses, view.AttachListOffset, view.LigCaretListOffset,
                markSetsOffset, model.ItemVariationStoreOffset, model.Location);
            return GdefLookupContext.TryCreate(classes, model.MarkGlyphSets, model.ItemVariationStore,
                out _gdef, out diagnostic, _admission.Limits);
        }

        private ReadStatus ValidateLayout(int ordinal, ReadOnlySpan<byte> bytes,
            List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            var kind = _rows[ordinal].Tag == Gsub ? LayoutLookupKind.Gsub : LayoutLookupKind.Gpos;
            ReadStatus status = Need(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            status = Optional(Gdef, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            status = LayoutListsView.TryOpen(bytes, out var view, out diagnostic, _rows[ordinal].Location);
            if (status != ReadStatus.Done) return status;
            Version(ordinal, view.Version);
            status = view.TryReadTable(_maxp!.NumGlyphs, kind, _gdef, out var model, out diagnostic, _admission.Limits);
            if (status != ReadStatus.Done) return status;
            if (kind == LayoutLookupKind.Gsub) _gsub = model; else _gpos = model;
            Scope(ordinal, "Whole layout model, actual GDEF/store, actual fvar FeatureVariations domain; metadata-only bodies explicitly refused");
            if (model!.FeatureVariations is not null)
            {
                status = Need(Fvar, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
                status = model.FeatureVariations.ValidateTable(_fvar, model.Features.Length, out diagnostic, _admission.Limits);
                if (status != ReadStatus.Done) return status;
            }
            int metadata = 0;
            ReadStatus first = ReadStatus.Done;
            FontDiagnostic firstDiagnostic = default;
            for (int i = 0; i < model.Lookups.Length; i++)
            {
                var lookup = model.Lookups[i];
                if (HasTypedBody(lookup)) continue;
                metadata++;
                status = view.TryOpenLookupList(kind, out var list, out _, out diagnostic);
                if (status == ReadStatus.Done)
                {
                    // Open again only to obtain the formal typed refusal and its
                    // exact bounded provenance for the metadata-only body.
                    status = list.TryOpenLookup(kind, i, out var leaf, out _, out diagnostic);
                    if (status == ReadStatus.Done)
                        status = leaf.Validate(_maxp!.NumGlyphs, kind, _gdef, model.Lookups.Length,
                            out diagnostic, _admission.Limits);
                }
                if (first == ReadStatus.Done && status != ReadStatus.Done)
                { first = status; firstDiagnostic = diagnostic; }
            }
            _rows[ordinal] = _rows[ordinal] with { MetadataOnlyLookupCount = metadata };
            diagnostic = firstDiagnostic;
            return first;
        }

        private static bool HasTypedBody(LookupTable lookup) =>
            lookup.SingleSubstitutions is not null || lookup.SinglePositions is not null ||
            lookup.Multiples is not null || lookup.Alternates is not null || lookup.Ligatures is not null ||
            lookup.Extensions is not null || lookup.PairPositions is not null || lookup.CursiveAttachments is not null ||
            lookup.Contextuals is not null || lookup.Chainings is not null || lookup.ReverseChains is not null ||
            lookup.MarkBases is not null || lookup.MarkLigs is not null || lookup.MarkMarks is not null ||
            lookup.ExtensionPositions is not null;

        private ReadStatus ValidateBitmap(int ordinal, ReadOnlySpan<byte> bytes,
            List<FontTableDependency> deps, out FontDiagnostic diagnostic)
        {
            uint tag = _rows[ordinal].Tag.GetValueOrDefault();
            bool color = tag is Cbdt or Cblc;
            uint dataTag = color ? Cbdt : Ebdt, locatorTag = color ? Cblc : Eblc;
            ReadStatus status = Optional(Maxp, ordinal, deps, out diagnostic); if (status != ReadStatus.Done) return status;
            int dataOrdinal = ordinal, locatorOrdinal = ordinal;
            if (tag != dataTag)
            {
                status = Presence(dataTag, true, ordinal, deps, out dataOrdinal, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            if (tag != locatorTag)
            {
                status = Presence(locatorTag, true, ordinal, deps, out locatorOrdinal, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            status = _directory.TryGetTableBytes(dataOrdinal, out var data, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = _directory.TryGetTableBytes(locatorOrdinal, out var locator, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int? glyphs = _maxp?.NumGlyphs;
            Scope(ordinal, tag is Cblc or Eblc ? "Official bitmap locator formats and image-data extents against actual same-face data length; payload frames checked by the data-table entry" : tag == Ebsc ? "Official EBSC scale records and source-strike relations against actual same-face EBLC and EBDT length" : "Official joint bitmap locator and payload-frame validation with actual same-face tables and optional actual maxp count");
            if (color)
            {
                status = CblcView.TryOpen(locator, out var locatorView, out diagnostic, _rows[locatorOrdinal].Location);
                if (status != ReadStatus.Done) return status;
                if (tag == Cblc)
                {
                    Version(ordinal, ((uint)locatorView.MajorVersion << 16) | locatorView.MinorVersion);
                    return locatorView.ValidateTable(data.Length, glyphs, out diagnostic, _admission.Limits);
                }
                status = CbdtView.TryOpen(data, out var view, out diagnostic, _rows[dataOrdinal].Location);
                if (status != ReadStatus.Done) return status;
                Version(ordinal, ((uint)view.MajorVersion << 16) | view.MinorVersion);
                return view.TryReadTable(locatorView, glyphs, out _, out diagnostic, _admission.Limits);
            }
            status = EblcView.TryOpen(locator, out var eblc, out diagnostic, _rows[locatorOrdinal].Location);
            if (status != ReadStatus.Done) return status;
            if (tag == Eblc)
            {
                Version(ordinal, ((uint)eblc.MajorVersion << 16) | eblc.MinorVersion);
                return eblc.ValidateTable(data.Length, glyphs, out diagnostic, _admission.Limits);
            }
            if (tag == Ebsc)
            {
                status = EbscView.TryOpen(bytes, out var view, out diagnostic, _rows[ordinal].Location);
                if (status != ReadStatus.Done) return status;
                Version(ordinal, ((uint)view.MajorVersion << 16) | view.MinorVersion);
                return view.ValidateTable(eblc, data.Length, glyphs, out diagnostic, _admission.Limits);
            }
            status = EbdtView.TryOpen(data, out var ebdt, out diagnostic, _rows[dataOrdinal].Location);
            if (status != ReadStatus.Done) return status;
            Version(ordinal, ((uint)ebdt.MajorVersion << 16) | ebdt.MinorVersion);
            return ebdt.ValidateTable(eblc, glyphs, out diagnostic, _admission.Limits);
        }
    }
}
