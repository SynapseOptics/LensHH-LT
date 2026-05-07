using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.GlassCatalog;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class CherryPickCatalogDialog : Window
{
    public CherryPickCatalogDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CherryPickCatalogViewModel vm) return;

        vm.RequestFolderPath = startDir => PickFolder(startDir);
        vm.RequestOpenFilePath = startDir => PickAgfFile(startDir);
        vm.ShowMessage = (title, body) => ShowInfo(title, body);
    }

    private async Task<string?> PickFolder(string startDir)
    {
        var picker = StorageProvider;
        if (picker == null) return null;
        IStorageFolder? start = null;
        if (Directory.Exists(startDir))
            start = await picker.TryGetFolderFromPathAsync(startDir);
        var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder",
            SuggestedStartLocation = start,
            AllowMultiple = false,
        });
        if (folders.Count == 0) return null;
        return folders[0].TryGetLocalPath();
    }

    private async Task<string?> PickAgfFile(string startDir)
    {
        var picker = StorageProvider;
        if (picker == null) return null;
        IStorageFolder? start = null;
        if (Directory.Exists(startDir))
            start = await picker.TryGetFolderFromPathAsync(startDir);
        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open existing filtered catalog (.agf)",
            SuggestedStartLocation = start,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("AGF catalog") { Patterns = new[] { "*.agf", "*.AGF" } },
            },
        });
        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private async Task ShowInfo(string title, string body)
    {
        var box = new Window
        {
            Title = title,
            Width = 460, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var ok = new Button { Content = "OK", Width = 80, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => box.Close();
        stack.Children.Add(ok);
        box.Content = stack;
        await box.ShowDialog(this);
    }

    private void SourceGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CherryPickCatalogViewModel vm
            && sender is DataGrid grid
            && grid.SelectedItem is GlassEntry g)
        {
            vm.AddGlass(g);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
