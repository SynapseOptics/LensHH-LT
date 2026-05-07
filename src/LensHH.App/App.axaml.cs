using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LensHH.App.Session;
using LensHH.App.ViewModels;
using LensHH.App.Views;

namespace LensHH.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load persisted preferences before any system is constructed.
        AppPreferences.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new GuiSession();
            var vm = new MainViewModel(session);
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            // Open a file passed on the command line — e.g. via the
            // .lhlt file association ("LensHH.App.exe \"C:\\path\\foo.lhlt\"")
            // or by drag-dropping onto the exe. Posted to the dispatcher
            // so the load runs after the window has finished initializing
            // and child VMs are subscribed to SystemChanged.
            var args = desktop.Args;
            if (args != null && args.Length > 0)
            {
                var path = args[0];
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var format = FormatFromExtension(path);
                        if (format != null) vm.OpenFile(path, format);
                    });
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".lhlt" => "lhlt",
            ".zmx" => "zmx",
            ".seq" => "codev",
            ".len" => "oslo",
            ".otx" => "optalix",
            _ => null,
        };
}
