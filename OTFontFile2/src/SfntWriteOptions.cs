namespace OTFontFile2;

public enum SfntTableOrdering
{
    PreserveInputOrder = 0,
    ByTagAscending = 1
}

public sealed class SfntWriteOptions
{
    public SfntTableOrdering TableOrdering { get; set; } = SfntTableOrdering.ByTagAscending;

    /// <summary>
    /// If a 'head' table is present, compute and write a valid checkSumAdjustment value.
    /// </summary>
    public bool WriteHeadCheckSumAdjustment { get; set; } = true;
}

