using Avalonia.Platform.Storage;
using CJKCharacterCount.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace CJKCharacterCount.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private FontAnalyzeResult? _analyzeResult;

    [ObservableProperty]
    private string? _selectedFontPath;

    [ObservableProperty]
    private string? _fontName;

    [ObservableProperty]
    private int _selectedFontIndex = 0;

    [ObservableProperty]
    private bool _isFontSelectorVisible = false;

    public ObservableCollection<string> FontList { get; } = new();

    public ObservableCollection<TableData> CjkTables { get; } = new();
    private static readonly string[] options = ["*.ttf", "*.otf", "*.ttc", "*.otc"];

    public ObservableCollection<BlockData> UnicodeBlocks { get; } = new();

    // Store mapped indices for the current file
    private List<int> _currentFontIndices = [];

    public MainViewModel()
    {
        StatusMessage = Core.Localization.Get("Msg_NoFile");
    }

    [RelayCommand]
    private async Task OpenFile(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Core.Localization.Get("Select font file"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Fonts") { Patterns = options }
            }
        });

        if (files.Count >= 1)
        {
            if (files[0].TryGetLocalPath() is string path)
            {
                await OpenFilePath(path);
            }
        }
    }

    public async Task OpenFilePath(string path)
    {
        SelectedFontPath = path;

        // Get Fonts
        FontList.Clear();
        _currentFontIndices.Clear();
        IsFontSelectorVisible = false;

        // Reset selection to ensure change notification later
        SelectedFontIndex = -1;

        try
        {
            var fonts = FontAnalyzer.GetFontsInCollection(path, Core.Localization.Culture);
            if (fonts.Count > 0)
            {
                foreach (var f in fonts)
                {
                    FontList.Add(f.Name);
                    _currentFontIndices.Add(f.Index);
                }

                IsFontSelectorVisible = fonts.Count > 1;

                // Trigger selection and analysis
                SelectedFontIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reading font: {ex.Message}";
        }
    }

    private void RefreshFontList()
    {
        if (string.IsNullOrEmpty(SelectedFontPath)) return;

        var currentIdx = SelectedFontIndex;
        FontList.Clear();

        try
        {
            var fonts = FontAnalyzer.GetFontsInCollection(SelectedFontPath, Core.Localization.Culture);
            foreach (var f in fonts)
            {
                FontList.Add(f.Name);
            }

            // Restore selection
            if (currentIdx >= 0 && currentIdx < FontList.Count)
            {
                SelectedFontIndex = currentIdx;
            }
        }
        catch { }
    }

    partial void OnSelectedFontIndexChanged(int value)
    {
        if (string.IsNullOrEmpty(SelectedFontPath) || value < 0 || value >= _currentFontIndices.Count) return;

        // Trigger re-analysis
        // Fire and forget but capture tasks?
        // Since property setter is synchronous, we probably should use a command or async handler.
        // But for ComboBox binding, this fires.
        // Let's call async method carefully.
        _ = AnalyzeFont(SelectedFontPath, _currentFontIndices[value]);
    }

    private async Task AnalyzeFont(string path, int index = -1)
    {
        IsLoading = true;
        StatusMessage = $"Analyzing {Path.GetFileName(path)}...";

        try
        {
            var result = await Task.Run(() =>
            {
                var analyzer = new FontAnalyzer(path, index); // Use index
                return (analyzer.FontName, analyzer.Analyze());
            });

            FontName = result.FontName;
            AnalyzeResult = result.Item2; // This calls UpdateStatsDisplay via OnAnalyzeResultChanged? No, OnAnalyzeResultChanged just updates CanSave.
                                          // Wait, OnAnalyzeResultChanged calls SaveReportCommand.NotifyCanExecuteChanged().
                                          // We need to call UpdateStatsDisplay explicitly or wire it up.

            UpdateStatsDisplay(); // Explicit call
            StatusMessage = $"Analysis complete: {FontName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AnalyzeResult = null;
            UpdateStatsDisplay();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateStatsDisplay()
    {
        CjkTables.Clear();
        UnicodeBlocks.Clear();

        if (AnalyzeResult == null) return;

        // Group CJK Stats by header
        // For simplicity in Grid, just flatten or use groups?
        // Let's list them flat for now but sorted

        var groups = new[] { CJKGroup.Simplified, CJKGroup.Mixed, CJKGroup.Traditional };
        var lang = Core.Localization.Culture.Name;
        // Map culture to key used in Registry/Resources: 
        // "zh-Hans" or "zh-CN" -> "zhs"
        // "zh-Hant" or "zh-TW" -> "zht"
        // else "en"
        string nameKey = "en";
        if (lang.StartsWith("zh-Hans") || lang.StartsWith("zh-CN")) nameKey = "zhs";
        else if (lang.StartsWith("zh-Hant") || lang.StartsWith("zh-TW") || lang.StartsWith("zh-HK")) nameKey = "zht";

        foreach (var group in groups)
        {
            // Insert Group Header
            string headerKey = group switch
            {
                CJKGroup.Simplified => "Section_Simplified",
                CJKGroup.Mixed => "Section_Mixed",
                CJKGroup.Traditional => "Section_Traditional",
                _ => ""
            };

            if (!string.IsNullOrEmpty(headerKey))
            {
                CjkTables.Add(new TableData(Core.Localization.Get(headerKey), 0, 0, 0, true));
            }

            foreach (var table in CJKTableRegistry.GetByGroup(group))
            {
                if (AnalyzeResult.CJKStatistics.TryGetValue(table.Id, out var stats))
                {
                    string tableName = table.LocalizedNames.TryGetValue(nameKey, out var n) ? n : table.LocalizedNames["en"];
                    CjkTables.Add(new TableData(tableName, stats.Covered, stats.Total, stats.Percentage));
                }
            }
        }

        // Unicode Blocks
        foreach (var block in Core.UnicodeBlocks.AllBlocks)
        {
            if (AnalyzeResult.UnicodeBlockStatistics.TryGetValue(block.Name, out var count))
            {
                // Total? Block doesn't store total.
                // We can calc total from ranges?
                int total = 0;
                foreach (var r in block.AssignedRanges.Span) total += (r.End - r.Start + 1);

                double pct = total > 0 ? (double)count / total * 100 : 0;

                // Try localized name: Block_{Name with spaces replaced by nothing or just Key}
                // UnicodeBlocks names are like "Basic Latin", "CJK Unified Ideographs"
                // Let's sanitize name for Resource Key: "Block_BasicLatin"
                string resKey = "Block_" + block.Name.Replace(" ", "").Replace("-", "");
                string displayName = Core.Localization.Get(resKey);
                if (displayName == resKey) displayName = block.Name; // Fallback

                UnicodeBlocks.Add(new BlockData(displayName, count, total, pct));
            }
        }

        // Total
        if (AnalyzeResult.UnicodeBlockStatistics.TryGetValue("Total", out int totalCount))
        {
            // Total CJK characters?
            // Python "Total" is specific sum.
            // We display it separately?
        }
    }

    public Services.LocalizationService Loc => Services.LocalizationService.Instance;

    [RelayCommand]
    private void ChangeLanguage(string langCode)
    {
        try
        {
            var culture = new CultureInfo(langCode);
            Core.Localization.Culture = culture;

            // Re-update stats display if labels depend on localization (like CJK Table names)
            UpdateStatsDisplay();
            RefreshFontList();

            // Update status message if it was a default message
            if (AnalyzeResult == null)
            {
                StatusMessage = Core.Localization.Get("Msg_NoFile");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error changing language: {ex.Message}";
        }
    }
    [RelayCommand(CanExecute = nameof(CanSaveReport))]
    private async Task SaveReport(IStorageProvider storageProvider)
    {
        if (AnalyzeResult == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Core.Localization.Get("Menu_Save"),
            DefaultExtension = "txt",
            SuggestedFileName = $"{FontName}_Report.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } }
            }
        });

        if (file != null)
        {
            try
            {
                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream);

                // Header
                await writer.WriteLineAsync($"CJK Character Count Report");
                await writer.WriteLineAsync($"--------------------------");
                await writer.WriteLineAsync($"Font: {FontName}");
                await writer.WriteLineAsync($"File: {SelectedFontPath}");
                await writer.WriteLineAsync($"Time: {DateTime.Now}");
                await writer.WriteLineAsync($"Total CJK Characters: {AnalyzeResult.TotalCJKCharacters}");
                await writer.WriteLineAsync();

                // CJK Tables
                await writer.WriteLineAsync($"[ {Core.Localization.Get("Header_CJKTables")} ]");
                foreach (var item in CjkTables)
                {
                    await writer.WriteLineAsync($"{item.Name}: {item.Covered}/{item.Total} ({item.Percentage:F2}%)");
                }
                await writer.WriteLineAsync();

                // Unicode Blocks
                await writer.WriteLineAsync($"[ {Core.Localization.Get("Header_UnicodeBlocks")} ]");
                foreach (var item in UnicodeBlocks)
                {
                    await writer.WriteLineAsync($"{item.Name}: {item.Covered}");
                }

                StatusMessage = string.Format(Core.Localization.Get("Msg_ReportSaved"), file.Name);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving report: {ex.Message}";
            }
        }
    }

    private bool CanSaveReport => AnalyzeResult != null;

    partial void OnAnalyzeResultChanged(FontAnalyzeResult? value)
    {
        SaveReportCommand.NotifyCanExecuteChanged();
    }
}

public record TableData(string Name, int Covered, int Total, double Percentage, bool IsHeader = false);
public record BlockData(string Name, int Covered, int Total, double Percentage);
