using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using LensHH.App.Session;
using LensHH.App.ViewModels;
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

        // Keep CPU-bound optimization at full speed even when the window is not in
        // the foreground (opt out of Windows 11 power throttling / EcoQoS). Without
        // this, switching to another app parks our worker threads on efficiency
        // cores and the run slows to a crawl.
        LensHH.IO.WindowsPerformance.DisablePowerThrottling();

        // Load existing license or start/continue 45-day trial
        ActivationManager.TryLoadExistingActivation();

        // Hidden dev smoke: drive the DE-pipeline dialog's view-model headlessly.
        //   LensHH.App --de-smoke <lens.lhlt> <outDir>
        if (args.Length >= 3 && args[0] == "--de-smoke")
        {
            Environment.Exit(RunDeSmoke(args[1], args[2]));
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Headless end-to-end check of DePipelineDialogViewModel.Run (the exact code the dialog runs):
    // load a lens, configure a short CPU run, execute the Run command, verify the gallery populates
    // and the pre-polish pool auto-saves. Returns 0 on success.
    private static int RunDeSmoke(string lens, string outDir)
    {
        int result = -1;
        BuildAvaloniaApp().SetupWithoutStarting();   // init dispatcher + platform, no window
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var session = new GuiSession();
                session.OpenFile(lens, "lhlt");
                var _ = session.GlassCatalog;   // ensure catalogs load
                var vm = new DePipelineDialogViewModel(session)
                {
                    UseGpu = false,
                    Generations = 20,
                    PopulationSize = 48,
                    SeedsToEmit = 4,
                    PolishCount = 4,
                    PolishMethodIndex = 1,      // Local LM
                    LmIterations = 50,
                    OutputDir = outDir,
                };
                await vm.RunCommand.ExecuteAsync(null);
                string preDir = Path.Combine(outDir, "seeds_pre_polish");
                int pre = Directory.Exists(preDir) ? Directory.GetFiles(preDir, "*.lhlt").Length : 0;
                Console.WriteLine($"SMOKE: cards={vm.Cards.Count}  pre_polish_files={pre}  status={vm.StatusText}");
                result = (vm.Cards.Count > 0 && pre > 0) ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SMOKE FAIL: {ex.GetType().Name}: {ex.Message}");
                result = 1;
            }
            finally { cts.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(cts.Token);
        return result;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
