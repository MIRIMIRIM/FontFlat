# OTFontFile.Performance.Tests

MSTest 单元测试项目，用于验证 OTFontFile 库的正确性和功能完整性。

## 项目结构

```
OTFontFile.Performance.Tests/
├── UnitTests/                    # 单元测试
│   ├── BufferTests.cs           # MBOBuffer 功能测试
│   ├── FileParsingTests.cs      # 文件解析测试
│   └── TableTests.cs            # 表解析测试
└── TestResources/                # 测试资源
    └── SampleFonts/              # 示例字体文件
```

## 测试类别

### 1. BufferTests.cs

测试 `MBOBuffer` 类的字节序转换和数据读取/写入功能：

- ✅ `GetByte` / `SetByte`
- ✅ `GetShort` / `SetShort`
- ✅ `GetUshort` / `SetUshort`
- ✅ `GetInt` / `SetInt`
- ✅ `GetUint` / `SetUint`
- ✅ `CalcChecksum` 校验和计算
- ✅ `BinaryEqual` 缓冲区比较
- ✅ 静态转换方法：`GetMBOshort`, `GetMBOushort`, `GetMBOint`, `GetMBOuint`

### 2. FileParsingTests.cs

测试字体文件的加载和解析功能：

- ✅ `OTFile.open()` 打开各种字体格式
- ✅ 单字体和字体集合(TTC)文件
- ✅ `OTFile.GetFont()` 获取字体对象
- ✅ 必需表验证：head, hhea, maxp, name, cmap, OS/2, post
- ✅ 表校验和验证
- ✅ DirectoryEntry 匹配

### 3. TableTests.cs

测试各种字体表的解析（集成测试阶段）：

- [ ] `Table_head` 必需字段验证
- [ ] `Table_cmap` 编码表和 Unicode 映射
- [ ] `Table_maxp` 字形数量
- [ ] `Table_name` 名称记录

## 测试资源

需要在 `TestResources/SampleFonts/` 目录下放置以下测试字体文件：

- `small.ttf` - 小型字体文件（<100 KB）
- `medium.ttf` - 中型字体文件（100 KB - 1 MB）
- `large.ttf` - 大型字体文件（>1 MB）
- `collection.ttc` - 字体集合文件（可选）

## 运行测试

### 使用 Visual Studio

1. 打开 `FontFlat.slnx`
2. 在 Test Explorer 中选择 "OTFontFile.Performance.Tests"
3. 运行所有测试或选择特定测试

### 使用 .NET CLI

```bash
# 运行所有测试
dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj

# 运行测试并查看详细输出
dotnet test --logger "console;verbosity=detailed"

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~BufferTests"

# 运行代码覆盖率
dotnet test --collect:"XPlat Code Coverage"
```

### 使用 PowerShell

```powershell
cd OTFontFile.Performance.Tests
dotnet test
```

## 预期结果

所有单元测试应该在优化前后都通过。这是性能优化的前提条件：

- **优化前**: 所有测试通过 ✅
- **优化后**: 所有测试必须仍然通过 ✅

任何测试失败都表示功能回归，必须修复。

## 扩展测试

### 添加新的字体文件

将测试字体文件（`.ttf`, `.otf`, `.ttc`）复制到 `TestResources/SampleFonts/` 目录：

```powershell
Copy-Item "path\to\your\font.ttf" "OTFontFile.Performance.Tests\TestResources\SampleFonts\"
```

### 添加新的测试用例

在相应的测试类中添加方法，使用 `[TestMethod]` 特性标记：

```csharp
[TestMethod]
public void MyNewFeature_ShouldWorkCorrectly()
{
    // Arrange - 准备测试数据
    // Act - 执行被测试的功能
    // Assert - 验证结果
}
```

## 注意事项

1. **测试隔离**: 每个测试方法应该独立，不依赖于其他测试的状态。
2. **资源清理**: 使用 `TestCleanup` 方法或 `using` 语句确保资源被正确释放。
3. **异常测试**: 使用 `Assert.ThrowsException` 测试预期的异常。
4. **性能基准测试不在该项目中**：性能测试请在 `OTFontFile.Benchmarks` 项目中进行。

## 代码覆盖率

目标：>= 80% 代码覆盖率

```bash
# 安装 coverlet 工具
dotnet add package coverlet.msbuild

# 运行测试并收集覆盖率
dotnet test --collect:"XPlat Code Coverage"

# 使用 reportgenerator 生成 HTML 报告
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport
```

## 故障排查

### 问题：测试找不到字体文件

**解决方法**：确保字体文件已复制到 `bin/Debug/net10.0/TestResources/SampleFonts/` 目录。

检查 `.csproj` 文件中的配置：

```xml
<ItemGroup>
  <None Update="TestResources\SampleFonts\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### 问题：测试超时

**解决方法**：对于大型字体测试，可能需要增加测试超时时间：

```csharp
[TestMethod]
[Timeout(30000)] // 30秒超时
public void LargeFileTest_ShouldComplete()
{
    // ...
}
```

## 贡献指南

1. 新的测试应该易于理解和维护
2. 使用有意义的测试名称（格式：`方法名_场景_预期`）
3. 为复杂的测试逻辑添加注释
4. 确保新测试在优化前后都能通过

## 相关文档

- [Optimization Plan](../../PERFORMANCE_OPTIMIZATION_PLAN.md)
- [Benchmark README](../OTFontFile.Benchmarks/README.md)
- [MSTest 文档](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)
