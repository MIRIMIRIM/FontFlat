# OTFontFile 高性能优化计划

## 项目概述
OTFontFile是一个用于解析和写入OpenType字体的.NET库。本项目旨在通过多种现代化技术对其进行性能优化，包括Span<T>、MemoryMappedFile、SIMD加速和延迟加载。同时建立完善的测试体系确保优化后的正确性和性能提升。

---

## 测试体系说明

本项目建立了两个互补的测试项目，分别在**功能正确性**和**性能度量**两个维度保障优化质量。

### 1. OTFontFile.Performance.Tests (MSTest) - 功能验证

**定位**：快速功能测试，确保优化过程中功能正确性

**特点**：
- ✅ 运行速度快：秒级完成
- ✅ 二元结果：通过/失败
- ✅ 持续验证：每次修改后运行
- ✅ 质量门槛：作为提交前的强制检查

**测试内容**：
- 字节序转换正确性（Byte/Short/Int/UInt）
- 校验和计算准确性
- 字体文件加载完整性
- 表解析逻辑正确性
- 边界条件和异常处理

**运行命令**：
```bash
# Debug 模式（快速验证）
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj

# Release 模式（验证优化后性能）
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj -c Release

# 生成代码覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

**何时使用**：
- ✅ 每次代码修改后
- ✅ 每个 Phase 开始前（验证基线）
- ✅ 每个 Phase 完成后（验证功能完整性）
- ✅ 提交代码前（强制检查）
- ✅ CI/CD 流水线中

---

### 2. OTFontFile.Benchmarks (BenchmarkDotNet) - 性能度量

**定位**：精准性能测量，量化优化效果

**特点**：
- 📊 运行慢：分钟级完成（多次热身+迭代）
- 📈 数值结果：均值、标准差、内存分配等
- 🧪 阶段性对比：仅需在关键节点运行
- 🎯 成功标准：评估优化是否达成目标

**测试内容**：
- 文件加载性能（小/中/大字体、字体集合）
- 校验和计算时间（不同表大小）
- MBOBuffer 操作吞吐量（读写性能）
- 表解析开销（单个/多个表）
- 内存使用情况（GC 压力）

**运行命令**：
```bash
# 全部基准测试
dotnet run --project OTFontFile.Benchmarks -- -c Release

# 单一类别测试
dotnet run --project OTFontFile.Benchmarks -- file      # 文件加载
dotnet run --project OTFontFile.Benchmarks -- checksum  # 校验和
dotnet run --project OTFontFile.Benchmarks -- buffer    # 缓冲区操作

# 导出格式
dotnet run --project OTFontFile.Benchmarks -- -c Release --exporters markdown,html
```

**何时使用**：
- ✅ Phase 0：建立性能基线
- ✅ 每个 Phase 完成后：评估优化效果
- ✅ 最终验证：生成对比报告
- ⚠️ 日常开发：不建议每次修改都运行（太慢）

---

### 3. 两项目的协作关系

```
                    ┌─────────────────────────────────────┐
                    │       开发流程                       │
                    └─────────────────────────────────────┘
                                      │
                                      ▼
                    ┌─────────────────┴─────────────────┐
                    │      代码修改                       │
                    └─────────────────┬─────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
        ▼                             ▼                             │
  ┌──────────┐               ┌─────────────────┐                     │
  │  单元测试 │               │   功能验证       │                     │
  │ (秒级)   │──────────────►│   MSTest        │                     │
  └──────────┘               └─────────┬───────┘                     │
        ▼                             │                             │
  通过/失败                        通过？                            │
        │                             │                             │
        │                    ┌────────┴────────┐                    │
        │                    ▼                 ▼                    │
        │             ┌──────────┐        ┌─────────┐               │
        │             │  提交代码 │        │ 修复bug │               │
        │             └────┬─────┘        └────┬────┘               │
        │                  │                   │                    │
        │         ┌────────┘                   └────────┐           │
        │         │   (每个 Phase 完成后)           │           │
        │         ▼                                 ▼           │
        │  ┌──────────────┐                   ┌──────────┐          │
        │  │  基准测试    │                   │  阶段性  │          │
        │  │ (分钟级)     │◄──────────────────│  检查点  │          │
        │  └──────┬───────┘                   └──────┬───┘          │
        │         │                                  │              │
        │         ▼                                  │              │
        │  ┌──────────────┐                         │              │
        │  │ 性能对比      │                         │              │
        │  └──────┬───────┘                         │              │
        │         ▼                                  │              │
        │  达成目标？                                │              │
        │         │                                  │              │
        │    ┌────┴─────┐                           │              │
        │    ▼          ▼                           │              │
        │ 继续下一个   调整优化                    └──────────────┘
        │    Phase      策略                                  │
        └──────────────────────────────────────────────────────┘
```

**关键工作流程**：

1. **开发阶段**：
   ```
   编写/修改代码 → 运行 MSTest (秒级)
                ↓
            通过？ → 是 → 提交代码
                ↓
              否 → 修复 bug → 重新测试
   ```

2. **Phase 完成阶段**：
   ```
   MSTest 全部通过 (功能验证) 
           ↓
   Commit 当前阶段代码 (git commit)
           ↓
   运行 BenchmarkDotNet (性能度量)
           ↓
   对比性能基线
           ↓
   达成目标？ → 是 → 记录日志，准备下一个 Phase
           ↓
        否 → 调整优化策略 → 重新测试 → 回滚或继续
   ```

3. **项目完成阶段**：
   ```
   所有 Phase 完成
           ↓
   全面 MSTest 测试（所有用例）
           ↓
   全面 BenchmarkDotNet 对比
           ↓
   生成优化前后对比报告
           ↓
   Final Commit & Release
   ```

**Git 提交策略示例**：

```bash
# 提交 1: Phase 1 基础设施
git add .
git commit -m "perf(Phase1): 添加 IMemoryBuffer 接口和 MemoryMappedFileBuffer 实现

- 测试: 所有 MSTest 用例通过 (45/45)
- 状态: 功能完整，待性能验证
- 性能: 未运行基准测试"

# 提交 2: 性能验证记录
git add docs/benchmark-phase1.md
git commit -m "docs(Phase1): 添加 Phase 1 性能验证结果

- 文件加载: 提升 120% (5ms → 2.3ms)
- 内存分配: 减少 65% (12MB → 4.2MB)
- 目标: ✅ 达成 (预期 2.5x)"
```

---

---

## 当前架构分析

### 核心组件

#### 1. **OTFile.cs** - 文件操作层
- 当前使用`FileStream`进行文件读取（Lines 43-50, OTFile.cs）
- 方法`ReadPaddedBuffer()`一次性读取整个表到`MBOBuffer`
- 支持单字体和字体集合(TTC)读取
- 提供SafeFileHandle支持

#### 2. **MBOBuffer.cs** - 字节序缓冲区
- Motorola Byte Order (大端序)字节缓冲区
- 使用`byte[] m_buf`存储数据（Lines 20-60, MBOBuffer.cs）
- 手动位操作进行字节序转换: `m_buf[offset]<<24 | m_buf[offset+1]<<16...`
- 提供静态方法进行MBO转换：`GetMBOshort`, `GetMBOushort`, `GetMBOint`, `GetMBOuint`
- 包含校验和计算和缓存机制

#### 3. **OTFont.cs** - 字体对象
- 管理字体表的创建和获取
- 提供缓存机制通过`MemBasedTables`（Dictionary<string, OTTable>）
- 通过`TableManager`获取表
- 支持内存中创建字体

#### 4. **TableManager.cs** - 表管理器
- 管理字体表的缓存
- 延迟加载策略(只在需要时读取表)
- 表别名处理(EBLC/CBLC/bloc等)

#### 5. **OTTable.cs** - 表基类
- 所有字体表的抽象基类
- 包含校验和计算逻辑
- 缓冲区管理
- 提供校验和、长度等方法

#### 6. **各类Table实现**
- 存在大量表实现：Table_cmap(2488行), Table_glyf, Table_head等
- 每个表都有特定的解析逻辑
- 全部继承自OTTable并使用MBOBuffer

### 已存在的优势
- ✅ .NET 10 目标框架
- ✅ AOT兼容支持（`<IsAotCompatible>True</IsAotCompatible>`）
- ✅ 支持Nullable引用类型
- ✅ 项目结构清晰（src/分离）

### 性能瓶颈识别

1. **内存分配频繁**
   - 每个表都创建新的`byte[] m_buf`
   - `ReadPaddedBuffer()`每次都分配新数组（Lines 254-260, OTFile.cs）
   - 4字节对齐的填充字节额外分配内存（Lines 24-30, MBOBuffer.cs）
   - 无对象池复用

2. **数据访问模式低效**
   - 手动位操作而非系统优化的`BinaryPrimitives`
   - 频繁的小规模数据类型转换
   - 无零拷贝机制

3. **I/O效率低**
   - `FileStream`同步读取加载所有表
   - ReadPaddedBuffer直接创建新MBOBuffer对象
   - 无内存映射支持（MemoryMappedFile）

4. **计算密集型操作**
   - 表校验和计算逐字节进行（需查看具体实现）
   - cmap查找可能使用线性搜索
   - 无SIMD加速

---

## 优化策略

### Phase 1: Span<T> 和 Memory<T> 重构 (高性能零拷贝)

#### 1.1 MBOBuffer 改造
**目标**: 替换`byte[]`为`Span<byte>`/`Memory<byte>`,减少内存分配

**具体实现**:
```csharp
public ref struct MBOBufferReader(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetUshort(uint offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice((int)offset, 2));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetUint(uint offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice((int)offset, 4));
    }
    
    // 其他读取方法...
}
```

**收益**:
- 零分配读取操作
- 避免临时数组创建
- 利用`BinaryPrimitives`进行高效字节序转换

#### 1.2 ReadOnlySequence<T> 支持分段数据
**目标**: 支持处理不连续的内存区域

**使用场景**: 处理TTC(字体集合)中的多个字体

#### 1.3 Span<T> 在表解析中的应用
**目标**: 在所有`Table_xxx.cs`中使用Span操作

**示例** - Table_cmap优化:
```csharp
public class Table_cmap : OTTable
{
    private Span<byte> _buffer;
    
    public EncodingTableEntry? GetEncodingTableEntry(uint i)
    {
        uint offset = 4 + i * 8;
        if (offset + 8 > _buffer.Length) return null;
        
        var entrySpan = _buffer.Slice((int)offset, 8);
        return new EncodingTableEntry
        {
            platformID = BinaryPrimitives.ReadUInt16BigEndian(entrySpan),
            encodingID = BinaryPrimitives.ReadUInt16BigEndian(entrySpan.Slice(2, 2)),
            offset = BinaryPrimitives.ReadUInt32BigEndian(entrySpan.Slice(4, 4))
        };
    }
}
```

---

### Phase 2: 现代化 I/O - System.IO.Pipelines & MemoryMappedFile

#### 2.1 System.IO.Pipelines 集成
**目标**: 使用Pipe进行高效的流式I/O

**优势**:
- 高效内存管理
- 自动buffer管理
- 异步支持

**实现**:
```csharp
public class OTFile : IDisposable
{
    private PipeReader? _pipeReader;
    
    public async ValueTask<MBOBuffer?> ReadPaddedBufferAsync(uint filepos, uint length)
    {
        var buffer = new byte[length];
        await _stream!.ReadAsync(buffer, (int)filepos, (int)length);
        return new MBOBuffer(filepos, buffer);
    }
}
```

#### 2.2 MemoryMappedFile 支持大型字体
**目标**: 对于大字体使用内存映射文件

**实现**:
```csharp
public class OTFile : IDisposable
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    
    public Span<byte> GetMappedSpan(long offset, int length)
    {
        unsafe
        {
            byte* ptr = null;
            _accessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            return new Span<byte>(ptr + offset, length);
        }
    }
}
```

#### 2.3 FileOptions 优化
**目标**: 使用SequentialScan和异步标志

**实现**:
```csharp
private FileStream OpenFileStream(string path)
{
    return new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        options: FileOptions.SequentialScan | FileOptions.Asynchronous
    );
}
```

---

### Phase 3: SIMD 优化 (SSE/AVX)

#### 3.1 校验和计算优化
**当前**: 逐字节累加计算checkSum

**优化后**: 使用SIMD并行计算
```csharp
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

public static uint CalcChecksumSIMD(ReadOnlySpan<byte> data)
{
    uint sum = 0;
    int i = 0;
    
    // 使用AVX2一次处理32字节
    if (Avx2.IsSupported)
    {
        Vector256<uint> accumulator = Vector256<uint>.Zero;
        int vectorLength = (data.Length / 4) * 4;
        
        while (i + 32 <= vectorLength)
        {
            // 批量读取8个uint32
            var vec = Avx2.LoadVector256(data.Slice(i, 32));
            accumulator = Avx2.Add(accumulator, vec);
            i += 32;
        }
        
        // 归约求和
        var sums = Sse2.Add(
            Sse2.CastAsUint128(Avx2.ExtractVector128(accumulator, 0)),
            Sse2.CastAsUint128(Avx2.ExtractVector128(accumulator, 1))
        );
        
        sum += sums.GetElement(0) + sums.GetElement(1) + 
               sums.GetElement(2) + sums.GetElement(3);
    }
    
    // 处理剩余字节
    for (; i + 4 <= data.Length; i += 4)
    {
        sum += BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4));
    }
    
    return sum;
}
```

**收益**: 4-8倍加速(取决于CPU架构)

#### 3.2 CMAP 表查找优化
**目标**: 使用SIMD加速Unicode到Glyph的映射查找

**Format 4 (分段查找)优化**:
```csharp
public ushort Format4LookupSIMD(ushort charCode)
{
    // SIMD加速的二分查找
    if (Sse2.IsSupported)
    {
        var keys = new Span<ushort>(segCountX2 / 2);
        // 使用SIMD指令比较多个key
    }
    
    // fallback到常规查找
}
```

#### 3.3 表头快速解析
**目标**: 使用SIMD批量读取和验证表头

**实现**: 批量比对多个表tag

---

### Phase 4: 字体表延迟加载和智能缓存

#### 4.1 智能预取
**目标**: 基于访问模式预取常用表

**策略**:
- 记录表访问频率
- 热表预加载优先级高
- 常用表(name, head, cmap, hhea等)优先加载

**实现**:
```csharp
public class SmartTableManager : TableManager
{
    private readonly Dictionary<OTTag, int> _accessFrequency = new();
    private readonly HashSet<OTTag> _prefetched = new();
    
    public OTTable? GetTableWithPrefetch(DirectoryEntry de)
    {
        var table = GetTableFromCache(de);
        if (table != null) return table;
        
        // 记录访问
        _accessFrequency[de.tag] = _accessFrequency.GetValueOrDefault(de.tag, 0) + 1;
        
        // 异步预取相关表
        if (_accessFrequency[de.tag] > 1)
        {
            _ = Task.Run(() => PrefetchRelatedTables(de.tag));
        }
        
        return LoadTable(de);
    }
    
    private void PrefetchRelatedTables(OTTag tag)
    {
        // cmap -> 预取 glyf, loca, hmtx
        // name -> 预取 head, OS2
        // ...
    }
}
```

#### 4.2 表级对象池
**目标**: 使用ArrayPool减少GC压力

**实现**:
```csharp
public static class BufferPool
{
    private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Create();
    
    public static PooledBuffer Rent(int size)
    {
        return new PooledBuffer(_pool.Rent(size), size);
    }
    
    public struct PooledBuffer : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly int _length;
        private bool _disposed;
        
        public PooledBuffer(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
            _disposed = false;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Return(_buffer);
                _disposed = true;
            }
        }
    }
}
```

#### 4.3 懒加载表内容
**目标**: 只加载表结构,内容按需加载

**实现**:
```csharp
public abstract class LazyTable : OTTable
{
    protected bool _contentLoaded;
    protected DirectoryEntry _directoryEntry;
    
    protected async ValueTask EnsureContentLoadedAsync()
    {
        if (!_contentLoaded)
        {
            // 只加载需要的部分
            _contentLoaded = true;
        }
    }
}
```

---

### Phase 5: 多线程并发优化

#### 5.1 并行表加载
**目标**: 并行加载独立的表

**实现**:
```csharp
public async Task<Dictionary<OTTag, OTTable>> LoadTablesAsync(DirectoryEntry[] entries)
{
    var tasks = entries.Select(async entry =>
    {
        var table = await LoadTableAsync(entry);
        return (entry.tag, table);
    });
    
    return (await Task.WhenAll(tasks))
        .ToDictionary(x => x.tag, x => x.table);
}
```

#### 5.2 并行处理字体集合
**目标**: 并行处理TTC中的多个字体

**实现**:
```csharp
public async Task<OTFont[]> LoadAllFontsAsync(OTFile file)
{
    var offsets = file.GetTTCHeader()!.DirectoryOffsets;
    var tasks = offsets.Select((offset, i) => 
        OTFont.ReadFontAsync(file, (uint)i, offset)
    );
    
    return await Task.WhenAll(tasks);
}
```

---

### Phase 6: 其他优化

#### 6.1 使用 BinaryPrimitives 替代手动位移
**性能提升**: JIT优化,更好的CPU指令利用

**示例**:
```csharp
// 旧
public uint GetUint(uint offset)
{
    return (uint)(m_buf[offset]<<24 | m_buf[offset+1]<<16 | m_buf[offset+2]<<8 | m_buf[offset+3]);
}

// 新
public uint GetUint(uint offset)
{
    return BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice((int)offset, 4));
}
```

#### 6.2 使用 ref struct 避免堆分配
**目标**: 短生命周期的对象使用ref struct

**实现**:
```csharp
public ref struct TableReader
{
    private readonly Span<byte> _buffer;
    
    public TableReader(Span<byte> buffer) => _buffer = buffer;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUshort(ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(offset, 2));
        offset += 2;
        return value;
    }
}
```

#### 6.3 内联关键方法
**目标**: 使用`[MethodImpl(MethodImplOptions.AggressiveInlining)]`

**适用场景**:
- 频繁调用的数据读取方法
- 简单的计算方法

#### 6.4 使用 Span 进行字符串比较
**目标**: 避免字符串分配

**实现**:
```csharp
public static bool TagEquals(Span<byte> buffer, ReadOnlySpan<byte> tag)
{
    return buffer.Slice(0, 4).SequenceEqual(tag);
}

// 使用
if (TagEquals(buffer, "glyf"u8)) { ... }
```

---

## 兼容性保证

### API 兼容性
- ✅ 保持公共API不变
- ✅ 所有现有方法继续工作
- ✅ 新API作为可选的高级接口

### 功能正确性
- 所有优化保证语义等价
- 校验和计算结果一致
- 数据解析结果一致

### 性能回归测试
- 建立完整的单元测试
- 建立性能基准测试
- 确保优化前后的功能一致性

---

## 优化预期收益

### 内存使用
- **目标**: 减少40-60%的内存分配
- **实现**: Span<T> + ArrayPool + 对象池

### 解析速度
- **目标**: 提升2-5倍
- **实现**: SIMD + BinaryPrimitives + 内联优化

### I/O 性能
- **目标**: 提升1.5-2倍
- **实现**: Pipelines + MemoryMappedFile + 异步I/O

### 并发性能
- **目标**: 线性扩展(基于CPU核心数)
- **实现**: 并行加载 + 无锁数据结构

---

## 风险和挑战

### 1. 兼容性风险
- 大量代码改动可能引入bug
- **缓解**: 完善的测试覆盖

### 2. 平台差异
- SIMD在不同CPU架构上可用性不同
- **缓解**: 运行时检测 + fallback实现

### 3. 复杂度增加
- 优化后代码更复杂,维护成本增加
- **缓解**: 良好的代码结构和文档

### 4. 向后兼容
- 需要保持旧API兼容
- **缓解**: 清晰的API版本管理

---

## 实施计划

### 里程碑
1. **Week 1-2**: Phase 1 - Span<T>重构
2. **Week 3**: Phase 2 - I/O优化
3. **Week 4**: Phase 3 - SIMD优化
4. **Week 5**: Phase 4-6 - 其他优化
5. **Week 6**: 测试和调优

### 优先级
1. **P0**: Span<T>重构, BinaryPrimitives替换
2. **P1**: SIMD优化关键路径
3. **P2**: I/O异步化
4. **P3**: 智能缓存

---

## Git 工作流程与提交策略

### 核心原则

1. **每次修改必须通过 MSTest**：确保功能正确性
2. **每个 Phase 完成后必须运行基准测试**：量化优化效果
3. **阶段性提交**：每个 Phase 完成后进行一次 commit
4. **性能验证独立提交**：基准测试结果单独记录
5. **清晰的提交信息**：使用规范的 commit message 格式

### Commit Message 格式

```bash
<type>(<scope>): <subject>

<body>

<footer>
```

**Type 类型**：
- `feat`: 新功能
- `perf`: 性能优化
- `fix`: Bug 修复
- `refactor`: 代码重构（不改变功能）
- `test`: 添加/修改测试
- `docs`: 文档更新
- `chore`: 构建/工具链更新

**Scope 范围**：
- `Phase1` - Phase6: 优化阶段
- `test`: 测试相关
- `docs`: 文档相关

### 完整工作流程

#### 日常开发循环

```bash
# 1. 创建功能分支
git checkout -b perf/phase1-mbobuffer-refactor

# 2. 编写代码/修改代码
# (编辑文件...)

# 3. 运行 MSTest 验证功能正确性
dotnet test OTFontFile.Performance.Tests -c Debug

# 4. 如果测试失败，修复并重新测试
# (重复步骤 2-3 直到测试全部通过)

# 5. 提交代码
git add .
git commit -m "perf(Phase1): 实现基于 Span<T> 的 MBOBufferReader

功能:
- 新增 MBOBufferReader ref struct
- 移除 byte[] 依赖，使用 Span<byte>
- 添加 BinaryPrimitives 端序转换

测试:
- ✅ 所有 MSTest 用例通过 (45/45)
- ✅ BufferTests 字节序转换正确
- ✅ FileParsingTests 文件加载正常

状态: 功能完整，基准测试待运行"
```

#### Phase 完成验证流程

```bash
# 1. 确保 MSTest 全部通过
dotnet test OTFontFile.Performance.Tests -c Release

# 2. 提交 Phase 功能代码
git add .
git commit -m "perf(Phase1): 完成 Span<T> 和 Memory<T> 重构

功能:
- MBOBuffer 全面使用 Span<T>
- 实现 MemoryMappedFileBuffer
- 更新所有表解析逻辑使用新 API

测试:
- ✅ 全部 MSTest 用例通过 (52/52)
- ✅ 所有表解析测试通过
- ✅ 内存泄漏检测通过

性能: 待基准测试验证"

# 3. 运行基准测试（可能需要几分钟）
cd OTFontFile.Benchmarks
dotnet run -- -c Release

# 4. 保存基准测试结果
# BenchmarkDotNet 会生成报告，保存到文档目录
# 例如: docs/benchmark-results/phase1-benchmark.md

# 5. 提交性能验证结果
cd ..
git add docs/benchmark-results/phase1-benchmark.md
git commit -m "docs(Phase1): 添加 Phase 1 性能验证报告

性能对比:

文件加载:
- 小字体 (100KB): 5.2ms → 2.1ms (提升 148%)
- 中字体 (1MB):    52ms  → 18ms  (提升 189%)
- 大字体 (10MB):   520ms → 185ms (提升 181%)

内存分配:
- 小字体: 1.2MB → 0.4MB (减少 67%)
- 中字体: 12MB  → 4.0MB (减少 67%)
- 大字体: 120MB → 40MB  (减少 67%)

目标达成:
- ✅ 文件加载速度: 预期 2.5x, 实际 1.8x (72%)
- ✅ 内存分配减少: 预期 60%,  实际 67%  (112%)
- ⚠️  注意: 文件加载未达到目标，需要进一步优化
"
```

#### 性能目标调整流程

```bash
# 如果性能未达预期，分析原因并调整策略

# 1. 分析基准测试结果
# 查看 BenchmarkDotNet 报告中的内存分配热区、GC 统计等

# 2. 决策: 继续优化 vs 接受现状
# 如果差距 < 10%，可能接受
# 如果差距 > 20%，需要继续优化

# 3. 继续优化（在当前 Phase 中）
git checkout -b perf/phase1-memory-pool-optimization

# (编写代码优化...)
dotnet test OTFontFile.Performance.Tests
git add .
git commit -m "perf(Phase1): 添加对象池减少内存分配

测试: 所有 MSTest 通过"

# 4. 再次运行基准测试
cd OTFontFile.Benchmarks
dotnet run -- -c Release

# 5. 如果目标达成，提交最终结果
git add docs/benchmark-results/phase1-final-benchmark.md
git commit -m "docs(Phase1): 最终性能验证（对象池优化后）

文件加载:
- 小字体: 5.2ms → 1.8ms (提升 189%) ✅

内存分配:
- 小字体: 1.2MB → 0.3MB (减少 75%) ✅

目标达成: 全部达成 ✅

下次优化: Phase 2 - MemoryMappedFile 集成"
```

### Git 分支策略

#### 主要分支

- `main` / `master`: 生产分支，保持稳定
- `feature/performance-optimization`: 功能分支（当前分支）
- `develop` (可选): 开发集成分支

#### 临时分支

```
feature/performance-optimization   (长期分支，整个优化期间)
├── perf/phase1-mbobuffer-refactor       (Phase 1 开发)
├── perf/phase1-memory-pool-opt          (Phase 1 进一步优化)
├── perf/phase2-memorymappedfile         (Phase 2 开发)
├── perf/phase3-simd-checksum            (Phase 3 开发)
└── ...
```

#### 分支操作示例

```bash
# 开始 Phase 2
git checkout feature/performance-optimization
git checkout -b perf/phase2-memorymappedfile

# 开发中... (多次小提交)
git commit -m "perf(Phase2): 实现 IMemoryBuffer 接口"
git commit -m "perf(Phase2): 实现 MemoryMappedFileBuffer"
git commit -m "perf(Phase2): 集成到 OTFile"

# Phase 2 功能完成，验证测试
dotnet test OTFontFile.Performance.Tests
git add .
git commit -m "perf(Phase2): 完成 MemoryMappedFile 集成

测试: ✅ 所有 MSTest 通过 (55/55)

基准测试: 待运行"

# 运行基准测试，提交结果
cd OTFontFile.Benchmarks
dotnet run -- -c Release
git add ...
git commit -m "docs(Phase2): Phase 2 性能验证

性能: ✅ 文件加载提升 2.3x"

# 合并回主优化分支
git checkout feature/performance-optimization
git merge perf/phase2-memorymappedfile --no-ff
git branch -d perf/phase2-memorymappedfile
```

### CI/CD 集成（推荐）

如果项目有 CI/CD 流水线，可以考虑添加以下检查：

```yaml
# .github/workflows/performance-tests.yml
name: Performance Tests

on:
  pull_request:
    branches: [feature/performance-optimization]
  push:
    branches: [feature/performance-optimization]

jobs:
  mstest:
    name: MSTest Functionality
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - name: Restore dependencies
        run: dotnet restore FontFlat.slnx
      - name: Build
        run: dotnet build FontFlat.slnx --configuration Release
      - name: Run MSTest
        run: dotnet test OTFontFile.Performance.Tests --configuration Release --no-build

  # 注意: 基准测试由于运行时间较长，不建议在每次 PR 时运行
  # 可以手动触发或在合并到主干时运行
  benchmark-manual:
    name: BenchmarkDotNet (Manual)
    runs-on: windows-latest
    if: github.event_name == 'workflow_dispatch'
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - name: Run BenchmarkDotNet
        run: dotnet run --project OTFontFile.Benchmarks --configuration Release
```

### 回滚策略

如果优化导致严重问题：

```bash
# 1. 回退到稳定状态
git log --oneline  # 查看提交历史
git revert <commit-hash>  # 回滚特定提交

# 2. 如果需要完全回退到优化前
git checkout -b backup-current-state
git checkout main

# 3. 分析失败原因，修正策略
# (查看日志、测试结果...)
```

### 最佳实践总结

| 操作 | 命令 | 备注 |
|------|------|------|
| 提交代码前必须 | `dotnet test OTFontFile.Performance.Tests` | 功能验证 |
| Phase 完成时 | `dotnet test` + `git commit` | 功能+提交 |
| 性能验证 | `dotnet run --project OTFontFile.Benchmarks` | 基准测试 |
| 记录基准 | `git add docs` + `git commit` | 独立提交 |
| 分支命名 | `perf/PhaseN-description` | 清晰规范 |
| 提交信息 | `<type>(<scope>): <subject>` | 遵循格式 |

---

## 测试策略

## 参考资源

- [.NET Performance Tips](https://docs.microsoft.com/en-us/dotnet/framework/performance/performance-tips)
- [System.Runtime.Intrinsics](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics)
- [System.IO.Pipelines](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines)
- [Span<T> Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/memory-and-spans/span-t)

---

## 附录: 主要目标

### 关键性能指标(KPI)
1. 字体加载时间减少 **50%+**
2. 内存分配减少 **40%+**
3. 确保所有现有单元测试通过
4. 建立完整的性能基准基线

---

## 测试基础设施

### 1. MSTest 项目 (OTFontFile.Performance.Tests)

**目的**: 验证功能正确性，确保优化前后没有功能回归

**已实现**:
- ✅ `BufferTests.cs` - MBOBuffer 功能测试
- ✅ `FileParsingTests.cs` - 文件解析测试
- ✅ `TableTests.cs` - 表解析测试框架

**测试框架**:
```xml
- MSTest.TestFramework 3.7.0
- MSTest.TestAdapter 3.7.0
- coverlet.collector (代码覆盖率)
```

**运行测试**:
```bash
# 运行所有测试
dotnet test

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~BufferTests"

# 运行代码覆盖率
dotnet test --collect:"XPlat Code Coverage"
```

**测试资源需求**:
- `TestResources/SampleFonts/` 目录需要包含测试字体文件
  - small.ttf - 小型字体 (<100KB)
  - medium.ttf - 中型字体 (100KB - 1MB)
  - large.ttf - 大型字体 (>1MB)
  - collection.ttc - 字体集合 (可选)

### 2. BenchmarkDotNet 项目 (OTFontFile.Benchmarks)

**目的**: 建立性能基线，测量优化效果

**已实现**:
- ✅ `FileLoadingBenchmarks.cs` - 文件加载性能
- ✅ `ChecksumBenchmarks.cs` - 校验和计算性能
- ✅ `MBOBufferBenchmarks.cs` - 缓冲区操作性能
- ✅ `TableParsingBenchmarks.cs` - 表解析性能

**性能工具**:
```xml
- BenchmarkDotNet 0.14.0
- BenchmarkDotNet.Diagnostics.Windows (CPU/内存诊断)
```

**基准测试配置**:
```csharp
[SimpleJob(warmupCount: 3, iterationCount: 10)]  // 3次热身，10次迭代
[MemoryDiagnoser]                                  // 内存分配诊断
[ThreadingDiagnoser]                               // 线程诊断
[MarkdownExporter, AsciiDocExporter, HtmlExporter] // 多格式报告
```

**运行基准**:
```bash
# 运行所有基准测试
dotnet run --project OTFontFile.Benchmarks

# 运行特定类别
dotnet run --project OTFontFile.Benchmarks -- file      # 文件加载
dotnet run --project OTFontFile.Benchmarks -- checksum  # 校验和
dotnet run --project OTFontFile.Benchmarks -- buffer    # 缓冲区操作

# Release 模式（生产级优化）
dotnet run --project OTFontFile.Benchmarks -- -c Release
```

### 3. 测试计划

#### Phase 0: 基线建立 (开始优化前)

```bash
# 1. 确保所有单元测试通过
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj

# 2. 准备测试字体文件
# 将测试字体复制到：
#   OTFontFile.Performance.Tests/TestResources/SampleFonts/
#   OTFontFile.Benchmarks/BenchmarkResources/SampleFonts/

# 3. 运行基准测试建立基线
cd OTFontFile.Benchmarks
dotnet run -- -c Release > ../benchmark-baseline.txt

# 4. 保存基准报告
# 将生成的报告保存在 docs/benchmark-baseline/ 目录
```

**基线记录模板**:

```markdown
### 性能基线 v1.0 (2025-12-23)

**测试环境**:
- OS: Windows 11
- CPU: Intel Core i7-12700K
- RAM: 32GB
- .NET: 10.0.0

**文件加载基线**:

| 测试 | 均值 | 标准误 | 中位数 | Gen 0 | Gen 1 | Gen 2 | 分配内存 |
|------|------|-------|-------|-------|-------|-------|---------|
| OpenFontFile_Small | ? ms | ? ms | ? ms | ? | ? | ? | ? KB |
| OpenFontFile_Medium | ? ms | ? ms | ? ms | ? | ? | ? | ? KB |
| OpenFontFile_Large | ? ms | ? ms | ? ms | ? | ? | ? | ? KB |

**校验和计算基线**:

| 表大小 | 均值 | 标准误 | 中位数 | 提升倍数 |
|--------|------|-------|-------|---------|
| 1KB | ? us | ? us | ? us | - |
| 4KB | ? us | ? us | ? us | - |
| 64KB | ? us | ? us | ? us | - |
| 1MB | ? ms | ? ms | ? ms | - |

**MBOBuffer 操作基线**:

| 操作 | 均值 | 标准误 | 分配内存 |
|------|------|-------|---------|
| ReadByte_Sequential | ? ns | ? ns | 0 B |
| ReadUint_Sequential | ? ns | ? ns | 0 B |
| WriteUint_Sequential | ? ns | ? ns | 120 KB |
```

#### Phase 1-5: 每个优化阶段后

1. 运行单元测试确保功能正确
2. 运行基准测试并对比基线
3. 记录性能变化
4. 如果未达目标，调整优化策略

#### Phase 6: 最终验证

1. 运行完整的测试套件
2. 生成优化前后对比报告
3. 验证所有性能目标达成

---

## 项目文件结构

### 当前结构
```
FontFlat/
├── OTFontFile/                      # 主要库
│   └── src/                         # 源代码
├── OTFontFile.Performance.Tests/    # MSTest 单元测试 ⭐ 新增
│   ├── UnitTests/                   # 单元测试类
│   └── TestResources/               # 测试资源
│       └── SampleFonts/             # 测试字体 ⭐ 需要添加
├── OTFontFile.Benchmarks/           # BenchmarkDotNet 性能基准 ⭐ 新增
│   ├── Benchmarks/                  # 基准测试类
│   └── BenchmarkResources/          # 基准测试资源
│       └── SampleFonts/             # 基准字体 ⭐ 需要添加
└── FontFlat.slnx                    # 解决方案 ✅ 已更新
```

### 需要添加的测试字体

请将以下测试字体文件放置在相应的 `SampleFonts/` 目录中：

1. **ASCII 字体** (~30-50 KB)
   - 用途: 小字体基准测试
   - 示例: `ascii.ttf` 或 `small.ttf`

2. **CJK 字体** (~500KB - 2MB)
   - 用途: 中型字体基准测试，测试 CMAP 查询
   - 示例: `cjk.ttf` 或 `medium.ttf`

3. **Emoji/彩色字体** (~5-15MB)
   - 用途: 大型字体基准测试，测试内存使用
   - 示例: `emoji.ttf` 或 `large.ttf`

4. **字体集合** (~10-50MB)
   - 用途: TTC 格式测试
   - 示例: `collection.ttc`

**获取测试字体的方法**:
- 使用开源字体（如 Noto 系列字体）
- 从系统字体目录复制
- 自行生成测试字体

---

## 详细优化实施步骤

### Step 1: 准备工作

```bash
# 1. 确保在正确的分支
git branch  # 应该是 feature/performance-optimization

# 2. 恢复解决方案文件更改
git add FontFlat.slnx
git commit -m "Add test and benchmark projects to solution"

# 3. 准备测试字体
# 将测试字体复制到两个 SampleFonts 目录

# 4. 运行基线测试
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj
dotnet run --project OTFontFile.Benchmarks -- -c Release

# 5. 记录基线结果
# 将结果保存到 PERFORMANCE_OPTIMIZATION_PLAN.md 附录
```

### Step 2: 实施 Span<T> 优化 (Week 1-2)

**目标**: 使用 `Span<T>` 和 `Memory<T>` 替代数组拷贝

**实施任务**:

1. 创建新缓冲区抽象
   - `src/BufferSpan/IMemoryBuffer.cs` - 缓冲区接口
   - `src/BufferSpan/MemoryMappedFileBuffer.cs` - 内存映射实现
   - `src/BufferSpan/ArrayBackedBuffer.cs` - 数组回退实现
   - `src/BufferSpan/SpanReader.cs` - Span 读取器

2. 重构 MBOBuffer
   - 添加字段: `private IMemoryBuffer? _backingBuffer`
   - 添加属性: `public ReadOnlySpan<byte> AsSpan()`
   - 保持向后兼容: `GetBuffer()` 返回 `byte[]`

3. 更新读取方法（可选，不影响兼容性）
   ```csharp
   // 新增高性能方法
   public ReadOnlySpan<byte> GetSpan(int offset, int length)
   {
       return _backingBuffer?.AsSpan(offset, length)
              ?? new ReadOnlySpan<byte>(_buf, offset, length);
   }
   ```

4. 更新 OTFile
   ```csharp
   // 添加可选的内存映射支持
   private MemoryMappedFileBuffer? _mmfBuffer;

   public bool OpenWithMemoryMapping(string path)
   {
       // 使用内存映射
       _mmfBuffer = new MemoryMappedFileBuffer(path);
       return OpenInternal(_mmfBuffer);
   }
   ```

**测试验证**:
```bash
# 运行完整测试套件
dotnet test

# 运行基准测试
dotnet run --project OTFontFile.Benchmarks -- -c Release

# 验证结果
- 内存分配是否减少？
- 加载时间是否改善？
- 所有单元测试是否通过？
```

### Step 3: 实施 SIMD 优化 (Week 3)

**目标**: 使用 SIMD 加速校验和计算

**实施任务**:

1. 创建 SIMD 工具类
   - `src/BufferSpan/SimdHelper.cs` - SIMD 校验和计算
   - 运行时检测: `Avx2.IsSupported`, `Sse2.IsSupported`
   - Fallback 到标量实现

2. 集成到 MBOBuffer
   ```csharp
   public uint CalcChecksum()
   {
       if (m_bValidChecksumAvailable)
           return m_cachedChecksum;

       if (SimdHelper.IsAvailable)
       {
           m_cachedChecksum = SimdHelper.CalcChecksumSIMD(new ReadOnlySpan<byte>(m_buf));
       }
       else
       {
           // 原有实现
           m_cachedChecksum = CalcChecksumScalar();
       }

       m_bValidChecksumAvailable = true;
       return m_cachedChecksum;
   }
   ```

3. (可选) 优化 CMAP Format 4 查找
   - 使用 SIMD 加速二分查找
   - 优化 CJK 字符集合映射查询

**测试验证**:
```bash
# 运行校验和基准测试
dotnet run --project OTFontFile.Benchmarks -- checksum

# 验证结果
- 校验和计算是否提速 5-7x？
- SIMD 降级路径是否工作？
- 结果是否与标量实现一致？
```

### Step 4: 实施延迟加载 (Week 4-5)

**目标**: 按需加载表，减少内存占用

**实施任务**:

1. 创建 LazyTable 包装器
   - `src/Lazy/LazyTable.cs` - 延迟加载表包装器
   - `src/Lazy/LoadingStrategy.cs` - 加载策略枚举

2. 重构 OTFont
   ```csharp
   // 替换 MemBasedTables
   private Dictionary<string, LazyTable<OTTable>> _lazyTables;

   // 新增策略属性
   public LoadingStrategy LoadingStrategy { get; set; }
       = LoadingStrategy.MetadataFirst;

   // 更新 GetTable 方法
   public T? GetTable<T>(string tag) where T : OTTable, new()
   {
       if (_lazyTables.TryGetValue(tag, out var lazyTable))
       {
           return lazyTable.Value as T;
       }
       return null;
   }
   ```

3. 添加预加载 API
   ```csharp
   public void PreloadTables(params string[] tags)
   {
       foreach (var tag in tags)
       {
           GetTable<OTTable>(tag);  // 触发加载
       }
   }
   ```

4. 支持向后兼容
   ```csharp
   // 保持旧 API
   public OTFile(string path) : this()
   {
       OpenWithStrategy(path, LoadingStrategy.Lazy);
   }

   // 新 API
   public static OTFile FromFile(string path, LoadingStrategy strategy)
   {
       var file = new OTFile { LoadingStrategy = strategy };
       file.open(path);
       return file;
   }
   ```

**测试验证**:
```bash
# 运行完整测试套件
dotnet test

# 运行内存基准测试
dotnet run --project OTFontFile.Benchmarks -- memory

# 验证结果
- 内存占用是否减少 70-90%？
- 延迟加载是否工作？
- 多线程并发访问是否安全？
```

### Step 5: 数据结构优化 (Week 6)

**目标**: 优化 Unicode 查询性能

**实施任务**:

1. 优化 CMAP Format 4
   ```csharp
   // Table_cmap.cs
   public ushort GetGlyphIndexBinarySearch(ushort charCode)
   {
       // 实现二分查找替代线性查找
       int left = 0, right = endCodes.Length - 1;
       while (left <= right)
       {
           int mid = left + (right - left) / 2;
           if (charCode < startCodes[mid])
               right = mid - 1;
           else if (charCode > endCodes[mid])
               left = mid + 1;
           else
               // 找到范围，返回 glyphIndex
               return CalculateGlyphIndex(mid, charCode);
       }
       return 0;  // .notdef
   }
   ```

2. 缓存热门字符映射
   ```csharp
   // 添加简单的 LRU 缓存
   private readonly Dictionary<ushort, ushort> _glyphCache
       = new Dictionary<ushort, ushort>(capacity: 256);

   private const int MaxCacheSize = 256;

   public ushort GetGlyphIndexWithCache(ushort charCode)
   {
       if (_glyphCache.TryGetValue(charCode, out var cachedGlyph))
           return cachedGlyph;

       var glyph = GetGlyphIndex(charCode);

       // LRU
       if (_glyphCache.Count >= MaxCacheSize)
       {
           // 移除最少使用的条目...
       }
       _glyphCache[charCode] = glyph;

       return glyph;
   }
   ```

**测试验证**:
```bash
# 运行 CMAP 查询基准测试
dotnet run --project OTFontFile.Benchmarks -- cmap

# 验证结果
- CJK 字符查询是否提速 3-5x？
- 缓存是否正确工作？
- 查找结果是否与之前一致？
```

### Step 6: 最终测试和文档 (Week 7)

**任务清单**:

1. **功能测试**
   ```bash
   # 运行所有单元测试
   dotnet test --collect:"XPlat Code Coverage"

   # 确保覆盖率 >= 80%
   ```

2. **性能测试**
   ```bash
   # 运行所有基准测试
   dotnet run --project OTFontFile.Benchmarks -- -c Release

   # 生成对比报告
   # 对比 baseline 和 optimized 结果
   ```

3. **代码审查**
   - 代码风格和可读性
   - 文档完整性
   - API 设计合理性

4. **更新文档**
   - API 文档 (XML 注释)
   - README.md
   - 迁移指南
   - 性能报告

5. **提交代码**
   ```bash
   # 提交所有更改
   git add .
   git commit -m "Complete performance optimization

   - Memory usage reduced by 70-90%
   - Loading time reduced by 50-70%
   - Checksum calculation accelerated by 5-7x
   - CMAP lookup improved by 3-5x

   All unit tests passing, performance targets achieved."

   # 推送到远程
   git push origin feature/performance-optimization
   ```

---

## 成功标准总结

### 功能完整性 ✅
- [ ] 所有现有单元测试通过
- [ ] 无功能性回归 bug
- [ ] 支持 TTC, CFF, GVAR, COLR 等复杂表

### 性能指标 ✅
- [ ] 小字体加载提速 >= 2.5x
- [ ] 中大字体加载提速 >= 3.3x
- [ ] 校验和计算提速 >= 5x
- [ ] Unicode 查询提速 >= 3x
- [ ] 内存占用减少 >= 70% (延迟加载场景)

### 代码质量 ✅
- [ ] 代码覆盖率 >= 80%
- [ ] 无严重安全警告
- [ ] API 文档完整性 >= 90%
- [ ] 代码审查通过

### 用户体验 ✅
- [ ] 配置简单明了
- [ ] 向后兼容，迁移成本 <= 1 小时
- [ ] 性能报告和文档完整

---

## 附录 A: 快速开始指南

### 初始设置
```bash
# 1. 克隆仓库
git clone https://github.com/yourorg/FontFlat.git
cd FontFlat

# 2. 签出优化分支
git checkout feature/performance-optimization

# 3. 准备测试字体
# 将测试字体复制到两个 SampleFolders 目录

# 4. 建立基线
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj
dotnet run --project OTFontFile.Benchmarks -- -c Release > baseline.txt

# 记录基线结果到 PERFORMANCE_OPTIMIZATION_PLAN.md
```

### 验证优化效果
```bash
# 1. 运行单元测试
dotnet test

# 2. 运行基准测试
dotnet run --project OTFontFile.Benchmarks -- -c Release > optimized.txt

# 3. 对比结果
# 手动对比 baseline.txt 和 optimized.txt
# 或使用脚本生成对比报告
```

### 提交优化成果
```bash
# 1. 创建合并请求
git checkout -b merge/performance-optimization

# 2. 提交
git add .
git commit -m "Performance optimization complete"

# 3. 推送
git push origin merge/performance-optimization

# 4. 创建 PR 到 main 分支
```

---

## 附录 B: 故障排查

### 常见问题

**Q: 单元测试失败怎么办？**
- A: 检查是否所有测试字体都已正确放置在 SampleFolders 目录
- 确保测试代码的路径配置正确
- 查看测试输出中的具体错误信息

**Q: 基准测试结果不稳定怎么办？**
- A: 运行多次取平均值
- 关闭其他应用程序
- 确保使用 Release 配置
- 检查 CPU 是否运行在节能模式

**Q: 内存诊断不可用怎么办？**
- A: 确保具有管理员权限
- 在 Windows 上运行
- 降级到不使用内存诊断的配置

---

**文档版本**: 2.0
**最后更新**: 2025-12-23
**状态**: 测试基础设施已建立，等待基线测试数据填充
3. CMAP查找速度提升 **3x+**
4. Checksum计算速度提升 **4x+**
5. 保持100%功能兼容性

### 代码质量指标
1. 测试覆盖率 ≥ 90%
2. 无性能回归
3. 保持AOT兼容
4. 保持现有API签名
