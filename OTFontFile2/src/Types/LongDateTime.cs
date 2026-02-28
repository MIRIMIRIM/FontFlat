namespace OTFontFile2;

public static class LongDateTime
{
    private static readonly DateTime s_epoch1904Utc = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime FromSecondsSince1904Utc(long seconds)
        => s_epoch1904Utc.AddSeconds(seconds);

    public static long ToSecondsSince1904Utc(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            utc = utc.ToUniversalTime();

        return (long)(utc - s_epoch1904Utc).TotalSeconds;
    }
}

