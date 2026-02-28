using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CJKCharacterCount.Avalonia.ViewModels;

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
                var ext = System.IO.Path.GetExtension(path).ToLower();
                if (ext == ".ttf" || ext == ".otf" || ext == ".ttc" || ext == ".otc")
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
                var ext = System.IO.Path.GetExtension(path).ToLower();
                if (ext == ".ttf" || ext == ".otf" || ext == ".ttc" || ext == ".otc")
                {
                    if (DataContext is MainViewModel vm)
                    {
                        await vm.OpenFilePath(path);
                    }
                    return;
                }
            }
        }
    }
}