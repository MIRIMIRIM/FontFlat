using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One DICT operand: an integer value, or a real number kept as its verbatim
/// wire bytes (RealBytes non-null; reals are never decoded to a number).</summary>
public readonly record struct Cff1Operand(int Value, byte[]? RealBytes)
{
    public bool IsReal => RealBytes is not null;
}

/// <summary>One decoded DICT entry: operator, operand list and the entry's raw wire
/// bytes for byte-faithful re-emission of untouched entries.</summary>
public sealed record Cff1DictEntry(int Operator, IReadOnlyList<Cff1Operand> Operands, byte[] RawBytes)
{
    // RawBytes carries the original wire span for diagnostics; canonical emission
    // always re-encodes from Operands.
}

/// <summary>One encoding supplement mapping: a character code plus the SID of the
/// glyph name it resolves to through the charset (TN5176 supplement pairs).</summary>
public readonly record struct Cff1EncodingSupplement(int Code, int Sid);

/// <summary>Owned name-keyed CFF1 model. Charstring, subroutine and string bytes are
/// preserved verbatim and never interpreted; this is not a Type 2 interpreter, a
/// sanitizer or a glyph closure engine. Produced by Cff1View.TryRead and edited by
/// Cff1TableCompiler.TryApply.</summary>
public sealed class Cff1Table
{
    private readonly byte[] _nameBytes;
    private readonly Cff1DictEntry[] _topDict;
    private readonly byte[][] _strings;
    private readonly byte[][] _globalSubrs;
    private readonly Cff1DictEntry[] _privateDict;
    private readonly bool _hasLocalSubrs;
    private readonly byte[][] _localSubrs;
    private readonly Cff1CharsetForm _charsetForm;
    private readonly int[] _charsetSids;
    private readonly Cff1EncodingForm _encodingForm;
    private readonly int[] _encodingCodes;
    private readonly Cff1EncodingSupplement[] _encodingSupplements;
    private readonly byte[][] _charStrings;
    private Dictionary<string, int>? _glyphNameMap;
    private Dictionary<int, int>? _codeToGlyph;

    internal Cff1Table(byte major, byte minor, byte hdrSize, byte[] nameBytes,
        Cff1DictEntry[] topDict, byte[][] strings, byte[][] globalSubrs,
        byte[][] localSubrs, bool hasLocalSubrs, Cff1DictEntry[] privateDict,
        Cff1CharsetForm charsetForm, int[] charsetSids, Cff1EncodingForm encodingForm,
        int[] encodingCodes, Cff1EncodingSupplement[] encodingSupplements,
        byte[][] charStrings, DataLocation location)
    {
        HeaderMajor = major;
        HeaderMinor = minor;
        HeaderHdrSize = hdrSize;
        _nameBytes = nameBytes;
        _topDict = topDict;
        _strings = strings;
        _globalSubrs = globalSubrs;
        _localSubrs = localSubrs;
        _hasLocalSubrs = hasLocalSubrs;
        _privateDict = privateDict;
        _charsetForm = charsetForm;
        _charsetSids = charsetSids;
        _encodingForm = encodingForm;
        _encodingCodes = encodingCodes;
        _encodingSupplements = encodingSupplements;
        _charStrings = charStrings;
        Location = location;
    }

    public byte HeaderMajor { get; }

    /// <summary>Wide-read fact: a minor version above zero was accepted leniently by
    /// strict validation; 0 is the strict 1.0 form.</summary>
    public byte HeaderMinor { get; }

    public byte HeaderHdrSize { get; }

    public DataLocation Location { get; }

    /// <summary>The single Name INDEX entry decoded as a PostScript-style name
    /// (Latin-1 byte map; the bytes themselves stay available through NameBytes).</summary>
    public string FontName => Encoding.Latin1.GetString(_nameBytes);

    public ReadOnlySpan<byte> NameBytes => _nameBytes;

    public int NumGlyphs => _charStrings.Length;

    public int StringCount => _strings.Length;

    public int GlobalSubrCount => _globalSubrs.Length;

    public int LocalSubrCount => _localSubrs.Length;

    public bool HasLocalSubrs => _hasLocalSubrs;

    public Cff1CharsetForm CharsetForm => _charsetForm;

    public Cff1EncodingForm EncodingForm => _encodingForm;

    public ReadOnlySpan<Cff1DictEntry> TopDict => _topDict;

    public ReadOnlySpan<Cff1DictEntry> PrivateDict => _privateDict;

    public ReadOnlySpan<byte[]> Strings => _strings;

    public ReadOnlySpan<byte[]> GlobalSubrs => _globalSubrs;

    public ReadOnlySpan<byte[]> LocalSubrs => _localSubrs;

    public ReadOnlySpan<byte[]> CharStrings => _charStrings;

    public ReadOnlySpan<Cff1EncodingSupplement> EncodingSupplements => _encodingSupplements;

    /// <summary>Charset SID per glyph id (glyph 0 is .notdef with SID 0); predefined
    /// charsets are expanded from the embedded standard tables.</summary>
    public ReadOnlySpan<int> CharsetSids => _charsetSids;

    /// <summary>Character code per glyph id for a custom encoding (0 means the glyph
    /// carries no code); empty for predefined encodings. Supplements are separate.</summary>
    public ReadOnlySpan<int> EncodingCodes => _encodingCodes;

    /// <summary>SID of one glyph's charset name; glyph 0 reports 0 (.notdef).</summary>
    public ReadStatus TryGetCharsetSid(int gid, out int sid, out FontDiagnostic diagnostic)
    {
        sid = 0;
        if (gid < 0 || gid >= _charsetSids.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                unchecked((ulong)(long)gid), (uint)_charsetSids.Length);
        sid = _charsetSids[gid];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>String of one glyph: the charset SID resolved through the standard
    /// strings (SID below 391) or the String INDEX.</summary>
    public ReadStatus TryGetGlyphName(int gid, out string? name, out FontDiagnostic diagnostic)
    {
        name = null;
        ReadStatus status = TryGetCharsetSid(gid, out int sid, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return SidName(sid, out name, out diagnostic);
    }

    /// <summary>Charset enumeration as (glyph, SID) pairs in glyph order, including
    /// glyph 0 (.notdef, SID 0).</summary>
    public ReadStatus TryGetSidGlyphs(out Cff1SidGlyph[]? glyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        glyphs = null;
        var budget = new BuildBudget(limits);
        long bytes = 12L * _charsetSids.Length + 16;
        if (!budget.Work(bytes, Location, out diagnostic) ||
            !budget.Buffer(bytes, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new Cff1SidGlyph[_charsetSids.Length];
        for (int gid = 0; gid < _charsetSids.Length; gid++)
            result[gid] = new Cff1SidGlyph(gid, _charsetSids[gid]);
        glyphs = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Encoding lookup of one code (0..255). Custom encodings resolve through
    /// their code tables and supplements; predefined Standard/Expert encodings resolve
    /// the code to a SID and match the glyph whose charset name carries it. A miss is
    /// Done with mapped=false, never a structural failure.</summary>
    public ReadStatus TryMapCode(int code, out bool mapped, out int glyph, out FontDiagnostic diagnostic)
    {
        mapped = false;
        glyph = 0;
        if ((uint)code > 0xFF)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (_encodingForm.Predefined)
        {
            int sid = Cff1Predefined.EncodingSid(_encodingForm.Format, code);
            if (sid == 0)
            {
                diagnostic = default;
                return ReadStatus.Done;
            }
            ReadStatus status = SidName(sid, out string? name, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var map = BuildGlyphNameMap();
            mapped = map!.TryGetValue(name!, out glyph);
            diagnostic = default;
            return ReadStatus.Done;
        }
        var codes = _codeToGlyph ??= BuildCodeToGlyph();
        if (codes.TryGetValue(code, out glyph))
        {
            mapped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        foreach (var supplement in _encodingSupplements)
        {
            if (supplement.Code != code)
                continue;
            ReadStatus status = SidName(supplement.Sid, out string? name, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var names = BuildGlyphNameMap();
            mapped = names!.TryGetValue(name!, out glyph);
            diagnostic = default;
            return ReadStatus.Done;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Standard-encoding resolution for the Type 2 endchar seac form (S3 card
    /// §2.2): the code is always interpreted through the predefined Standard encoding —
    /// never the face's own encoding form, which may be Expert or custom, while the seac
    /// reference is defined against the standard codes (TN5176 Type 2 appendix) — and
    /// the SID's standard name matches the charset by name. sid=0 (the code has no
    /// Standard entry) and mapped=false (the name is not in the charset) are the
    /// caller's refusal inputs, never structural failures.</summary>
    internal ReadStatus TryMapStandardCode(int code, out int sid, out bool mapped,
        out int glyph, out FontDiagnostic diagnostic)
    {
        mapped = false;
        glyph = 0;
        sid = Cff1Predefined.EncodingSid(0, code);
        if (sid == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = SidName(sid, out string? name, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var map = BuildGlyphNameMap();
        mapped = map.TryGetValue(name!, out glyph);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus SidName(int sid, out string? name, out FontDiagnostic diagnostic)
    {
        name = null;
        if (sid < 0 || sid >= Cff1Layout.StandardStringCount + _strings.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Cid, 0), out diagnostic, (uint)sid,
                (uint)(Cff1Layout.StandardStringCount + _strings.Length));
        if (sid < Cff1Layout.StandardStringCount)
        {
            name = Cff1StandardStrings.Names[sid];
        }
        else
        {
            var bytes = _strings[sid - Cff1Layout.StandardStringCount];
            name = Encoding.Latin1.GetString(bytes);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private Dictionary<string, int> BuildGlyphNameMap()
    {
        if (_glyphNameMap is Dictionary<string, int> map)
            return map;
        map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int gid = 1; gid < _charsetSids.Length; gid++)
        {
            int sid = _charsetSids[gid];
            string? name;
            if (sid < Cff1Layout.StandardStringCount)
            {
                if (sid == 0)
                    continue;
                name = Cff1StandardStrings.Names[sid];
            }
            else if (sid - Cff1Layout.StandardStringCount < _strings.Length)
            {
                name = Encoding.Latin1.GetString(_strings[sid - Cff1Layout.StandardStringCount]);
            }
            else
            {
                continue;
            }
            if (!map.ContainsKey(name))
                map[name] = gid;
        }
        map[".notdef"] = 0;
        _glyphNameMap = map;
        return map;
    }

    private Dictionary<int, int> BuildCodeToGlyph()
    {
        var map = new Dictionary<int, int>();
        for (int gid = 1; gid < _encodingCodes.Length; gid++)
        {
            int code = _encodingCodes[gid];
            if (code != 0 && !map.ContainsKey(code))
                map[code] = gid;
        }
        return map;
    }
}
