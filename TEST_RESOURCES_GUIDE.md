# OTFontFile 测试资源准备指南

## 概述

本项目使用真实的 OpenType 字体文件进行功能测试和性能基准测试。由于字体文件可能受版权保护，这些文件**不会提交到 Git 仓库**，而是从本地系统或公开来源获取。

## 目录结构

```
FontFlat/
├── OTFontFile.Performance.Tests/
│   └── TestResources/SampleFonts/
│       └── .gitkeep              # 目录占位符
└── OTFontFile.Benchmarks/
    └── BenchmarkResources/SampleFonts/
        └── .gitkeep              # 目录占位符
```

## 测试字体要求

### 1. OTFontFile.Performance.Tests (功能测试)

**需要的字体文件**：
- `small.ttf` - 小字体 (< 100KB)
  - 单一字体文件
  - 包含基本拉丁字符
  - 建议: ASCII 字符集

- `standard.ttf` - 标准字体 (~100KB - 1MB)
  - 包含基础拉丁、西欧文字
  - 建议: Noto Sans, Roboto, Arial

- `large.ttf` - 大字体 (> 5MB)
  - 复杂字体，包含大量字形
  - 建议: CJK 字体、Emoji 字体

- `collection.ttc` - 字体集合 (~10-50MB)
  - 包含多种字形的 TTC 文件
  - 用于测试集合解析功能

**文件命名约定**：
```
small.ttf          # 小型字体
medium.ttf         # 中型字体
large.ttf          # 大型字体
collection.ttc     # 字体集合
cjk.ttf            # 可选：CJK字体
emoji.ttf          # 可选：Emoji字体
variable.ttf       # 可选：可变字体(color)
```

### 2. OTFontFile.Benchmarks (性能基准测试)

**需要的字体文件**：
- `small.ttf` - 小字体 (< 100KB)
  - 文件大小: 约 50-100KB
  - 用于快速加载基准测试

- `medium.ttf` - 中型字体 (100KB - 2MB)
  - 文件大小: 约 1-2MB
  - 用于标准加载基准测试

- `large.ttf` - 大字体 (> 5MB)
  - 文件大小: 约 10-20MB
  - 用于大文件加载基准测试

- `collection.ttc` - 字体集合
  - 文件大小: 约 10-50MB
  - 用于 TTC 解析基准测试

## 获取测试字体

### 方案 1: 使用系统字体（推荐）

**Windows**:
```powershell
# 复制系统字体到测试目录
Copy-Item "C:\Windows\Fonts\arial.ttf" "OTFontFile.Performance.Tests\TestResources\SampleFonts\small.ttf"
Copy-Item "C:\Windows\Fonts\arialbd.ttf" "OTFontFile.Performance.Tests\TestResources\SampleFonts\medium.ttf"

# 如果有 CJK 字体
Copy-Item "C:\Windows\Fonts\msyh.ttc" "OTFontFile.Performance.Tests\TestResources\SampleFonts\collection.ttc"
```

**macOS**:
```bash
# 复制系统字体
cp "/System/Library/Fonts/Helvetica.ttc" OTFontFile.Performance.Tests/TestResources/SampleFonts/medium.ttf
cp "/System/Library/Fonts/Supplemental/Arial Unicode.ttf" OTFontFile.Performance.Tests/TestResources/SampleFonts/large.ttf
```

### 方案 2: 使用开源字体

推荐使用 SIL Open Font License (OFL) 字体：

**Google Fonts**:
```bash
# 使用 git 下载
git clone https://github.com/googlefonts/noto-fonts.git

# 复制需要的字体
cp noto-fonts/hinted/ttf/NotoSans-Regular.ttf OTFontFile.Performance.Tests/TestResources/SampleFonts/small.ttf
cp noto-fonts/unhinted/ttf/NotoSansCJKsc-Regular.ttc OTFontFile.Performance.Tests/TestResources/SampleFonts/collection.ttc
```

**Source Han Sans (思源黑体)**:
- 下载地址: https://github.com/adobe-fonts/source-han-sans
- TTC 文件适合测试集合功能

**Noto Fonts 项目**:
- GitHub: https://github.com/googlefonts/noto-fonts
- 包含多种语言和字体类型

### 方案 3: 生成测试字体（高级）

如果需要完全控制的测试环境，可以使用 FontForge 或其他工具生成测试字体：

```bash
# 使用 FontForge 生成简单字体
# 注意: 这需要 FontForge 知识
```

## 准备脚本

### Windows PowerShell 脚本

创建 `PrepareTestFonts.ps1`:

```powershell
# 准备测试字体脚本
# 使用前请修改源字体路径

$testResourceDirs = @(
    "OTFontFile.Performance.Tests\TestResources\SampleFonts",
    "OTFontFile.Benchmarks\BenchmarkResources\SampleFonts"
)

# 创建目录
foreach ($dir in $testResourceDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "创建目录: $dir" -ForegroundColor Green
    }
}

# 定义字体映射 (修改为实际存在的字体路径)
$fontMap = @{
    "small.ttf"       = "C:\Windows\Fonts\arial.ttf"
    "medium.ttf"      = "C:\Windows\Fonts\seguiemj.ttf"  # Emoji 字体
    "large.ttf"       = "C:\Windows\Fonts\msyhbd.ttc"  # 大型 CJK 字体
    "collection.ttc"  = "C:\Windows\Fonts\YuGothM.ttc"  # TTC 集合
}

# 复制字体文件
foreach ($dir in $testResourceDirs) {
    Write-Host "`n处理目录: $dir" -ForegroundColor Cyan

    foreach ($target in $fontMap.Keys) {
        $source = $fontMap[$target]

        if (Test-Path $source) {
            Copy-Item $source (Join-Path $dir $target) -Force
            $size = (Get-Item $source).Length / 1KB
            Write-Host "  ✓ $target ← $source (${size:N1} KB)" -ForegroundColor Green
        } else {
            Write-Host "  ✗ $target (源文件不存在: $source)" -ForegroundColor Red
        }
    }
}

Write-Host "`n完成! 请验证测试字体是否准备就绪." -ForegroundColor Yellow
Write-Host "运行测试前执行: dotnet build FontFlat.slnx" -ForegroundColor Yellow
```

**使用方法**：
```powershell
# 在项目根目录运行
.\PrepareTestFonts.ps1
```

### 复制基准测试字体

如果您已经在测试项目中准备了字体，可以直接复制到基准测试项目：

```powershell
# 复制测试字体到基准测试项目
Copy-Item -Path "OTFontFile.Performance.Tests\TestResources\SampleFonts\*" `
          -Destination "OTFontFile.Benchmarks\BenchmarkResources\SampleFonts\" `
          -Recurse -Force
```

## 验证测试资源

### 检查字体文件是否存在

```powershell
# 检查测试项目字体
Get-ChildItem "OTFontFile.Performance.Tests\TestResources\SampleFonts" -File | Select-Object Name,@{N="Size(KB)";E={$_.Length/1KB}}

# 检查基准测试字体
Get-ChildItem "OTFontFile.Benchmarks\BenchmarkResources/SampleFonts" -File | Select-Object Name,@{N="Size(KB)";E={$_.Length/1KB}}
```

### 运行测试验证

```bash
# 构建项目
dotnet build FontFlat.slnx

# 运行测试
dotnet test OTFontFile.Performance.Tests

# 运行基准测试（需要更长时间）
dotnet run --project OTFontFile.Benchmarks -- -c Release
```

## 常见问题

### Q1: 必须要所有字体文件吗？

**A**:
- 功能测试（MSTest）：需要至少 `small.ttf` 和 `medium.ttf`
- 基准测试（BenchmarkDotNet）：需要 `small.ttf`, `medium.ttf`, `large.ttf`, `collection.ttc`

如果缺少文件，测试会跳过相应用例（需要修改测试代码支持跳过）。

### Q2: 字体文件格式要求吗？

**A**:
- 支持：OpenType TrueType (.ttf), OpenType CFF (.otf), Font Collection (.ttc)
- 不支持：WOFF, WOFF2, EOT（这些是 web 字体格式）
- 建议：使用 .ttf，这是最常用的格式

### Q3: 字体大小有要求吗？

**A**:
- 小字体: < 100KB，用于快速测试
- 中型字体: 100KB - 2MB，用于标准测试
- 大字体: > 5MB，用于压力测试
- 集合文件: > 10MB，用于 TTC 测试

### Q4: 为什么字体文件被 gitignore？

**A**:
- 版权保护：字体通常受版权保护，不能随意分发
- 文件大小：字体文件较大，会增加仓库体积
- 本地化：开发者可以使用本地系统字体，无需从网络下载
- 安全：避免将不敏感的二进制文件提交到版本控制

### Q5: 可以使用自己创建的字体吗？

**A**: 可以，但需要确保：
1. 字体格式正确（OpenType TrueType）
2. 包含必要的表（head, hhea, hmtx, maxp, cmap 等）
3. 符合 OpenType 规范
4. 建议：先用已知的商业或开源字体，确保代码正确后再自定义

## 推荐字体来源

| 字体 | 大小 | 类型 | 授权 | 推荐度 |
|------|------|------|------|--------|
| Arial | ~300KB | TTF | 系统字体 | ⭐⭐⭐⭐⭐ (系统自带) |
| Noto Sans | ~200KB | TTF | SIL OFL | ⭐⭐⭐⭐⭐ (开源) |
| Roboto | ~150KB | TTF | Apache 2.0 | ⭐⭐⭐⭐⭐ (开源) |
| Source Han Sans | ~16MB | TTC | SIL OFL | ⭐⭐⭐⭐ (大字体) |
| Segoe UI Emoji | ~10MB | TTF | 系统字体 | ⭐⭐⭐⭐ (系统自带) |
| Noto Emoji | ~6MB | TTF | SIL OFL | ⭐⭐⭐⭐ (开源) |

## 版权许可说明

使用测试字体时，请遵守相应字体的授权许可：

- **O** 开源项目字体（如 Google Fonts 可用于开源项目
- **S** 系统字体（如 Arial, Segoe UI）：可用于个人开发
- **C** 商业字体：需要购买授权许可

本项目仅用于内部测试和基准测量，不涉及字体分发。

## 自动化测试资源检查

如果测试框架需要检查资源文件，可以这样实现：

```csharp
// 在测试类中添加
[TestInitialize]
public void TestInitialize()
{
    var testFontPath = Path.Combine(TestContext.TestDeploymentDirectory, "TestResources", "SampleFonts", "small.ttf");

    if (!File.Exists(testFontPath))
    {
        Assert.Inconclusive($"测试字体文件不存在: {testFontPath}\n" +
                           $"请参考 TEST_RESOURCES_GUIDE.md 准备测试资源");
    }
}
```

## 下一步

准备完成后：
1. 运行 `.\PrepareTestFonts.ps1` 准备字体文件
2. 执行 `dotnet build FontFlat.slnx` 构建项目
3. 执行 `dotnet test OTFontFile.Performance.Tests` 运行单元测试
4. 执行 `dotnet run --project OTFontFile.Benchmarks` 运行基准测试

---

**文档版本**: 1.0
**更新日期**: 2025-12-23
**适用版本**: OTFontFile Performance Optimization Phase 0+
