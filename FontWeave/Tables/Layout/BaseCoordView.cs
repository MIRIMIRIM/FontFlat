using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct BaseCoordView
{
    // The per-version record readers are generated private hooks; these wrappers
    // publish them to the whole-table walk (the CaretValueView precedent). The version
    // gate is each generated reader's own branch - a format-2 record read under
    // another format refuses inactive before touching the bytes.
    public ReadStatus TryGetFormat2Header(out Format2HeaderData header, out FontDiagnostic diagnostic) =>
        TryReadFormat2HeaderAt(BaseCoordLayout.Format2HeaderField, -1, out header, out diagnostic);

    public ReadStatus TryGetFormat3Header(out Format3HeaderData header, out FontDiagnostic diagnostic) =>
        TryReadFormat3HeaderAt(BaseCoordLayout.Format3HeaderField, -1, out header, out diagnostic);

    // The walk reads the format word directly, so the known-version gate needs this
    // explicit face (an unknown baseCoordFormat is the native UnsupportedVersion
    // refusal at the format field, the generated branch's own shape).
    internal ReadStatus TryRequireKnownFormat(out FontDiagnostic diagnostic) =>
        TryRequireKnownVersion(out _, out diagnostic);
}
