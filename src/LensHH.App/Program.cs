using System;
using System.Globalization;
using Avalonia;
using LensHH.Core.Activation;

namespace LensHH.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Force invariant culture so all numeric TextBox / DataGrid bindings
        // use '.' as the decimal separator regardless of OS locale.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // Load existing license or start/continue 45-day trial
        ActivationManager.TryLoadExistingActivation();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
