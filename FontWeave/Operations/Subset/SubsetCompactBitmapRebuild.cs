using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap bitmap strikes and the sbix/SVG document lists (SE card
/// §2.2-§2.5).
///
/// The CBDT/CBLC and EBDT/EBLC pairs rebuild jointly: the joint read chain
/// materializes the strikes and the pool in canonical chain order, the walk prunes
/// per slot (the k-th populated slot pairs with the k-th image, OQ-SE-2 — the same
/// order the compilers' fixup contract itself consumes), the index layer splits the
/// contiguous formats into maximal retained runs and filters the sparse arrays, the
/// strike rows keep every non-rewritten field verbatim with the surviving span, and
/// empty strikes (and an all-empty pair) drop (OQ-SE-5). Composite frames'
/// component glyph references remap; a dangling component refuses (RE3, OQ-SE-6).
/// The rebuilt parts re-answer the full pairing matrix through the compiler
/// factories, which re-derive every offset/sbitOffset/pool position from the k-th
/// pairing. EBSC rides the rebuilt EBLC strike set: the paired validated read
/// supplies the chain's own faces (a missing EBLC partner propagates as RE2), each
/// record survives only while its substitute ppem pair still names a surviving
/// strike, records travel verbatim, and an all-dead record set drops the table.
/// sbix re-slots every strike over the output domain with 'dupe' targets remapped
/// (a dangling target refuses, RE3) and SVG re-spans the document records over each
/// range's surviving span with only the referenced documents kept — bytes verbatim,
/// no decompression, no recompression. Every rebuilt model re-answers through the
/// compiler factories; any failure refuses with nothing published.</summary>
public static partial class SubsetOperation
{
    // ---- CBDT/CBLC ----

    private static ReadStatus RebuildCbdtCblc(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? cbdtBytes, out byte[]? cblcBytes,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        cbdtBytes = null;
        cblcBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Cbdt, out var cbdtAt,
            out var cbdtSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Cblc, out var cblcAt, out var cblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CblcView.TryOpen(cblcSource, out var cblcView, out diagnostic, cblcAt);
        if (status != ReadStatus.Done) return status;
        status = cblcView.TryReadTable(cbdtSource.Length, maxp.NumGlyphs,
            out var cblc, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CbdtView.TryOpen(cbdtSource, out var cbdtView, out diagnostic, cbdtAt);
        if (status != ReadStatus.Done) return status;
        status = cbdtView.TryReadTable(cblcView, maxp.NumGlyphs, out var cbdt,
            out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var newStrikes = new List<CblcStrike>();
        var newImages = new List<CbdtImage>();
        status = RebuildCblcStrikes(cblc!, cbdt!, map, cbdtAt, newStrikes, newImages,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (newImages.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = CbdtTableCompiler.TryCreate(3, 0, newImages, out var newCbdt,
            out diagnostic, location: cbdtAt);
        if (status != ReadStatus.Done) return status;
        status = CblcTableCompiler.TryCreate(3, 0, newStrikes, newCbdt!,
            out var newCblc, out diagnostic, location: cblcAt);
        if (status != ReadStatus.Done) return status;
        // TryCompilePair re-derives the canonical index layer against the packed
        // pool one more time (the k-th pairing contract) and emits both tables.
        status = CblcTableCompiler.TryCompilePair(newCblc!, newCbdt!, out cblcBytes,
            out cbdtBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ChargeCompiled(budget, cblcAt, cblcBytes!, out cblcBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, cbdtAt, cbdtBytes!, out cbdtBytes, out diagnostic);
    }

    /// <summary>The joint chain walk: strikes in wire order, records in wire order,
    /// glyph slots ascending, absent forms skipped — the order the pool arrived in.
    /// Contiguous formats split into maximal retained runs (the record's own index
    /// and image formats preserved); sparse arrays filter slot by slot; the strike
    /// rows keep every non-rewritten field verbatim with the surviving span.</summary>
    private static ReadStatus RebuildCblcStrikes(CblcTable cblc, CbdtTable cbdt,
        GlyphRemap map, DataLocation cbdtAt, List<CblcStrike> newStrikes,
        List<CbdtImage> newImages, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status;
        var images = cbdt.Images;
        int imageCursor = 0;
        foreach (var strike in cblc.Sizes)
        {
            var newSubtables = new List<CblcIndexSubtable>();
            int strikeMin = -1, strikeMax = -1;
            FontDiagnostic emitDiagnostic = default;
            // One run of kept slots: the images join the new pool in chain order
            // with composite component references remapped (a dangling one refuses,
            // RE3); the record lands with the run's mapped bounds.
            ReadStatus EmitRun(List<(int Glyph, CbdtImage Image)> slots,
                Func<int, int, CblcIndexSubtable> build)
            {
                int runStart = -1;
                for (int s = 0; s <= slots.Count; s++)
                {
                    bool kept = s < slots.Count && map.OutputOf(slots[s].Glyph) >= 0;
                    if (kept)
                    {
                        if (runStart < 0) runStart = s;
                        // The strike row's surviving span lives in the output domain
                        // (the output face's glyph space), so the remapped value is
                        // what the min/max tracks.
                        int output = map.OutputOf(slots[s].Glyph);
                        if (strikeMin < 0 || output < strikeMin) strikeMin = output;
                        if (output > strikeMax) strikeMax = output;
                        newImages.Add(RemappedImage(slots[s].Image, map, cbdtAt,
                            out emitDiagnostic));
                        if (emitDiagnostic.Code != 0) return ReadStatus.InvalidFormat;
                    }
                    if (runStart >= 0 && (!kept || s == slots.Count))
                    {
                        newSubtables.Add(build(runStart, s - 1));
                        runStart = -1;
                    }
                }
                return ReadStatus.Done;
            }
            foreach (var part in strike.Subtables)
            {
                // One (input glyph, image) pair per populated slot, in chain order;
                // the image consumption mirrors the reader's own slot enumeration
                // (CountSlots), which is what keeps the k-th pairing aligned.
                var slots = new List<(int Glyph, CbdtImage Image)>();
                switch (part)
                {
                    case CblcOffsetSubtable offsets:
                    {
                        int span = offsets.SbitOffsets.Length - 1;
                        if (!budget.Work(span, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < span; k++)
                        {
                            if (offsets.SbitOffsets[k] == offsets.SbitOffsets[k + 1])
                                continue;
                            slots.Add((offsets.FirstGlyphIndex + k, images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) => new CblcOffsetSubtable(newSubtables.Count,
                                offsets.IndexFormat, offsets.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph),
                                RunPattern(slots, first, last, map)));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcImageSizeSubtable size:
                    {
                        if (size.ImageSize == 0) break;
                        if (!budget.Work(size.LastGlyphIndex - size.FirstGlyphIndex + 1,
                                cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++)
                            slots.Add((g, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) => new CblcImageSizeSubtable(newSubtables.Count,
                                size.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph), size.ImageSize,
                                size.BigMetrics));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcPairSubtable pairs:
                    {
                        int count = pairs.GlyphIds.Length - 1;
                        if (!budget.Work(count, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < count; k++)
                        {
                            if (pairs.SbitOffsets[k] == pairs.SbitOffsets[k + 1]) continue;
                            slots.Add((pairs.GlyphIds[k], images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 2];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                glyphs[^1] = 0xFFFF; // the sizing sentinel
                                var offsets = new ushort[glyphs.Length];
                                for (int s = 0; s < offsets.Length; s++)
                                    offsets[s] = (ushort)s;
                                return new CblcPairSubtable(newSubtables.Count,
                                    pairs.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph), glyphs,
                                    offsets);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcGlyphIdArraySubtable array:
                    {
                        if (array.ImageSize == 0) break;
                        if (!budget.Work(array.GlyphIds.Length, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        foreach (ushort glyph in array.GlyphIds)
                            slots.Add((glyph, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 1];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                return new CblcGlyphIdArraySubtable(newSubtables.Count,
                                    array.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph),
                                    array.ImageSize, array.BigMetrics, glyphs);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                }
            }
            if (newSubtables.Count == 0) continue;
            newStrikes.Add(new CblcStrike(0, 0, strike.ColorRef, strike.HoriMetrics,
                strike.VertMetrics, (ushort)strikeMin, (ushort)strikeMax, strike.PpemX,
                strike.PpemY, strike.BitDepth, strike.Flags, [.. newSubtables]));
        }
        return ReadStatus.Done;
    }

    /// <summary>One pool frame verbatim except a composite frame's component glyph
    /// references, which remap through the frozen map (RE3 on a dangling one).</summary>
    private static CbdtImage RemappedImage(CbdtImage image, GlyphRemap map,
        DataLocation cbdtAt, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (image.ImageFormat is not (8 or 9) || image.Components.Length == 0)
            return image;
        var components = new CbdtComponent[image.Components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            var component = image.Components[i];
            int output = map.OutputOf(component.GlyphId);
            if (output < 0)
            {
                _ = ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    cbdtAt.AtLocal(DataField.GlyphId, image.SourcePosition ?? 0, i),
                    out diagnostic, component.GlyphId, 0UL);
                return image;
            }
            components[i] = new((ushort)output, component.XOffset, component.YOffset);
        }
        return new CbdtImage(image.ImageFormat, image.Metrics, image.Payload,
            image.BitDepth, components, image.CompositePad);
    }

    /// <summary>One format-1/3 run's offset pattern over the record's full output
    /// span: the run's kept slots land populated (adjacent entries differ) in chain
    /// order and every other output glyph of the span — an interior source-absent
    /// form — stays an equal-offset absent pair. The values themselves re-derive
    /// from the paired frames at the compiler's rewrite; only the populated
    /// pattern travels.</summary>
    private static uint[] RunPattern(List<(int Glyph, CbdtImage Image)> slots,
        int first, int last, GlyphRemap map)
    {
        int outFirst = map.OutputOf(slots[first].Glyph);
        int outLast = map.OutputOf(slots[last].Glyph);
        var pattern = new uint[outLast - outFirst + 2];
        int kept = first;
        uint run = 0;
        for (int o = 0; o < pattern.Length - 1; o++)
        {
            pattern[o] = run;
            if (kept <= last && map.OutputOf(slots[kept].Glyph) == outFirst + o)
            {
                run++;
                kept++;
            }
        }
        pattern[^1] = run;
        return pattern;
    }

    /// <summary>The EBDT/EBLC mirror of the run pattern (the same index codec).</summary>
    private static uint[] RunPattern(List<(int Glyph, EbdtImage Image)> slots,
        int first, int last, GlyphRemap map)
    {
        int outFirst = map.OutputOf(slots[first].Glyph);
        int outLast = map.OutputOf(slots[last].Glyph);
        var pattern = new uint[outLast - outFirst + 2];
        int kept = first;
        uint run = 0;
        for (int o = 0; o < pattern.Length - 1; o++)
        {
            pattern[o] = run;
            if (kept <= last && map.OutputOf(slots[kept].Glyph) == outFirst + o)
            {
                run++;
                kept++;
            }
        }
        pattern[^1] = run;
        return pattern;
    }

    // ---- EBDT/EBLC (+ EBSC linkage) ----

    private static ReadStatus RebuildEbdtEblc(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? ebdtBytes, out byte[]? eblcBytes,
        out HashSet<int> survivingStrikes, out bool dropped, out FontDiagnostic diagnostic)
    {
        ebdtBytes = null;
        eblcBytes = null;
        survivingStrikes = [];
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Ebdt, out var ebdtAt,
            out var ebdtSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Eblc, out var eblcAt, out var eblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EblcView.TryOpen(eblcSource, out var eblcView, out diagnostic, eblcAt);
        if (status != ReadStatus.Done) return status;
        status = eblcView.TryReadTable(ebdtSource.Length, maxp.NumGlyphs, out var eblc,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = EbdtView.TryOpen(ebdtSource, out var ebdtView, out diagnostic, ebdtAt);
        if (status != ReadStatus.Done) return status;
        status = ebdtView.TryReadTable(eblcView, maxp.NumGlyphs, out var ebdt,
            out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var newStrikes = new List<EblcStrike>();
        var newImages = new List<EbdtImage>();
        status = RebuildEblcStrikes(eblc!, ebdt!, map, ebdtAt, newStrikes, newImages,
            survivingStrikes, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (newImages.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = EbdtTableCompiler.TryCreate(2, 0, newImages, out var newEbdt,
            out diagnostic, location: ebdtAt);
        if (status != ReadStatus.Done) return status;
        status = EblcTableCompiler.TryCreate(2, 0, newStrikes, newEbdt!,
            out var newEblc, out diagnostic, location: eblcAt);
        if (status != ReadStatus.Done) return status;
        status = EblcTableCompiler.TryCompilePair(newEblc!, newEbdt!, out eblcBytes,
            out ebdtBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ChargeCompiled(budget, eblcAt, eblcBytes!, out eblcBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, ebdtAt, ebdtBytes!, out ebdtBytes, out diagnostic);
    }

    /// <summary>The EBDT/EBLC mirror of the joint chain walk; the surviving
    /// strikes' (ppemX, ppemY) pairs feed the EBSC record filter.</summary>
    private static ReadStatus RebuildEblcStrikes(EblcTable eblc, EbdtTable ebdt,
        GlyphRemap map, DataLocation ebdtAt, List<EblcStrike> newStrikes,
        List<EbdtImage> newImages, HashSet<int> survivingStrikes, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status;
        var images = ebdt.Images;
        int imageCursor = 0;
        foreach (var strike in eblc.Sizes)
        {
            var newSubtables = new List<EblcIndexSubtable>();
            int strikeMin = -1, strikeMax = -1;
            FontDiagnostic emitDiagnostic = default;
            ReadStatus EmitRun(List<(int Glyph, EbdtImage Image)> slots,
                Func<int, int, EblcIndexSubtable> build)
            {
                int runStart = -1;
                for (int s = 0; s <= slots.Count; s++)
                {
                    bool kept = s < slots.Count && map.OutputOf(slots[s].Glyph) >= 0;
                    if (kept)
                    {
                        if (runStart < 0) runStart = s;
                        // The strike row's surviving span lives in the output domain.
                        int output = map.OutputOf(slots[s].Glyph);
                        if (strikeMin < 0 || output < strikeMin) strikeMin = output;
                        if (output > strikeMax) strikeMax = output;
                        newImages.Add(RemappedEbdtImage(slots[s].Image, map, ebdtAt,
                            out emitDiagnostic));
                        if (emitDiagnostic.Code != 0) return ReadStatus.InvalidFormat;
                    }
                    if (runStart >= 0 && (!kept || s == slots.Count))
                    {
                        newSubtables.Add(build(runStart, s - 1));
                        runStart = -1;
                    }
                }
                return ReadStatus.Done;
            }
            foreach (var part in strike.Subtables)
            {
                var slots = new List<(int Glyph, EbdtImage Image)>();
                switch (part)
                {
                    case EblcOffsetSubtable offsets:
                    {
                        int span = offsets.SbitOffsets.Length - 1;
                        if (!budget.Work(span, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < span; k++)
                        {
                            if (offsets.SbitOffsets[k] == offsets.SbitOffsets[k + 1])
                                continue;
                            slots.Add((offsets.FirstGlyphIndex + k, images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) => new EblcOffsetSubtable(newSubtables.Count,
                                offsets.IndexFormat, offsets.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph),
                                RunPattern(slots, first, last, map)));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcImageSizeSubtable size:
                    {
                        if (size.ImageSize == 0) break;
                        if (!budget.Work(size.LastGlyphIndex - size.FirstGlyphIndex + 1,
                                ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++)
                            slots.Add((g, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) => new EblcImageSizeSubtable(newSubtables.Count,
                                size.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph), size.ImageSize,
                                size.BigMetrics));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcPairSubtable pairs:
                    {
                        int count = pairs.GlyphIds.Length - 1;
                        if (!budget.Work(count, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < count; k++)
                        {
                            if (pairs.SbitOffsets[k] == pairs.SbitOffsets[k + 1]) continue;
                            slots.Add((pairs.GlyphIds[k], images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 2];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                glyphs[^1] = 0xFFFF;
                                var offsets = new ushort[glyphs.Length];
                                for (int s = 0; s < offsets.Length; s++)
                                    offsets[s] = (ushort)s;
                                return new EblcPairSubtable(newSubtables.Count,
                                    pairs.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph), glyphs,
                                    offsets);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcGlyphIdArraySubtable array:
                    {
                        if (array.ImageSize == 0) break;
                        if (!budget.Work(array.GlyphIds.Length, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        foreach (ushort glyph in array.GlyphIds)
                            slots.Add((glyph, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 1];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                return new EblcGlyphIdArraySubtable(newSubtables.Count,
                                    array.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph),
                                    array.ImageSize, array.BigMetrics, glyphs);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                }
            }
            if (newSubtables.Count == 0) continue;
            survivingStrikes.Add(EbscTableCompiler.PpemPair(strike.PpemX, strike.PpemY));
            newStrikes.Add(new EblcStrike(0, 0, strike.ColorRef, strike.HoriMetrics,
                strike.VertMetrics, (ushort)strikeMin, (ushort)strikeMax, strike.PpemX,
                strike.PpemY, strike.BitDepth, strike.Flags, [.. newSubtables]));
        }
        return ReadStatus.Done;
    }

    private static EbdtImage RemappedEbdtImage(EbdtImage image, GlyphRemap map,
        DataLocation ebdtAt, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (image.ImageFormat is not (8 or 9) || image.Components.Length == 0)
            return image;
        var components = new EbdtComponent[image.Components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            var component = image.Components[i];
            int output = map.OutputOf(component.GlyphId);
            if (output < 0)
            {
                _ = ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    ebdtAt.AtLocal(DataField.GlyphId, 0, i), out diagnostic,
                    component.GlyphId, 0UL);
                return image;
            }
            components[i] = new((ushort)output, component.XOffset, component.YOffset);
        }
        return new EbdtImage(image.ImageFormat, image.Metrics, image.Data,
            image.BitDepth, components, image.CompositePad);
    }

    /// <summary>EBSC rides the rebuilt EBLC strike set (SE card §2.5): the paired
    /// validated read re-answers the chain's faces (an EBSC without its EBLC partner
    /// refuses at the missing-table face — RE2), records whose substitute ppem pair
    /// no longer names a surviving strike drop, and an all-dead set drops the
    /// table.</summary>
    private static ReadStatus RebuildEbsc(SfntDirectoryView directory, MaxpTable maxp,
        HashSet<int> survivingStrikes, BuildBudget budget, out byte[]? ebscBytes,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        ebscBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Ebsc, out var ebscAt,
            out var ebscSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Eblc, out var eblcAt, out var eblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EblcView.TryOpen(eblcSource, out var eblcView, out diagnostic, eblcAt);
        if (status != ReadStatus.Done) return status;
        status = EbscView.TryOpen(ebscSource, out var ebscView, out diagnostic, ebscAt);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Ebdt, out int ebdtOrdinal,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ebdtOrdinal, out var ebdtSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ebscView.TryReadTable(eblcView, ebdtSource.Length, maxp.NumGlyphs,
            out var ebsc, out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var kept = new List<EbscScaleRecord>();
        foreach (var record in ebsc!.Records)
        {
            if (survivingStrikes.Contains(EbscTableCompiler.PpemPair(
                    record.SubstitutePpemX, record.SubstitutePpemY)))
                kept.Add(record);
        }
        if (kept.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = EbscTableCompiler.TryCreate(ebsc.MajorVersion, ebsc.MinorVersion, kept,
            out var newEbsc, out diagnostic, location: ebscAt);
        if (status != ReadStatus.Done) return status;
        status = EbscTableCompiler.TryCompile(newEbsc!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, ebscAt, bytes!, out ebscBytes, out diagnostic);
    }

    // ---- sbix ----

    private static ReadStatus RebuildSbix(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? sbixBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        sbixBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Sbix, out var sbixAt,
            out var sbixSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = SbixView.TryOpen(sbixSource, out var sbixView, out diagnostic, sbixAt);
        if (status != ReadStatus.Done) return status;
        status = sbixView.TryReadTable(maxp.NumGlyphs, out var sbix, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work((long)sbix!.StrikeCount * outputCount, sbixAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var newStrikes = new List<SbixStrike>();
        for (int strikeIndex = 0; strikeIndex < sbix.StrikeCount; strikeIndex++)
        {
            var strike = sbix.Strikes[strikeIndex];
            var slots = new SbixGlyph?[outputCount];
            bool any = false;
            for (int o = 0; o < outputCount; o++)
            {
                if (strike.Glyphs[inputOrder[o]] is not { } glyph)
                {
                    slots[o] = null;
                    continue;
                }
                if (glyph.GraphicType == SbixLayout.GraphicTypeDupe)
                {
                    // The dupe target remaps through the frozen map; an unselected
                    // target refuses (RE3, the TryWithGlyphPayload dangling-dupe
                    // precedent's shape).
                    int target = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt16BigEndian(glyph.Data);
                    int output = map.OutputOf(target);
                    if (output < 0)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            sbixAt.AtLocal(DataField.GlyphId,
                                SbixSlotSourceAt(sbix.NumGlyphs, sbix.StrikeCount,
                                    strikeIndex, inputOrder[o]), inputOrder[o]),
                            out diagnostic, (ulong)target, 0UL);
                    var payload = (byte[])glyph.Data.Clone();
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                        payload.AsSpan(0), (ushort)output);
                    slots[o] = new SbixGlyph(glyph.OriginOffsetX, glyph.OriginOffsetY,
                        glyph.GraphicType, payload);
                }
                else slots[o] = glyph;
                any = true;
            }
            if (any) newStrikes.Add(new SbixStrike(strike.Ppem, strike.Ppi, slots));
        }
        // An all-strike-empty table drops (OQ-SE-5); empty strikes drop per strike.
        if (newStrikes.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = SbixTableCompiler.TryCreate(sbix.Version, sbix.Flags, outputCount,
            newStrikes, out var newSbix, out diagnostic, location: sbixAt);
        if (status != ReadStatus.Done) return status;
        status = SbixTableCompiler.TryCompile(newSbix!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, sbixAt, bytes!, out sbixBytes, out diagnostic);
    }

    /// <summary>The source wire coordinate of one strike slot's dupe target word
    /// (the read layout's own arithmetic, the RE3 diagnostic's provenance).</summary>
    private static ulong SbixSlotSourceAt(int numGlyphs, int strikeCount,
        int strikeIndex, int glyphId)
    {
        ulong at = SbixLayout.StrikeOffsetsAt + 4UL * (uint)strikeCount +
            4UL * (ulong)(uint)strikeIndex * (SbixLayout.StrikeHeaderLength +
                4UL * ((ulong)(uint)numGlyphs + 1UL));
        return at + SbixLayout.StrikeHeaderLength +
            4UL * ((ulong)(uint)numGlyphs + 1UL) + 8UL * (ulong)(uint)glyphId +
            SbixLayout.GlyphDataHeaderLength;
    }

    // ---- SVG ␠ ----

    private static ReadStatus RebuildSvg(SfntDirectoryView directory, GlyphRemap map,
        BuildBudget budget, out byte[]? svgBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        svgBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Svg, out var svgAt,
            out var svgSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = SvgView.TryOpen(svgSource, out var svgView, out diagnostic, svgAt);
        if (status != ReadStatus.Done) return status;
        status = svgView.TryReadTable(out var svg, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var newRecords = new List<SvgDocumentRecord>();
        var newDocuments = new List<byte[]>();
        var documentIndex = new Dictionary<int, int>();
        foreach (var record in svg!.Records)
        {
            if (!TrySurvivingSpan(map, record.StartGlyphID, record.EndGlyphID,
                    out ushort firstSurvivor, out ushort lastSurvivor))
                continue;
            if (!documentIndex.TryGetValue(record.PayloadIndex, out int newIndex))
            {
                newIndex = newDocuments.Count;
                documentIndex[record.PayloadIndex] = newIndex;
                newDocuments.Add(svg.Documents[record.PayloadIndex]);
            }
            newRecords.Add(new((ushort)map.OutputOf(firstSurvivor),
                (ushort)map.OutputOf(lastSurvivor), newIndex));
        }
        if (newRecords.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = SvgTableCompiler.TryCreate(svg.Version, 0, newRecords, newDocuments,
            out var newSvg, out diagnostic, location: svgAt);
        if (status != ReadStatus.Done) return status;
        status = SvgTableCompiler.TryCompile(newSvg!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, svgAt, bytes!, out svgBytes, out diagnostic);
    }
}
