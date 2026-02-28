using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// ttfautohint parameters table (<c>TTFA</c>).
/// </summary>
[OtTable("TTFA", 0)]
public readonly partial struct TtfaTable
{
    public int Length => _table.Length;

    public ReadOnlySpan<byte> Data => _table.Span;

    public string GetAsciiString() => Encoding.ASCII.GetString(_table.Span);
}

