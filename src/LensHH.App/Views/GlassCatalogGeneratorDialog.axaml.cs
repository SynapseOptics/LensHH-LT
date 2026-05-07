using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class GlassCatalogGeneratorDialog : Window
{
    public GlassCatalogGeneratorDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GlassCatalogGeneratorViewModel vm)
        {
            vm.ShowMessage = async (title, message) =>
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(16),
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = message,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new Button
                            {
                                Content = "OK",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                Padding = new Avalonia.Thickness(24, 6)
                            }
                        }
                    }
                };

                var okButton = ((StackPanel)dialog.Content).Children[1] as Button;
                okButton!.Click += (s, ev) => dialog.Close();

                await dialog.ShowDialog(this);
            };

            vm.RequestFolderPath = async (startDir) =>
            {
                var startLocation = await StorageProvider.TryGetFolderFromPathAsync(startDir);
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder",
                    AllowMultiple = false,
                    SuggestedStartLocation = startLocation
                });

                return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            };
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
