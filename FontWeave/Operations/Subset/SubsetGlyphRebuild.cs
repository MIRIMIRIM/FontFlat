using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>glyf/loca rebuild for the retain-GIDs slice (S1a card §4).
///
/// The rebuild is a byte-faithful splice: retained slots copy their source windows
/// verbatim, unselected slots become empty glyphs, and short-format images pad at most
/// one zero byte per slot to keep offsets even. loca is rebuilt from the resulting
/// offsets in the source format, which head — preserved raw — keeps pointing at.</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildGlyfAndLoca(bool[] retained, LocaTable loca,
        GlyfTable glyf, bool dropHinting, BuildBudget budget, out byte[]? glyfBytes,
        out byte[]? locaBytes, out FontDiagnostic diagnostic)
    {
        glyfBytes = null;
        locaBytes = null;
        ReadStatus status = GlyfTableCompiler.TryRetainSubset(glyf, retained, !loca.LongFormat,
            dropHinting, budget, out var image, out var offsets, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The same-format shape feeds TryFromOffsets: monotonicity, short evenness and
        // the encodable range are enforced there, refusing rather than upgrading (card
        // §1.2 — a format flip is a refusal, not a silent repair).
        status = LocaTableCompiler.TryFromOffsets(loca, offsets!, out var rebuiltLoca,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocaTableCompiler.TryCompile(rebuiltLoca!, out locaBytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        glyfBytes = image;
        return ReadStatus.Done;
    }
}
