using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LensHH.Core.Activation;

namespace LensHH.RenderApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load existing license or trial (same as MCP server)
        ActivationManager.TryLoadExistingActivation();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new RenderWindow();
            desktop.MainWindow = window;

            var pipeServer = new PipeServer(window);
            pipeServer.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
