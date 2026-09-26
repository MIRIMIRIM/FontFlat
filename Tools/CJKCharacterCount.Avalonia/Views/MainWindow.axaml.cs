using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CJKCharacterCount.Avalonia.ViewModels;
using System;

namespace CJKCharacterCount.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null)
        {
            foreach (var item in files)
            {
                var path = item.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) continue;
                if (IsSupportedFontPath(path))
                {
                    e.DragEffects = DragDropEffects.Copy;
                    return;
                }
            }
        }
        e.DragEffects = DragDropEffects.None;
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null)
        {
            // Take first valid file
            foreach (var item in files)
            {
                var path = item.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) continue;
                if (IsSupportedFontPath(path))
                {
                    if (DataContext is MainViewModel vm)
                    {
                        try
                        {
                            await vm.OpenFilePath(path);
                        }
                        catch (Exception ex)
                        {
                            vm.StatusMessage = $"Error opening dropped font: {ex.Message}";
                        }
                    }
                    return;
                }
            }
        }
    }

    private static bool IsSupportedFontPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".otc", StringComparison.OrdinalIgnoreCase);
    }
}
