using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Debug table (<c>Debg</c>) storing a UTF-8 JSON payload (fonttools convention).
/// </summary>
[OtTable("Debg", 0)]
public readonly partial struct DebgTable
{
    public int Length => _table.Length;

    /// <summary>
    /// Raw UTF-8 JSON payload bytes.
    /// </summary>
    public ReadOnlySpan<byte> JsonUtf8 => _table.Span;
}

