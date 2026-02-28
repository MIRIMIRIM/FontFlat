using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using OTFontFile;

namespace OTFontFile.Benchmarks.Benchmarks;

/// <summary>
/// 验证快速收益优化的性能提升
/// 包括：
/// 1. FileOptions 优化 (I/O 顺序读取)
/// 2. uint 字符串比较 (标签比较优化)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class QuickWinsBenchmarks
{
    private Dictionary<uint, string> _tagCacheUint;
    private Dictionary<string, string> _tagCacheString;
    private uint[] _testTags;

    [GlobalSetup]
    public void Setup()
    {
        // 使用 uint 进行标签比较
        _tagCacheUint = new Dictionary<uint, string>
        {
            { 0x676C7966, "glyf" }, // 0x 'glyf'
            { 0x43464620, "CFF " }, // CFF 
            { 0x43464632, "CFF2" }, // CFF2
            { 0x43424454, "CBDT" }, // CBDT
            { 0x45424454, "EBDT" }, // EBDT
            { 0x53564720, "SVG " }, // SVG 
            { 0x68656164, "head" }, // head
            { 0x6D617870, "maxp" }, // maxp
            { 0x68686561, "hhea" }, // hhea
            { 0x686D7478, "hmtx" }, // hmtx
            { 0x6E616D65, "name" }, // name
            { 0x636D6170, "cmap" }, // cmap
            { 0x4F532F32, "OS/2" }, // OS/2
            { 0x506F7374, "post" }, // post
        };

        // 使用 string 进行标签比较（旧方式）
        _tagCacheString = new Dictionary<string, string>
        {
            { "glyf", "glyf" },
            { "CFF ", "CFF " },
            { "CFF2", "CFF2" },
            { "CBDT", "CBDT" },
            { "EBDT", "EBDT" },
            { "SVG ", "SVG " },
            { "head", "head" },
            { "maxp", "maxp" },
            { "hhea", "hhea" },
            { "hmtx", "hmtx" },
            { "name", "name" },
            { "cmap", "cmap" },
            { "OS/2", "OS/2" },
            { "post", "post" },
        };

        _testTags = new uint[]
        {
            0x676C7966, // glyf
            0x68656164, // head
            0x636D6170, // cmap
            0x686D7478, // hmtx
            0x6E616D65, // name
        };
    }

    /// <summary>
    /// 测试使用 uint 进行标签查找的性能（新方式）
    /// </summary>
    [Benchmark]
    public void TagLookup_Uint()
    {
        var result = new List<string>();
        foreach (var tag in _testTags)
        {
            if (_tagCacheUint.TryGetValue(tag, out var value))
            {
                result.Add(value);
            }
        }
    }

    /// <summary>
    /// 测试使用 string 进行标签查找的性能（旧方式）
    /// </summary>
    [Benchmark]
    public void TagLookup_String()
    {
        var result = new List<string>();
        foreach (var tag in _testTags)
        {
            string tagString = new string(new[] 
            { 
                (char)((tag >> 24) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)(tag & 0xFF)
            });
            
            if (_tagCacheString.TryGetValue(tagString, out var value))
            {
                result.Add(value);
            }
        }
    }

    /// <summary>
    /// 测试 uint 标签比较的性能（新方式）
    /// </summary>
    [Benchmark]
    public void TagComparison_Uint()
    {
        uint largeTable = 0x676C7966; // glyf
        int count = 0;
        
        // 模拟 ShouldUseLazyLoad 的逻辑
        foreach (var tag in _testTags)
        {
            if (tag == largeTable)
            {
                count++;
            }
        }
    }

    /// <summary>
    /// 测试 string 标签比较的性能（旧方式）
    /// </summary>
    [Benchmark]
    public void TagComparison_String()
    {
        string largeTable = "glyf";
        int count = 0;
        
        // 模拟旧的 ShouldUseLazyLoad 的逻辑
        foreach (var tag in _testTags)
        {
            string tagString = new string(new[] 
            { 
                (char)((tag >> 24) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)(tag & 0xFF)
            });
            
            if (tagString == largeTable)
            {
                count++;
            }
        }
    }

    /// <summary>
    /// 测试 uint HashSet 查找的性能（新方式）
    /// </summary>
    [Benchmark]
    public void HashSetLookup_Uint()
    {
        var largeTableTags = new HashSet<uint>
        {
            0x676C7966, // glyf
            0x43464620, // CFF 
            0x43464632, // CFF2
            0x43424454, // CBDT
            0x45424454, // EBDT
            0x53564720, // SVG 
        };
        
        int count = 0;
        foreach (var tag in _testTags)
        {
            if (largeTableTags.Contains(tag))
            {
                count++;
            }
        }
    }

    /// <summary>
    /// 测试 string HashSet 查找的性能（旧方式）
    /// </summary>
    [Benchmark]
    public void HashSetLookup_String()
    {
        var largeTableTags = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "glyf",
            "CFF ",
            "CFF2",
            "CBDT",
            "EBDT",
            "SVG ",
        };
        
        int count = 0;
        foreach (var tag in _testTags)
        {
            string tagString = new string(new[] 
            { 
                (char)((tag >> 24) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)(tag & 0xFF)
            });
            
            if (largeTableTags.Contains(tagString))
            {
                count++;
            }
        }
    }
}
