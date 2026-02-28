using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// FontForge time stamp table (<c>FFTM</c>).
/// </summary>
[OtTable("FFTM", 28, GenerateBuilder = true)]
[OtField("Version", OtFieldKind.UInt32, 0, HasDefaultValue = true, DefaultValue = 1)]
[OtField("FFTimeStamp", OtFieldKind.UInt64, 4)]
[OtField("SourceCreated", OtFieldKind.UInt64, 12)]
[OtField("SourceModified", OtFieldKind.UInt64, 20)]
public readonly partial struct FftmTable
{
    public DateTime GetFFTimeStampUtc() => LongDateTime.FromSecondsSince1904Utc(unchecked((long)FFTimeStamp));
    public DateTime GetSourceCreatedUtc() => LongDateTime.FromSecondsSince1904Utc(unchecked((long)SourceCreated));
    public DateTime GetSourceModifiedUtc() => LongDateTime.FromSecondsSince1904Utc(unchecked((long)SourceModified));
}

