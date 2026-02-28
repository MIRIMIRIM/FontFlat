namespace OTFontFile2;

public interface ISfntTableSource
{
    Tag Tag { get; }
    int Length { get; }

    /// <summary>
    /// Checksum value to be written into the table directory entry.
    /// For 'head', the checkSumAdjustment field is treated as 0 (per OpenType spec).
    /// </summary>
    uint GetDirectoryChecksum();

    /// <summary>
    /// Writes the table bytes to the destination stream.
    /// If this is the 'head' table, <paramref name="headCheckSumAdjustment"/> should be written at offset 8.
    /// For non-'head' tables, the value should be ignored.
    /// </summary>
    void WriteTo(Stream destination, uint headCheckSumAdjustment);
}

