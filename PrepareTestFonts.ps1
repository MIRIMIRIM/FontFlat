# 准备 OTFontFile 测试字体资源
# 此脚本自动从系统复制字体到测试目录

param(
    [switch]$Force,
    [string]$SystemFontPath = "C:\Windows\Fonts"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OTFontFile 测试字体准备工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 定义目录
$testDirPerformance = "OTFontFile.Performance.Tests\TestResources\SampleFonts"
$testDirBenchmark = "OTFontFile.Benchmarks\BenchmarkResources\SampleFonts"

# 创建目录（如果不存在）
Write-Host "检查目录..." -ForegroundColor Yellow
foreach ($dir in @($testDirPerformance, $testDirBenchmark)) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  ✓ 创建目录: $dir" -ForegroundColor Green
    } else {
        Write-Host "  ✓ 目录已存在: $dir" -ForegroundColor Gray
    }
}

Write-Host ""

# 检测可用的系统字体（常用字体）
Write-Host "检测系统字体..." -ForegroundColor Yellow
$availableFonts = [System.IO.Directory]::GetFiles($SystemFontPath, "*.ttf") +
                  [System.IO.Directory]::GetFiles($SystemFontPath, "*.ttc") |
                  Where-Object { Test-Path $_ } |
                  ForEach-Object { Get-Item $_ } |
                  Select-Object -First 50

if ($availableFonts.Count -eq 0) {
    Write-Host "  ✗ 未找到系统字体文件" -ForegroundColor Red
    Write-Host "`n请手动指定字体文件，或检查系统字体路径" -ForegroundColor Yellow
    exit 1
}

Write-Host "  找到 $($availableFonts.Count) 个字体文件" -ForegroundColor Green

# 显示可用字体（前20个）
Write-Host "`n可用的系统字体 (显示前20个):" -ForegroundColor Gray
$availableFonts | Select-Object -First 20 | ForEach-Object {
    $size = $_.Length / 1KB
    Write-Host "  - $($_.Name) ($([int]$size) KB)" -ForegroundColor DarkGray
}

if ($availableFonts.Count -gt 20) {
    Write-Host "  ... 还有 $($availableFonts.Count - 20) 个其他字体" -ForegroundColor DarkGray
}

Write-Host ""

# 智能选择字体（基于大小和名称）
Write-Host "选择合适的测试字体..." -ForegroundColor Yellow

# 小字体 (< 200KB)
$smallFonts = $availableFonts |
              Where-Object { $_.Length -lt 200KB -and $_.Extension -eq ".ttf" } |
              Sort-Object Length |
              Select-Object -First 1

# 中型字体 (200KB - 2MB)
$mediumFonts = $availableFonts |
               Where-Object { $_.Length -ge 200KB -and $_.Length -lt 2MB -and $_.Extension -eq ".ttf" } |
               Sort-Object Length |
               Select-Object -First 1

# 大字体 (> 5MB)
$largeFonts = $availableFonts |
              Where-Object { $_.Length -ge 5MB } |
              Sort-Object Length |
              Select-Object -First 1

# 字体集合 (.ttc)
$collectionFonts = $availableFonts |
                   Where-Object { $_.Extension -eq ".ttc" } |
                   Sort-Object Length |
                   Select-Object -First 1

# 映射到目标文件名
$fontMap = @{}
$isComplete = $true

if ($smallFonts) {
    $fontMap["small.ttf"] = $smallFonts.FullName
    $size = $smallFonts.Length / 1KB
    Write-Host "  ✓ 小字体: $($smallFonts.Name) ($([int]$size) KB)" -ForegroundColor Green
} else {
    Write-Host "  ✗ 未找到合适的小字体 (<200KB)" -ForegroundColor Red
    $isComplete = $false
}

if ($mediumFonts) {
    $fontMap["medium.ttf"] = $mediumFonts.FullName
    $size = $mediumFonts.Length / 1KB
    Write-Host "  ✓ 中型字体: $($mediumFonts.Name) ($([int]$size) KB)" -ForegroundColor Green
} else {
    Write-Host "  ✗ 未找到合适的中型字体 (200KB-2MB)" -ForegroundColor Red
    $isComplete = $false
}

if ($largeFonts) {
    $fontMap["large.ttf"] = $largeFonts.FullName
    $size = $largeFonts.Length / 1MB
    Write-Host "  ✓ 大型字体: $($largeFonts.Name) ($([int]$size) MB)" -ForegroundColor Green
} else {
    Write-Host "  ✗ 未找到合适的大型字体 (>5MB)" -ForegroundColor Red
    $isComplete = $false
}

if ($collectionFonts) {
    $fontMap["collection.ttc"] = $collectionFonts.FullName
    $size = $collectionFonts.Length / 1MB
    Write-Host "  ✓ 字体集合: $($collectionFonts.Name) ($([int]$size) MB)" -ForegroundColor Green
} else {
    Write-Host "  ✗ 未找到字体集合文件 (.ttc)" -ForegroundColor Red
    $isComplete = $false
}

Write-Host ""

# 交互式确认
if (-not $Force) {
    Write-Host "准备复制以下文件:" -ForegroundColor Cyan
    foreach ($target in $fontMap.Keys) {
        $source = $fontMap[$target]
        Write-Host "  $target <-" -ForegroundColor Gray
        Write-Host "    来源: $source" -ForegroundColor DarkGray
    }

    $confirm = Read-Host "`n是否继续? (Y/N)"
    if ($confirm -ne "Y" -and $confirm -ne "y") {
        Write-Host "操作已取消" -ForegroundColor Yellow
        exit 0
    }
}

# 复制文件
Write-Host "`n正在复制文件..." -ForegroundColor Yellow

$successCount = 0
foreach ($dir in @($testDirPerformance, $testDirBenchmark)) {
    Write-Host "`n目标目录: $dir" -ForegroundColor Cyan

    foreach ($target in $fontMap.Keys) {
        $source = $fontMap[$target]
        $destination = Join-Path $dir $target

        try {
            Copy-Item $source $destination -Force
            $size = (Get-Item $source).Length / 1KB
            Write-Host "  ✓ $target ($([int]$size) KB)" -ForegroundColor Green
            $successCount++
        } catch {
            Write-Host "  ✗ 复制失败: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# 总结
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "准备完成!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($isComplete) {
    Write-Host "状态: 所有测试字体已准备完成 ✓" -ForegroundColor Green
} else {
    Write-Host "状态: 部分字体未找到 ⚠" -ForegroundColor Yellow
    Write-Host "提示: 您可以手动下载开源字体补充缺失的文件" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "  1. 构建项目: dotnet build FontFlat.slnx" -ForegroundColor Gray
Write-Host "  2. 运行测试: dotnet test OTFontFile.Performance.Tests" -ForegroundColor Gray
Write-Host "  3. 运行基准: dotnet run --project OTFontFile.Benchmarks -- -c Release" -ForegroundColor Gray
Write-Host ""
Write-Host "文档: 查看 TEST_RESOURCES_GUIDE.md 了解更多详细信息" -ForegroundColor Gray
Write-Host ""
