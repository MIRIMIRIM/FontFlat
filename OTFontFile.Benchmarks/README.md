# OTFontFile.Benchmarks

使用 BenchmarkDotNet 的性能基准测试项目，用于测量 OTFontFile 库在优化前后的性能变化，验证性能提升是否符合预期。

## 项目结构

```
OTFontFile.Benchmarks/
├── Benchmarks/                   # 基准测试分类
│   ├── FileLoadingBenchmarks.cs    # 文件加载性能
│   ├── ChecksumBenchmarks.cs       # 校验和计算性能
│   ├── MBOBufferBenchmarks.cs      # 缓冲区操作性能
│   └── TableParsingBenchmarks.cs   # 表解析性能
├── BenchmarkResources/             # 基准测试资源
│   └── SampleFonts/                # 示例字体文件
└── Program.cs                      # 测试运行入口
```

## 性能指标

### 目标性能提升

| 指标 | 基线 (优化前) | 目标 (优化后) | 目标提升 |
|------|--------------|--------------|---------|
| 小字体 (<100KB) 加载 | ~5ms | ~2ms | >= 2.5x |
| 中字体 (100KB-10MB) 加载 | ~500ms | ~150ms | >= 3.3x |
| 大字体 (>10MB) 加载 | ~5000ms | ~1500ms | >= 3.3x |
| 校验和计算 (10KB) | ~0.1ms | ~0.02ms | >= 5x |
| 校验和计算 (1MB) | ~10ms | ~1.5ms | >= 6.7x |
| Unicode 查询 (CJK) | ~0.05ms | ~0.015ms | >= 3.3x |
| 初始内存占用 (10MB 字体) | ~12MB | ~3MB | <= 30% |

## 运行基准测试

### 使用 .NET CLI

```bash
# 编译项目
dotnet build

# 运行所有基准测试
dotnet run --project OTFontFile.Benchmarks

# 运行特定类别的基准测试
dotnet run --project OTFontFile.Benchmarks -- file
dotnet run --project OTFontFile.Benchmarks -- checksum
dotnet run --project OTFontFile.Benchmarks -- buffer

# 使用配置文件
dotnet run --project OTFontFile.Benchmarks -- -c Release
```

### 使用 Visual Studio

1. 将 `OTFontFile.Benchmarks` 设置为启动项目
2. 按 F5 运行

### 使用 PowerShell

```powershell
cd OTFontFile.Benchmarks
dotnet run
```

## 基准测试类别

### 1. FileLoadingBenchmarks.cs

测试文件加载性能：

- ✅ `OpenFontFile` - 打开字体文件
- ✅ `OpenAndCloseFontFile` - 打开并关闭
- ✅ `GetFirstFont` - 获取第一个字体
- ✅ `GetAllFontsFromTTC` - 从字体集合获取所有字体
- ✅ `GetFileLength` - 获取文件长度
- ✅ `FullFontLoadMemoryUsage` - 完整加载的内存使用

**参数**:
- `FontType`: Small, Medium, Large, Collection

### 2. ChecksumBenchmarks.cs

测试校验和计算性能：

- ✅ `CalcChecksum` - 计算校验和
- ✅ `VerifyChecksum` - 验证校验和
- ✅ `CalcMultipleSmallTables` - 计算多个小表
- ✅ `LargeTableChecksum` vs `SmallTableChecksum` - 大小表对比

**参数**:
- `TableSize`: 1024, 4096, 16384, 65536, 262144, 1048576

### 3. MBOBufferBenchmarks.cs

测试 MBOBuffer 操作性能：

**读取操作**:
- `ReadByte`, `ReadShort`, `ReadUshort`, `ReadInt`, `ReadUint`

**写入操作**:
- `WriteByte`, `WriteShort`, `WriteUshort`, `WriteInt`, `WriteUint`

**静态方法**:
- `StaticGetMBOshort`, `StaticGetMBOushort`, `StaticGetMBOint`, `StaticGetMBOuint`

### 4. TableParsingBenchmarks.cs

测试字体表解析性能：

**单个表加载**:
- `LoadHeadTable`, `LoadMaxpTable`, `LoadCmapTable`, `LoadGlyfTable`

**多表加载**:
- `LoadAllRequiredTables` - 加载所有必需表
- `LoadAllTables` - 加载所有表

**表数据访问**:
- `CmapGetEncodingTableEntries` - cmap 编码表条目
- `NameTableGetAllRecords` - name 表所有记录

**属性访问**:
- `HeadTableAllProperties` - head 表所有属性
- `MaxpTableAllProperties` - maxp 表所有属性

**对比测试**:
- `LoadAllTables_MediumFont` vs `LoadAllTables_LargeFont`

## 测试资源

需要在 `BenchmarkResources/SampleFonts/` 目录下放置以下测试字体文件：

- `small*.ttf` - 小型字体文件（<100 KB）
- `medium*.ttf` - 中型字体文件（100 KB - 1 MB）
- `large*.ttf` - 大型字体文件（>1 MB）
- `*.ttc` - 字体集合文件

## 输出报告

Benchmarks 会生成以下格式的报告：

- **CSV**: `*-report.csv` - 表格数据
- **HTML**: `*-report.html` - 交互式 HTML 报告
- **Markdown**: `*-report.md` - Markdown 表格
- **R-Plot**: `*-results.r` - R 脚本用于绘图

报告保存在 `OTFontFile.Benchmarks/bin/[配置]/net10.0/BenchmarkDotNet.Artifacts/results/` 目录。

## 基线建立

在进行优化之前，必须建立性能基线：

```bash
# 1. 切换到主分支
git checkout main

# 2. 运行完整基准测试
cd OTFontFile.Benchmarks
dotnet run -- -c Release > baseline.txt

# 3. 将结果保存为基线
# 将生成的报告复制到安全位置
```

### 基线报告格式

将基准线结果记录在 `PERFORMANCE_OPTIMIZATION_PLAN.md` 中：

```markdown
### 性能基线 (优化前)

| 测试 | 均值 | 标准误 | 中位数 | Gen 0 | Gen 1 | Gen 2 | 分配内存 |
|------|------|-------|-------|-------|-------|-------|---------|
| OpenFontFile_Small | 4.52 ms | 0.12 ms | 4.50 ms | - | - | - | 150 KB |
| CalcChecksum_65536 | 0.65 ms | 0.01 ms | 0.64 ms | - | - | - | 65 KB |
| ...
```

## 优化验证

完成优化后，运行相同的基准测试并对比：

```bash
# 1. 切换到优化分支
git checkout feature/performance-optimization

# 2. 运行基准测试
cd OTFontFile.Benchmarks
dotnet run -- -c Release > optimized.txt

# 3. 对比结果
# 手动对比 baseline.txt 和 optimized.txt
# 或使用 Excel/脚本自动生成对比报告
```

### 性能提升计算

```
提升倍数 = (优化前用时) / (优化后用时)
提升百分比 = ((优化前用时 - 优化后用时) / 优化前用时) * 100%
```

## BenchmarkDotNet 配置

项目使用以下配置：

```csharp
[SimpleJob(warmupCount: 3, iterationCount: 10)]  // 3次热身，10次迭代
[MemoryDiagnoser]                                  // 内存诊断
[ThreadingDiagnoser]                               // 线程诊断
[MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]  // 多格式导出
```

### 自定义配置

可以修改 `Program.cs` 或基准测试属性来自定义配置：

```csharp
// 增加迭代次数以获得更精确的结果
[SimpleJob(warmupCount: 5, iterationCount: 20)]

// 添加硬件计数器诊断（需要管理员权限）
[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.CacheMisses)]

// 自定义编译器标志
[DisassemblyDiagnoser(printSource: true, printAsm: true)]
```

## 注意事项

1. **一致性**: 在相同环境下运行基准测试和基线测试
2. **关闭其他应用**: 避免后台程序影响测试结果
3. **多次运行**: 运行至少3次取平均值以减少随机误差
4. **Release 配置**: 始终使用 Release 模式运行（生产级优化）
5. **预热系统**: 在首次运行前，让系统预热

## 常见问题

### 问题：找不到字体文件

**解决方法**：确保字体文件已复制到输出目录。检查 `.csproj` 配置：

```xml
<ItemGroup>
  <None Update="BenchmarkResources\SampleFonts\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### 问题：结果不稳定

**解决方法**：
1. 增加迭代次数
2. 关闭后台程序
3. 使用节能模式而非高性能模式
4. 多次运行取平均

### 问题：内存诊断不可用

**解决方法**：确保具有管理员权限或在 Windows 上运行。

## 性能分析工具

### 生成性能报告

```bash
# 使用 dotnet-trace 进行追踪
dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5 --output trace.nettrace

# 使用 PerfView 分析
# 下载: https://github.com/microsoft/perfview
# 打开 trace.nettrace 并查看 CPU Usage 和 Memory Events
```

### Visual Studio Profiler

1. 菜单：Debug -> Performance Profiler
2. 选择 "CPU Usage" 或 "Memory Usage"
3. 运行测试
4. 分析热点和内存分配

## 相关文档

- [Optimization Plan](../../PERFORMANCE_OPTIMIZATION_PLAN.md)
- [Test README](../OTFontFile.Performance.Tests/README.md)
- [BenchmarkDotNet 文档](https://benchmarkdotnet.org/articles/guides/home.html)
- [.NET 性能博客](https://devblogs.microsoft.com/dotnet/tag/performance/)

## 下一步

1. **基线建立**: 在主分支运行所有基准测试，建立性能基线
2. **优化实施**: 按照 `PERFORMANCE_OPTIMIZATION_PLAN.md` 中的计划实施优化
3. **验证优化**: 在优化分支运行对比测试，验证性能提升
4. **生成报告**: 创建优化前后对比报告，更新项目文档
