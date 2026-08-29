using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LensHH.App.Session;
using LensHH.Core.MeritFunction;
using LensHH.Core.NativeInterop;
using LensHH.Core.Optimization;

namespace LensHH.App.Views;

/// <summary>
/// The "Preview" button: says what an optimization run will actually execute, before it runs.
/// </summary>
/// <remarks>
/// This must NEVER predict the path with logic of its own. It asks
/// <see cref="ComputePathPlanner"/> — the same code the optimizer calls to choose its path —
/// and formats the answer. A second, parallel implementation would drift from the real one
/// and start lying, which is the exact problem Preview exists to solve.
///
/// It also collects variables the same way the optimizer does (a throwaway
/// <see cref="LocalOptimizer"/> + <c>CollectVariables()</c>) rather than re-deriving the
/// variable list, for the same reason.
/// </remarks>
public static class ComputePathPreview
{
    /// <summary>
    /// Builds the report for a run configured as given. <paramref name="algorithm"/> is the
    /// dialog's own name for what it is about to launch, e.g. "Basin Hopping (HJ + LM)".
    /// </summary>
    public static string Build(
        GuiSession session,
        string algorithm,
        EngineMode requestedEngine,
        MeritDerivativeMode requestedDerivative,
        bool useBroydenUpdate,
        bool gpuImageOperands,
        string? extra = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(algorithm);
        sb.AppendLine(new string('-', algorithm.Length));
        sb.AppendLine();

        try
        {
            var probe = new LocalOptimizer(session.System, session.MeritFunction, session.GlassCatalog);
            probe.CollectVariables();

            var plan = ComputePathPlanner.Plan(
                session.System,
                session.MeritFunction,
                probe.Variables,
                requestedEngine,
                requestedDerivative,
                useBroydenUpdate,
                forceEngineOverride: false,
                requestedUseGpu: gpuImageOperands,
                gpuImageOperands: gpuImageOperands);

            sb.Append(plan.Report());

            if (probe.Variables.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNING: no variables are defined — the run will stop immediately.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("Could not determine the compute path:");
            sb.AppendLine("  " + ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine();
            sb.Append(extra);
        }
        return sb.ToString();
    }

    /// <summary>Shows the report in a modal, selectable so it can be copied into a bug report.</summary>
    public static async Task ShowAsync(Window owner, string report)
    {
        var win = new Window
        {
            Title = "Compute path preview",
            Width = 620,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };

        var text = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
            BorderThickness = new Avalonia.Thickness(0),
        };

        var close = new Button
        {
            Content = "Close",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => win.Close();

        var grid = new Grid { Margin = new Avalonia.Thickness(16), RowDefinitions = new RowDefinitions("*,Auto") };
        var scroll = new ScrollViewer { Content = text, Margin = new Avalonia.Thickness(0, 0, 0, 12) };
        Grid.SetRow(scroll, 0);
        Grid.SetRow(close, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(close);
        win.Content = grid;

        await win.ShowDialog(owner);
    }
}
