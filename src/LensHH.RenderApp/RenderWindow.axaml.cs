using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace LensHH.RenderApp;

public partial class RenderWindow : Window
{
    public RenderWindow()
    {
        InitializeComponent();
    }

    public void UpdateImage(string title, Bitmap bitmap)
    {
        PlaceholderText.IsVisible = false;

        // Check if a tab with this title already exists — update it
        foreach (var item in TabControl.Items.OfType<TabItem>())
        {
            if (item.Header is string header && header == title)
            {
                if (item.Content is ScrollViewer sv && sv.Content is Image img)
                    img.Source = bitmap;
                TabControl.SelectedItem = item;
                return;
            }
        }

        // Create new tab
        var image = new Image
        {
            Stretch = Avalonia.Media.Stretch.Uniform,
            Margin = new Avalonia.Thickness(4),
            Source = bitmap
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = image
        };

        var tab = new TabItem
        {
            Header = title,
            Content = scrollViewer
        };

        TabControl.Items.Add(tab);
        TabControl.SelectedItem = tab;

        // Auto-resize to fit content, capped at reasonable bounds
        double desiredW = Math.Max(900, Math.Min(bitmap.PixelSize.Width / 2.0 + 20, 1800));
        double desiredH = Math.Max(700, Math.Min(bitmap.PixelSize.Height / 2.0 + 80, 1100));
        Width = desiredW;
        Height = desiredH;
    }

    public void ClearDisplay()
    {
        TabControl.Items.Clear();
        PlaceholderText.IsVisible = true;
    }
}
