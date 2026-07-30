using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LensHH.Core.NativeInterop;

namespace LensHH.App.Views.Controls;

/// <summary>
/// Compact, self-contained "GPU active" chip. Polls the ground-truth
/// <see cref="GpuActivity"/> counters (kernel launches, not checkbox state) and
/// lights up bright while ANY GPU mechanism is doing work, dim when idle, grey
/// when no CUDA device is present. The tooltip breaks down the three modes with
/// live counts. Drop it into any optimizer dialog's status area — no wiring.
/// </summary>
public partial class GpuStatusChip : UserControl
{
    private readonly DispatcherTimer _timer;
    private GpuActivity.Snapshot _prev;
    private int _idleTicks = 999;

    public GpuStatusChip()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _prev = GpuActivity.Take();
        _idleTicks = 999;
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void Refresh()
    {
        bool available = GpuGridTracer.IsAvailable;
        var now = GpuActivity.Take();
        bool grew = now.Total > _prev.Total;
        _prev = now;
        if (grew) _idleTicks = 0; else if (_idleTicks < 1000) _idleTicks++;
        bool active = available && _idleTicks <= 3;   // grew within ~1 second

        if (!available)
        {
            Apply("#F0F0F0", "#CCCCCC", "#CCCCCC", "#999999", "GPU off");
            ToolTip.SetTip(Chip, "No CUDA device detected on this machine.");
            return;
        }

        if (active)
            Apply("#E7F5EA", "#66BB6A", "#2E7D32", "#2E7D32", "GPU");
        else
            Apply("#F0F0F0", "#CCCCCC", "#AAAAAA", "#777777", "GPU");

        string state = now.AnyActive ? (active ? "  (active)" : "  (idle)") : "  (none — CPU only)";
        string tip =
            "GPU acceleration this run:" + state + "\n" +
            $"  Image-quality:  {now.ImageQualityTraces:N0} traces\n" +
            $"  Pre-screen:     {now.PreScreenBatches:N0} batches\n" +
            $"  DE-resident:    {now.DeGenerations:N0} generations";
        // Honesty hint: the image-quality trace accelerates spot/wavefront/sensitivity
        // operands on a field with off-axis extent (ray-aiming off). If the user enabled
        // it but no traces ran, say why rather than just look idle.
        if (LensHH.App.Session.AppPreferences.GpuImageQuality && now.ImageQualityTraces == 0)
            tip += "\n\nImage-quality GPU is enabled but hasn't engaged: it needs a" +
                   "\nspot/wavefront/sensitivity operand on an off-axis field, ray-aiming" +
                   "\noff. On-axis-only merits evaluate on the CPU.";
        ToolTip.SetTip(Chip, tip);
    }

    private void Apply(string bg, string border, string icon, string label, string text)
    {
        Chip.Background = new SolidColorBrush(Color.Parse(bg));
        Chip.BorderBrush = new SolidColorBrush(Color.Parse(border));
        Icon.Foreground = new SolidColorBrush(Color.Parse(icon));
        Label.Foreground = new SolidColorBrush(Color.Parse(label));
        Label.Text = text;
    }
}
