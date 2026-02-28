namespace CJKCharacterCount.Core;

public static class CJKTableRegistry
{
    private static readonly Dictionary<string, CJKTable> _tables = new();

    // Standard resources
    public static readonly CJKTable Gb2312 = Load("gb2312", "gb2312-han");
    public static readonly CJKTable Changyong3500 = Load("3500changyong", "3500changyong-han");
    public static readonly CJKTable Tongyong7000 = Load("7000tongyong", "7000tongyong-han");
    public static readonly CJKTable YiwuJiaoyu = Load("yiwu-jiaoyu", "yiwu-jiaoyu-han");
    public static readonly CJKTable TongyongGuifan = Load("tongyong-guifan", "tongyong-guifan-han");
    
    public static readonly CJKTable HanyiJianfan = Load("hanyi-jianfan", "hanyi-jianfan-han");
    public static readonly CJKTable FangzhengJianfan = Load("fangzheng-jianfan", "fangzheng-jianfan-han");
    public static readonly CJKTable IICore = Load("iicore", "iicore-han");
    
    public static readonly CJKTable Big5 = Load("big5", "big5-han");
    public static readonly CJKTable Big5Changyong = Load("big5changyong", "big5changyong-han");
    public static readonly CJKTable Changyong4808 = Load("4808changyong", "4808changyong-han");
    public static readonly CJKTable Cichangyong6343 = Load("6343cichangyong", "6343cichangyong-han");
    public static readonly CJKTable JF7000Core = Load("jf7000-core", "jf7000-core-han");
    public static readonly CJKTable HKChangyong = Load("hkchangyong", "hkchangyong-han");
    public static readonly CJKTable HKSCS = Load("hkscs", "hkscs-han");
    public static readonly CJKTable GB12345 = Load("gb12345", "gb12345-han");
    public static readonly CJKTable Gujiyinshua = Load("gujiyinshua", "gujiyinshua-han");
    public static readonly CJKTable Suppchara = Load("suppchara", "suppchara-han");

    // Special Algorithmic Tables
    public static readonly CJKTable GBK;
    public static readonly CJKTable GB18030;

    static CJKTableRegistry()
    {
        // Define GBK
        var gbkChars = new HashSet<int>();
        gbkChars.Add(0x3007); // ZERO
        // 4E00-9FA5
        for (int i = 0x4E00; i <= 0x9FA5; i++) gbkChars.Add(i);
        // Compatibility
        foreach (var c in GbkCompatibilityList) gbkChars.Add(c);

        GBK = new CJKTable("gbk", CJKGroup.Mixed, 
            new Dictionary<string, string> { ["en"] = "GBK", ["zhs"] = "GBK", ["zht"] = "GBK" }, 
            gbkChars);
        _tables["gbk"] = GBK;

        // Define GB18030 (Level 1 implementation per Python source)
        // ZERO + Unified + ExtA + NonCompat
        var gb18030Chars = new HashSet<int>();
        AddBlock(gb18030Chars, UnicodeBlocks.CjkZero);
        AddBlock(gb18030Chars, UnicodeBlocks.CjkUnifiedIdeographs);
        AddBlock(gb18030Chars, UnicodeBlocks.CjkExtensionA);
        AddBlock(gb18030Chars, UnicodeBlocks.CjkNonCompatibilityIdeographs);

        GB18030 = new CJKTable("gb18030", CJKGroup.Mixed,
            new Dictionary<string, string> { ["en"] = "GB18030", ["zhs"] = "GB18030", ["zht"] = "GB18030" },
            gb18030Chars);
        _tables["gb18030"] = GB18030;
    }

    private static void AddBlock(HashSet<int> set, UnicodeBlock block)
    {
        var ranges = block.AssignedRanges.Span;
        foreach (var range in ranges)
        {
            for (int i = range.Start; i <= range.End; i++)
                set.Add(i);
        }
    }

    private static CJKTable Load(string name, string resourceName)
    {
        var t = CJKTable.LoadFromResource(resourceName + ".txt");
        _tables[name] = t;
        return t;
    }

    private static readonly int[] GbkCompatibilityList = 
    [
        0xFA0E, 0xFA0F, 0xFA11, 0xFA13, 0xFA14, 0xFA1F, 
        0xFA21, 0xFA23, 0xFA24, 0xFA27, 0xFA28, 0xFA29, 
        0xF92C, 0xF979, 0xF995, 0xF9E7, 0xF9F1, 0xFA0C, 
        0xFA0D, 0xFA18, 0xFA20 
    ];

    public static IEnumerable<CJKTable> GetByGroup(CJKGroup group)
    {
        return GetPredefinedOrder(group);
    }

    private static IEnumerable<CJKTable> GetPredefinedOrder(CJKGroup group)
    {
        if (group == CJKGroup.Simplified)
        {
            yield return Gb2312;
            yield return Changyong3500;
            yield return Tongyong7000;
            yield return YiwuJiaoyu;
            yield return TongyongGuifan;
        }
        else if (group == CJKGroup.Mixed)
        {
            yield return HanyiJianfan;
            yield return FangzhengJianfan;
            yield return IICore;
            yield return GBK;
            yield return GB18030;
        }
        else if (group == CJKGroup.Traditional)
        {
            yield return Changyong4808;
            yield return Cichangyong6343;
            yield return Big5;
            yield return Big5Changyong;
            yield return JF7000Core;
            yield return HKChangyong;
            yield return HKSCS;
            yield return Suppchara;
            yield return GB12345;
            yield return Gujiyinshua;
        }
    }
}
