using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>seac reference closure over static CFF1 faces (S3 card §2).
///
/// Type 2 endchar's deprecated seac form (TN5176 Type 2 charstring appendix) references
/// its base and accent glyphs by STANDARD-encoding code, so the retained set must extend
/// to both referenced glyphs or the output renders the composite from missing parts —
/// the S1c compact slice kept the charstring bytes but could drop a referenced glyph
/// (S1c card §2.3's registered gap). The scan is a token-level conservative walk
/// (OQ-S3-2 lean): a per-operator stack-effect table over three-state values (known
/// literal / unknown), the value stack and the hint count persisting across subr call
/// frames, a known literal index descending into the called body through its INDEX's
/// bias (TN5176 §4.7) and an unknown index descending into every body of the called
/// INDEX with an all-unknown stack, plus a call-path cycle guard that terminates loop
/// branches. This is reference enumeration, not a charstring interpreter: no coordinates,
/// no outline and no rendering semantics are modeled, and no charstring or subr byte is
/// ever rewritten. Detection resolves through the Standard encoding regardless of the
/// face's own encoding form (card §2.2); a reference that cannot be decided statically —
/// a code without a Standard entry, a standard name outside the charset, operands still
/// unknown at endchar, or any seac form at all on a CID-keyed face, whose charset has
/// no name domain to resolve through (OQ-S3-3 lean) — refuses the operation with
/// UnsupportedOperation(17) at the model's location, failure atomic. Newly resolved
/// glyphs join the retained set and are scanned in turn (worklist fixed point, the S1a
/// CloseComposites shape); every visited program body charges one work unit and every
/// newly added GID one more (the closure budget's injection point, S0-BF-001).</summary>
public static partial class SubsetOperation
{
    /// <summary>Materializes the CFF table of an admitted static CFF1 face once, after
    /// decide and before any closure or freeze stage (S3 card §1.3, OQ-S3-5): the parked
    /// model feeds the seac closure below, the compact map freeze and the payload
    /// rebuilds, replacing the freeze-time materialization — one TryRead per operation
    /// in both modes (single-parse discipline, S1c card §1.2).</summary>
    private static ReadStatus MaterializeCff1(SfntDirectoryView directory, int cffOrdinal,
        PrepareState state, out FontDiagnostic diagnostic)
    {
        ReadStatus status = directory.TryGetTableBytes(cffOrdinal, out var cffBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = Cff1View.TryOpen(cffBytes, out var view, out diagnostic,
            directory.TableLocation(cffOrdinal));
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out var nameKeyed, out var cidKeyed, out _,
            out diagnostic, state.Budget);
        if (status != ReadStatus.Done) return status;
        state.CffNameKeyed = nameKeyed;
        state.CffCidKeyed = cidKeyed;
        return ReadStatus.Done;
    }

    /// <summary>Extends the retained set to the fixed point of the seac references of
    /// every retained glyph program (card §2.1). Runs after the layout reference union —
    /// layout-added programs are scanned too — and before the map freeze, so the
    /// compact frozen order carries the additions. Both flavors scan for detection;
    /// a detected form resolves name-keyed through the Standard encoding and refuses
    /// CID-keyed, where the charset carries CIDs and no name domain exists.</summary>
    private static ReadStatus CloseSeacReferences(DataLocation faceAt, PrepareState state,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        Cff1Table? nameKeyed = state.CffNameKeyed;
        CidCff1Table? cidKeyed = state.CffCidKeyed;
        if (nameKeyed is null && cidKeyed is null)
        {
            // The materialization step parked the model for every admitted CFF face;
            // a missing one is a pipeline bug, never a request error (the parked-model
            // guard shape of the variable rebuilds).
            diagnostic = new(DiagnosticCode.UnsupportedOperation,
                faceAt with { TableTag = Cff }, Cff);
            return ReadStatus.UnsupportedFormat;
        }

        var added = new List<ushort>();
        var queue = new Queue<int>();
        for (int gid = 0; gid < state.Retained.Length; gid++)
            if (state.Retained[gid]) queue.Enqueue(gid);
        while (queue.Count > 0)
        {
            int gid = queue.Dequeue();
            var scan = new SeacScan(budget, nameKeyed, cidKeyed, gid);
            ReadStatus status = scan.Run(out int bcharCode, out int acharCode,
                out bool found, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!found) continue;
            if (cidKeyed is not null)
            {
                // No name domain: a detected seac form is structurally unresolvable on
                // a CIDFont regardless of its operand values (TN5176: CIDFonts carry no
                // glyph names; OQ-S3-3's structural tier, distinct from a name-keyed
                // face's dangling data).
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    cidKeyed.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                    (ulong)gid, 0UL);
            }
            status = AddSeacReference(nameKeyed!, state, budget, gid, bcharCode,
                added, queue, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = AddSeacReference(nameKeyed!, state, budget, gid, acharCode,
                added, queue, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        added.Sort();
        state.SeacClosureAdded = added;
        return ReadStatus.Done;
    }

    /// <summary>Resolves one seac code through the Standard encoding and adds its
    /// glyph to the retained set (card §2.2/§2.3). GID 0 (.notdef) resolves and adds
    /// nothing; a code without a Standard entry and a name outside the charset are
    /// the face's own dangling references and refuse (the TT composite over-domain
    /// tier of face defects).</summary>
    private static ReadStatus AddSeacReference(Cff1Table model, PrepareState state,
        BuildBudget budget, int gid, int code, List<ushort> added, Queue<int> queue,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = model.TryMapStandardCode(code, out int sid, out bool mapped,
            out int refGid, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sid == 0)
        {
            // Category 1 (card §2.3): the code has no Standard encoding entry, so no
            // glyph could ever answer the reference; Detail is the code, Expected the
            // empty SID.
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                (ulong)code, 0UL);
        }
        if (!mapped)
        {
            // Category 2: the standard name is not in the charset — the input face's
            // own hanging reference, the same tier as a TT composite pointing outside
            // the face's GID domain.
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                (ulong)sid, 0UL);
        }
        if (refGid == 0 || state.Retained[refGid]) return ReadStatus.Done;
        if (!budget.Work(1, model.Location.At(DataField.GlyphId, 0, refGid),
                out diagnostic))
            return ReadStatus.ResourceLimit;
        state.Retained[refGid] = true;
        added.Add((ushort)refGid);
        queue.Enqueue(refGid);
        return ReadStatus.Done;
    }

    /// <summary>TN5176 §4.7: the call bias as a function of the CALLED INDEX's count —
    /// the only bias input, which this closure never moves (the S1c §2.2 frozen fact:
    /// subr INDEX counts are untouched, so both bias inputs stay fixed).</summary>
    private static int SubrBias(int count) =>
        count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    /// <summary>One retained glyph's token-level scan. The stack and hint count live
    /// for the whole glyph walk — subr call frames share them (the card §2.1 census
    /// difference: the census's per-program reset models existence, the closure needs
    /// the cross-boundary stack to catch an endchar whose operands a caller frame
    /// pushed). The path list is the cycle guard: a (kind, index) body already on the
    /// current call path is not re-entered, so loop branches terminate.</summary>
    private sealed class SeacScan
    {
        private const int KindGlyph = 0, KindLocal = 1, KindGlobal = 2;

        // One value stack slot: a known literal or the unknown value. Unknown is the
        // default; a known zero is a real literal and must stay distinct from it.
        private readonly record struct Slot(bool Known, int Value)
        {
            public static Slot Of(int value) => new(true, value);
        }

        private readonly List<Slot> _stack = [];
        private int _hints;
        private readonly List<(int Kind, int Index)> _path = [];
        private readonly BuildBudget _budget;
        private readonly DataLocation _at;
        private readonly int _gid;
        private readonly byte[][] _locals;
        private readonly byte[][] _globals;
        private readonly byte[] _program;

        internal SeacScan(BuildBudget budget, Cff1Table? nameKeyed,
            CidCff1Table? cidKeyed, int gid)
        {
            _budget = budget;
            _gid = gid;
            DataLocation modelAt = cidKeyed is not null
                ? cidKeyed.Location
                : nameKeyed!.Location;
            _at = modelAt.At(DataField.GlyphId, 0, gid);
            _program = cidKeyed is not null ? cidKeyed.CharStrings[gid]
                : nameKeyed!.CharStrings[gid];
            if (cidKeyed is not null)
            {
                cidKeyed.TryGetLocalSubrs(gid, out var perGlyph, out _);
                _locals = perGlyph.ToArray();
                _globals = cidKeyed.GlobalSubrs.ToArray();
            }
            else
            {
                _locals = nameKeyed!.LocalSubrs.ToArray();
                _globals = nameKeyed.GlobalSubrs.ToArray();
            }
        }

        /// <summary>Walks the glyph program. found=true reports the endchar seac
        /// form's two standard-encoding codes; a stack that still holds unknown
        /// operands at a seac endchar refuses (card §2.3 category 3).</summary>
        internal ReadStatus Run(out int bcharCode, out int acharCode, out bool found,
            out FontDiagnostic diagnostic)
        {
            return ScanBody(KindGlyph, _gid, _program, out bcharCode, out acharCode,
                out found, out diagnostic);
        }

        private ReadStatus ScanBody(int kind, int index, byte[] program,
            out int bcharCode, out int acharCode, out bool found,
            out FontDiagnostic diagnostic)
        {
            bcharCode = acharCode = 0;
            found = false;
            diagnostic = default;
            var key = (kind, index);
            if (_path.Contains(key)) return ReadStatus.Done; // cycle branch: stop here
            if (!_budget.Work(1, _at, out diagnostic)) return ReadStatus.ResourceLimit;
            _path.Add(key);
            int i = 0, n = program.Length;
            while (i < n)
            {
                int b = program[i];
                if (b == 28 && i + 2 < n)
                {
                    _stack.Add(Slot.Of((short)((program[i + 1] << 8) | program[i + 2])));
                    i += 3;
                }
                else if (b is >= 32 and <= 246)
                {
                    _stack.Add(Slot.Of(b - 139));
                    i += 1;
                }
                else if (b is >= 247 and <= 250 && i + 1 < n)
                {
                    _stack.Add(Slot.Of((b - 247) * 256 + program[i + 1] + 108));
                    i += 2;
                }
                else if (b is >= 251 and <= 254 && i + 1 < n)
                {
                    _stack.Add(Slot.Of(-(b - 251) * 256 - program[i + 1] - 108));
                    i += 2;
                }
                else if (b == 255 && i + 4 < n)
                {
                    _stack.Add(default); // 16.16 fixed: a value, never a code
                    i += 5;
                }
                else if (b == 12 && i + 1 < n)
                {
                    ReadStatus status = Escape(program[i + 1], out bcharCode,
                        out acharCode, out found, out diagnostic);
                    if (status != ReadStatus.Done || found) return status;
                    i += 2;
                }
                else if (b is 1 or 3 or 18 or 23)
                {
                    // Stem declarations: the width form leaves one extra value, so
                    // pairs advance the hint count that sizes every later mask.
                    _hints += _stack.Count / 2;
                    _stack.Clear();
                    i += 1;
                }
                else if (b is 19 or 20)
                {
                    // (cntr)mask: pop the optional width, skip the hint mask bytes.
                    _stack.Clear();
                    i += 1 + ((_hints + 7) >> 3);
                }
                else if (b is 10 or 29)
                {
                    ReadStatus status = Call(b == 10, out bcharCode, out acharCode,
                        out found, out diagnostic);
                    if (status != ReadStatus.Done || found) return status;
                    i += 1;
                }
                else if (b == 11)
                {
                    break; // return: leave this body, the caller frame continues
                }
                else if (b == 14)
                {
                    // endchar: 4-5 operands = the deprecated seac form (TN5176 Type 2
                    // appendix); the last two values are the base/accent codes. The
                    // census's stack>=4 judgment is the existence lower bound (S1c
                    // oracle.py:280-281); the closure reads the two values and refuses
                    // when they are not statically decidable.
                    if (_stack.Count >= 4)
                    {
                        Slot bchar = _stack[^2], achar = _stack[^1];
                        if (bchar.Known && achar.Known)
                        {
                            found = true;
                            bcharCode = bchar.Value;
                            acharCode = achar.Value;
                        }
                        else
                        {
                            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                                DiagnosticCode.UnsupportedOperation, _at,
                                out diagnostic, (uint)_stack.Count, 4UL);
                        }
                    }
                    return ReadStatus.Done; // endchar ends the glyph program
                }
                else if (b is 4 or 5 or 6 or 7 or 8 or 21 or 22 or 24 or 25 or 26
                    or 27 or 30 or 34 or 35 or 36 or 37)
                {
                    // Path construction: every value on the stack is this operator's
                    // operand (plus the optional width), well-formed programs included.
                    _stack.Clear();
                    i += 1;
                }
                else
                {
                    // Reserved or not modeled: no stack effect is assumed, the
                    // conservative direction (values persist rather than vanish).
                    i += 1;
                }
            }
            _path.RemoveAt(_path.Count - 1);
            return ReadStatus.Done;
        }

        /// <summary>The 12 X escape family: arithmetic and logic produce unknown values
        /// (card §2.3 category 3's computation tier), random always does, and the stack
        /// tools keep their fixed arities (TN5176 Type 2 operator table).</summary>
        private ReadStatus Escape(int op, out int bcharCode, out int acharCode,
            out bool found, out FontDiagnostic diagnostic)
        {
            bcharCode = acharCode = 0;
            found = false;
            diagnostic = default;
            switch (op)
            {
                case 3 or 4 or 15:       // and / or / eq → one unknown result
                    Pop(2);
                    _stack.Add(default);
                    break;
                case 22:                 // ifelse pops its four operands
                    Pop(4);
                    _stack.Add(default);
                    break;
                case 9 or 14 or 26:      // abs / neg / sqrt → unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 10 or 11 or 12 or 24: // add / sub / div / mul → unknown
                    Pop(2);
                    _stack.Add(default);
                    break;
                case 5 or 30:            // not / index → unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 18:                 // drop
                    Pop(1);
                    break;
                case 23:                 // random → unknown
                    _stack.Add(default);
                    break;
                case 28:                 // dup: the copy carries the original's state
                    if (_stack.Count > 0) _stack.Add(_stack[^1]);
                    break;
                case 29:                 // exch
                    if (_stack.Count >= 2)
                        (_stack[^1], _stack[^2]) = (_stack[^2], _stack[^1]);
                    break;
                case 31:                 // roll: pops its two control values
                    Pop(2);
                    break;
                default:                 // unmodeled: assume no effect
                    break;
            }
            return ReadStatus.Done;
        }

        /// <summary>callsubr/callgsubr: pops the index operand, descends the resolved
        /// body through the CALLED INDEX's bias; an unknown index descends every body
        /// of the INDEX with an all-unknown stack (card §2.1's conservative tier — the
        /// runtime selection is not statically decidable, so the scan keeps every
        /// branch instead of guessing one).</summary>
        private ReadStatus Call(bool local, out int bcharCode, out int acharCode,
            out bool found, out FontDiagnostic diagnostic)
        {
            bcharCode = acharCode = 0;
            found = false;
            diagnostic = default;
            byte[][] table = local ? _locals : _globals;
            Slot top = _stack.Count > 0 ? _stack[^1] : default;
            if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
            int kind = local ? KindLocal : KindGlobal;
            if (top.Known)
            {
                int idx = top.Value - SubrBias(table.Length);
                if ((uint)idx < (uint)table.Length)
                    return ScanBody(kind, idx, table[idx], out bcharCode,
                        out acharCode, out found, out diagnostic);
                // Out of range: the program would fault at run time; no body to scan.
                return ReadStatus.Done;
            }
            var saved = _stack.ToArray();
            for (int s = 0; s < table.Length; s++)
            {
                _stack.Clear();
                for (int v = 0; v < saved.Length; v++)
                    _stack.Add(default); // same depth, every value unknown
                ReadStatus status = ScanBody(kind, s, table[s], out bcharCode,
                    out acharCode, out found, out diagnostic);
                if (status != ReadStatus.Done || found) return status;
            }
            _stack.Clear();
            for (int v = 0; v < saved.Length; v++)
                _stack.Add(default); // the caller resumes over unknowable values
            return ReadStatus.Done;
        }

        private void Pop(int count)
        {
            for (int k = 0; k < count && _stack.Count > 0; k++)
                _stack.RemoveAt(_stack.Count - 1);
        }
    }
}
