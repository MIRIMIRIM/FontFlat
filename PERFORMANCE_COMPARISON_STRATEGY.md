# 性能对比策略：如何同时测试优化前后的代码

## 问题

在进行性能优化时，我们需要：
1. ✅ 保留未优化的代码作为**基线**
2. ✅ 运行优化后的代码获取**新数据**
3. ✅ 在**相同环境**下进行对比
4. ✅ 确保测试结果**可靠且可比较**

## 解决方案对比

| 方案 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| **Git 分支** | 简单、无代码重复 | 无法同时运行、频繁切换 | ⭐⭐⭐ |
| **并行项目** | 可同时运行、清晰对比 | 代码重复、占用空间 | ⭐⭐⭐⭐⭐ |
| **条件编译** | 无重复、一键切换 | 代码复杂、混淆逻辑 | ⭐⭐ |
| **运行时切换** | 同一代码、灵活 | 复杂度最高、可能影响性能 | ⭐⭐ |

---

## 🎯 推荐方案：并行项目结构

### 架构设计

```
FontFlat/
├── OTFontFile/                    # ✅ 新优化版本（主要工作区）
│   ├── src/
│   │   ├── MBOBuffer.cs (使用Span)
│   │   ├── OTFile.cs (使用MemoryMappedFile)
│   │   └── ...
│   └── OTFontFile.csproj
│
├── OTFontFile.Baseline/            # ✅ 原始基线版本（只读）
│   ├── src/
│   │   ├── MBOBuffer.cs (原始byte[])
│   │   ├── OTFile.cs (原始FileStream)
│   │   └── ...
│   └── OTFontFile.Baseline.csproj
│
├── OTFontFile.Performance.Tests/   # ✅ 对比测试项目
│   ├── UnitTests/
│   │   ├── BufferTests.Tests.cs
│   │   ├── FileParsingTests.Tests.cs
│   │   └── ComparisonTests.Tests.cs   # ⭐ 新增：新旧对比测试
│   └── OTFontFile.Performance.Tests.csproj (同时引用两个版本)
│
└── OTFontFile.Benchmarks/          # ✅ 对比基准测试
    ├── Benchmarks/
    │   ├── FileLoadingBenchmarks.cs
    │   └── ComparisonBenchmarks.cs       # ⭐ 新增：新旧对比基准
    └── OTFontFile.Benchmarks.csproj (同时引用两个版本)
```

### 关键设计点

1. **命名空间区分**：
   ```csharp
   // OTFontFile (新版本)
   namespace FontFlat
   {
       public class OTFile { /* 使用 MemoryMappedFile */ }
   }

   // OTFontFile.Baseline (原始版本)
   namespace FontFlat.Baseline
   {
       public class OTFile { /* 使用 FileStream */ }
   }
   ```

2. **测试代码可同时引用**：
   ```csharp
   using FontFlat;           // 新版本
   using FontFlat.Baseline;   // 原始版本

   [TestMethod]
   public void CompareMemoryUsage()
   {
       // 测试原始版本
       var baselineResult = MeasureMemoryUsage<FontFlat.Baseline.OTFile>();

       // 测试优化版本
       var optimizedResult = MeasureMemoryUsage<FontFlat.OTFile>();

       // 验证内存减少
       Assert.IsTrue(optimizedResult < baselineResult * 0.5,
           $"期望内存减少50%，实际减少: {100 * (1 - optimizedResult / baselineResult):F1}%");
   }
   ```

3. **基准测试可同时运行**：
   ```csharp
   [MemoryDiagnoser]
   public class FileLoadingComparisonBenchmarks
   {
       [Benchmark(Baseline = true)]
       public void Baseline_LoadSmallFont()
       {
           using var file = new FontFlat.Baseline.OTFile();
           file.open("small.ttf");
       }

       [Benchmark]
       public void Optimized_LoadSmallFont()
       {
           using var file = new FontFlat.OTFile();
           file.open("small.ttf");
       }
   }
   ```

---

## 📋 实施步骤

### Phase 0: 创建 Baseline 项目

```bash
# 1. 复制当前项目（优化前的原始代码）
cp -r OTFontFile OTFontFile.Baseline

# 2. 重命名项目文件
mv OTFontFile.Baseline/OTFontFile.csproj OTFontFile.Baseline/OTFontFile.Baseline.csproj

# 3. 修改命名空间（可使用正则替换）
# 将所有 namespace FontFlat 改为 namespace FontFlat.Baseline
```

**自动化脚本（PowerShell）**：

```powershell
# Create-BaselineProject.ps1

$baselineDir = "OTFontFile.Baseline"
$sourceDir = "OTFontFile"

Write-Host "创建基准项目..." -ForegroundColor Cyan

# 1. 复制项目文件夹
if (Test-Path $baselineDir) {
    Write-Host "✗ 基准项目已存在，正在删除..." -ForegroundColor Yellow
    Remove-Item $baselineDir -Recurse -Force
}

Copy-Item -Path $sourceDir -Destination $baselineDir -Recurse

# 2. 重命名项目文件
Rename-Item -Path "$baselineDir\OTFontFile.csproj" `
            -NewName "OTFontFile.Baseline.csproj"

# 3. 修改项目文件
$projFile = "$baselineDir\OTFontFile.Baseline.csproj"
(Get-Content $projFile) -replace '<Project>.*', '<Project>' -replace 'OTFontFile', 'OTFontFile.Baseline' |
    Set-Content $projFile

# 4. 修改所有源文件的命名空间
Get-ChildItem -Path "$baselineDir\src" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $newContent = $content -replace 'namespace FontFlat', 'namespace FontFlat.Baseline'
    Set-Content -Path $_.FullName -Value $newContent
    Write-Host "  ✓ $($_.Name)" -ForegroundColor Green
}

Write-Host "`n✓ 基准项目创建完成!" -ForegroundColor Green
Write-Host "下一步: 将 OTFontFile.Baseline 添加到解决方案" -ForegroundColor Yellow
```

### Phase 1: 将 Baseline 项目添加到解决方案

```bash
# 方式1: 手动编辑解决方案
# 将 OTFontFile.Baseline.csproj 添加到 FontFlat.slnx

# 方式2: 使用 dotnet CLI
dotnet sln FontFlat.slnx add OTFontFile.Baseline/OTFontFile.Baseline.csproj
```

### Phase 2: 更新测试和基准项目引用

```xml
<!-- OTFontFile.Performance.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\OTFontFile\OTFontFile.csproj" />
  <ProjectReference Include="..\OTFontFile.Baseline\OTFontFile.Baseline.csproj" />
</ItemGroup>

<!-- OTFontFile.Benchmarks.csproj -->
<ItemGroup>
  <ProjectReference Include="..\OTFontFile\OTFontFile.csproj" />
  <ProjectReference Include="..\OTFontFile.Baseline\OTFontFile.Baseline.csproj" />
</ItemGroup>
```

### Phase 3: 创建对比测试

#### 添加对比单元测试

```csharp
// UnitTests/ComparisonTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FontFlat;                    // 优化版本
using FontFlat.Baseline;            // 原始版本

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class ComparisonTests
    {
        private const string TestFontPath = "TestResources/SampleFonts/small.ttf";

        [TestMethod]
        public void Compare_FileLoadingTime()
        {
            // 测试原始版本
            var baselineTime = Measure(() => LoadBaselineFont());

            // 测试优化版本
            var optimizedTime = Measure(() => LoadOptimizedFont());

            // 验证性能提升至少 50%
            var improvement = (baselineTime - optimizedTime) / baselineTime;
            Assert.IsTrue(improvement >= 0.5,
                $"性能提升不足。基线: {baselineTime:F2}ms, 优化: {optimizedTime:F2}ms, 提升: {improvement:P1}");
        }

        [TestMethod]
        public void Compare_MemoryAllocation()
        {
            var baselineMemory = MeasureMemory(() => LoadBaselineFont());
            var optimizedMemory = MeasureMemory(() => LoadOptimizedFont());

            // 验证内存减少至少 60%
            var reduction = (baselineMemory - optimizedMemory) / baselineMemory;
            Assert.IsTrue(reduction >= 0.6,
                $"内存减少不足。基线: {baselineMemory / 1024:F1}KB, 优化: {optimizedMemory / 1024:F1}KB, 减少: {reduction:P1}");
        }

        [TestMethod]
        public void Compare_TableParsingAccuracy()
        {
            // 加载两个版本的实例
            var baselineFont = LoadBaselineFont();
            var optimizedFont = LoadOptimizedFont();

            // 对比关键表数据，确保解析结果一致
            var baselineHead = baselineFont.GetTable(FontFlat.Baseline.Table_head.Tag) as FontFlat.Baseline.Table_head;
            var optimizedHead = optimizedFont.GetTable(FontFlat.Table_head.Tag) as FontFlat.Table_head;

            Assert.AreEqual(baselineHead.unitsPerEm, optimizedHead.unitsPerEm,
                "unitsPerEm 解析结果不一致");

            Assert.AreEqual(baselineHead.created, optimizedHead.created,
                "created 时间戳不一致");
        }

        private FontFlat.Baseline.OTFont LoadBaselineFont()
        {
            var file = new FontFlat.Baseline.OTFile();
            file.open(TestFontPath);
            return file.getFont(0);
        }

        private FontFlat.OTFont LoadOptimizedFont()
        {
            var file = new FontFlat.OTFile();
            file.open(TestFontPath);
            return file.getFont(0);
        }

        private long Measure(Action action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private long MeasureMemory(Action action)
        {
            var before = GC.GetTotalMemory(true);
            action();
            var after = GC.GetTotalMemory(true);
            return after - before;
        }
    }
}
```

#### 添加对比基准测试

```csharp
// Benchmarks/ComparisonBenchmarks.cs
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using FontFlat;                    // 优化版本
using FontFlat.Baseline;            // 原始版本

namespace OTFontFile.Benchmarks.Benchmarks
{
    [Config(typeof(ComparisonConfig))]
    [MemoryDiagnoser]
    public class ComparisonBenchmarks
    {
        private const string SmallFontPath = "BenchmarkResources/SampleFonts/small.ttf";
        private const string MediumFontPath = "BenchmarkResources/SampleFonts/medium.ttf";
        private const string LargeFontPath = "BenchmarkResources/SampleFonts/large.ttf";

        // 文件加载对比
        [Benchmark(Baseline = true)]
        [Arguments(SmallFontPath)]
        [Arguments(MediumFontPath)]
        [Arguments(LargeFontPath)]
        public void Baseline_LoadFile(string fontPath)
        {
            var file = new FontFlat.Baseline.OTFile();
            file.open(fontPath);
            file.close();
        }

        [Benchmark]
        [Arguments(SmallFontPath)]
        [Arguments(MediumFontPath)]
        [Arguments(LargeFontPath)]
        public void Optimized_LoadFile(string fontPath)
        {
            var file = new FontFlat.OTFile();
            file.open(fontPath);
            file.close();
        }

        // 校验和计算对比
        [Benchmark(Baseline = true)]
        public void Baseline_CalculateChecksum()
        {
            var buffer = new FontFlat.Baseline.MBOBuffer(1024);
            buffer.CalcChecksum(0, 1024);
        }

        [Benchmark]
        public void Optimized_CalculateChecksum()
        {
            var buffer = new FontFlat.MBOBuffer(1024);
            buffer.CalcChecksum(0, 1024);
        }

        // 表解析对比
        [Benchmark(Baseline = true)]
        public void Baseline_ParseHeadTable()
        {
            using var file = new FontFlat.Baseline.OTFile();
            file.open(SmallFontPath);
            var font = file.getFont(0);
            var _ = font.GetTable(FontFlat.Baseline.Table_head.Tag);
            file.close();
        }

        [Benchmark]
        public void Optimized_ParseHeadTable()
        {
            using var file = new FontFlat.OTFile();
            file.open(SmallFontPath);
            var font = file.getFont(0);
            var _ = font.GetTable(FontFlat.Table_head.Tag);
            file.close();
        }
    }

    public class ComparisonConfig : ManualConfig
    {
        public ComparisonConfig()
        {
            // 使用 ASCII 表格式，清晰对比
            SummaryStyle = BenchmarkDotNet.Reports.SummaryStyle.Default
                .WithMaxParameterColumnWidth(60)
                .WithTimeUnit(BenchmarkDotNet.Horology.TimeUnit.Millisecond);
        }
    }
}
```

### Phase 4: 运行对比测试

```bash
# 运行对比单元测试
dotnet test OTFontFile.Performance.Tests --filter "FullyQualifiedName~ComparisonTests"

# 运行对比基准测试（显示详细对比）
dotnet run --project OTFontFile.Benchmarks -- -c Release --filter "*Comparison*"

# 生成 HTML 报告（可视化对比）
dotnet run --project OTFontFile.Benchmarks -- -c Release --exporters "html,markdown"
```

---

## 🎨 BenchmarkDotNet 对比输出示例

```
| Method               | Mean      | Error     | StdDev    | Median    | Gen0   | Gen1   | Allocated |
|--------------------- |----------:|----------:|----------:|----------:|-------:|-------:|----------:|
| Baseline_LoadFile    | 5.234 ms  | 0.102 ms  | 0.095 ms  | 5.210 ms  | 10.000 |  2.000 |   84.2 KB |
| Optimized_LoadFile   | 1.876 ms  | 0.045 ms  | 0.042 ms  | 1.870 ms  |  5.000 |  1.000 |   32.1 KB |

对比结果:
- 性能提升: 178.9% (5.234ms → 1.876ms)
- 内存减少: 61.9% (84.2KB → 32.1KB)
```

---

## 🔄 工作流程

### 优化过程中的完整流程

```bash
# 1. 创建 Baseline 项目（优化前的快照）
.\scripts\Create-BaselineProject.ps1

# 2. 添加到解决方案
dotnet sln add OTFontFile.Baseline\OTFontFile.Baseline.csproj

# 3. 更新测试项目引用
# (手动编辑 csproj 文件)

# 4. 创建对比测试
# (手动创建 ComparisonTests.cs 和 ComparisonBenchmarks.cs)

# 5. 开始优化...

# Phase 1: 实现优化
dotnet test OTFontFile.Performance.Tests --filter "FullyQualifiedName~ComparisonTests"

# Phase 2: 验证性能提升
dotnet run --project OTFontFile.Benchmarks -- -c Release --filter "*Comparison*"

# Phase 3: 记录结果
git commit -m "docs(Phase1): 记录优化对比结果

性能对比:
- 文件加载: 5.234ms → 1.876ms (提升 178.9%)
- 内存使用: 84.2KB → 32.1KB (减少 61.9%)
```

---

## ⚡ 其他方案（备选）

### 方案 A: Git 分支切换（简单但繁琐）

```bash
# 1. 在 main 分支建立基线
git checkout main
dotnet run --project OTFontFile.Benchmarks -- -c Release > baseline-main.txt
git commit -m "Baseline commit: Phase 0"

# 2. 切换到优化分支，进行优化
git checkout feature/performance-optimization
# (开发优化...)

# 3. 优化完成，再次测试
dotnet run --project OTFontFile.Benchmarks -- -c Release > baseline-optimized.txt

# 4. 对比结果（手动对比两个文件）
```

**缺点**：
- 无法在同一个基准测试中直接对比
- 需要频繁切换分支
- 环境可能发生变化导致不可比

### 方案 B: 条件编译（复杂但不推荐）

```csharp
// 在源代码中
#if OPTIMIZED
    public class OTFile
    {
        // 使用 Span<T> 和 MemoryMappedFile
    }
#else
    public class OTFile
    {
        // 使用 byte[] 和 FileStream
    }
#endif
```

**缺点**：
- 代码复杂，难以维护
- 编译两个版本不方便
- 容易引入逻辑错误

---

## 📊 性能报告生成建议

### 自动化对比报告脚本

```powershell
# Generate-PerformanceReport.ps1

# 运行基准测试
dotnet run --project OTFontFile.Benchmarks -- -c Release --exporters json > results.json

# 解析并生成 Markdown 报告
$json = Get-Content results.json | ConvertFrom-Json

$report = @"
# Performance Comparison Report

**Date**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Environment**: Windows 11, .NET 10.0

## Results

| Metric | Baseline | Optimized | Improvement |
|--------|----------|-----------|-------------|
| File Loading | $($json.Results[0].Baseline.Mean) | $($json.Results[0].Optimized.Mean) | $([math]::Round(($json.Results[0].Baseline.Mean - $json.Results[0].Optimized.Mean) / $json.Results[0].Baseline.Mean * 100, 1))% |

"@

$report | Out-File "PERFORMANCE_REPORT.md" -Encoding UTF8

Write-Host "✓ 报告已生成: PERFORMANCE_REPORT.md" -ForegroundColor Green
```

---

## ✅ 推荐方案总结

**首选：并行项目结构**（OTFontFile + OTFontFile.Baseline）

优点：
- ✅ 可同时运行对比测试
- ✅ 无需频繁切换
- ✅ 代码清晰，易于维护
- ✅ BenchmarkDotNet 原生支持 Baseline 对比

实施步骤：
1. 复制 OTFontFile → OTFontFile.Baseline
2. 修改命名空间为 `FontFlat.Baseline`
3. 更新测试项目引用两个版本
4. 创建对比测试类
5. 运行对比测试和基准测试

---

## 📝 下一步建议

立即创建 OTFontFile.Baseline 项目，并更新测试配置。这样可以：
- 在优化过程中随时对比性能
- 确保优化不破坏功能正确性
- 生成清晰的可视化对比报告

准备好后，我们可以一起实施这个方案！
