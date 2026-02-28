using System.Text;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>PCLT</c> table.
/// </summary>
public sealed partial class PcltTableBuilder
{
    public void SetTypefaceString(string value)
    {
        SetAsciiPadded(value, _typeface);
        MarkDirty();
    }

    public void SetCharacterComplementString(string value)
    {
        SetAsciiPadded(value, _characterComplement);
        MarkDirty();
    }

    public void SetFileNameString(string value)
    {
        SetAsciiPadded(value, _fileName);
        MarkDirty();
    }

    private static void SetAsciiPadded(string value, byte[] target)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        int byteCount = Encoding.ASCII.GetByteCount(value);
        if (byteCount > target.Length)
            throw new ArgumentOutOfRangeException(nameof(value), $"String must be <= {target.Length} ASCII bytes.");

        target.AsSpan().Fill(0x20);
        Encoding.ASCII.GetBytes(value.AsSpan(), target);
    }
}

