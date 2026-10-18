using FontWeave.Core;

namespace FontWeave.Containers;

public static class SfntFaces
{
    public static ReadStatus TryGetFace(this FontSource source, int index, out FontFaceRef face, out FontDiagnostic diagnostic)
    {
        face = default;
        diagnostic = default;
        var location = new DataLocation(source.SourceId, index, 0, -1, DataField.Signature, OffsetBasis.File, 0);
        if (index < 0)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location, out diagnostic);
        if (source.Bytes.Length < 4)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location, out diagnostic, (ulong)source.Bytes.Length, 4);
        uint signature = ReadWire.U32(source.Bytes, 0);
        if (signature is 0x774F4646 or 0x774F4632)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location, out diagnostic, signature);
        if (signature != 0x74746366)
        {
            if (index != 0)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location, out diagnostic, (ulong)index, 1);
            // Raw sfnt inspection does not decide whether the version is supported.
            face = new FontFaceRef(source, 0, 0, false);
            return ReadStatus.Done;
        }
        if (source.Bytes.Length < 12)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location, out diagnostic, (ulong)source.Bytes.Length, 12);
        uint version = ReadWire.U32(source.Bytes, 4);
        if (version is not (0x00010000 or 0x00020000))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.At(DataField.Version, 4), out diagnostic, version);
        uint count = ReadWire.U32(source.Bytes, 8);
        if ((uint)index >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.At(DataField.Count, 8), out diagnostic, (ulong)index, count);
        ulong site = 12UL + 4UL * (uint)index;
        if (!ReadWire.Slice(source.Bytes, site, 4, out var offsetBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.At(DataField.Offset, site), out diagnostic, (ulong)source.Bytes.Length, site + 4);
        face = new FontFaceRef(source, index, ReadWire.U32(offsetBytes, 0), true);
        return ReadStatus.Done;
    }

    // The edit operations keep the standalone-only admission the directory view's former
    // container refusal gave them; validate, inspect and subset admit collection faces
    // through the face-domain directory validation instead.
    internal static ReadStatus RequireStandaloneFace(this FontFaceRef face, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!face.IsCollection) return ReadStatus.Done;
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
            face.Location.At(DataField.Version, face.DirectoryFileOffset), out diagnostic, 0x74746366u);
    }

    public static ReadStatus TryOpenDirectory(this FontFaceRef face, out SfntDirectoryView view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        diagnostic = default;
        FontSource? source = face.Source;
        if (source is null)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, face.Location, out diagnostic);
        if (!ReadWire.Slice(source.Bytes, face.DirectoryFileOffset, 12, out _))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                face.Location, out diagnostic, (ulong)source.Bytes.Length, (ulong)face.DirectoryFileOffset + 12);
        view = new SfntDirectoryView(source.Bytes, face);
        return ReadStatus.Done;
    }
}
