using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using Svg.Skia;

namespace LensHH.App.ViewModels;

public partial class LayoutViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _layoutImage;
    [ObservableProperty] private int _numRays = 15;
    [ObservableProperty] private int _startSurface = 0;
    [ObservableProperty] private bool _startFromSurface1 = true;

    /// <summary>
    /// Wavelength dropdown items. Index 0 = "Primary" (maps to -1 passed
    /// to the engine); indices 1..N map to wavelengths 0..N-1 on the
    /// optical system.
    /// </summary>
    public ObservableCollection<string> WavelengthOptions { get; } = new();

    [ObservableProperty] private int _selectedWavelengthOption;  // 0 = Primary

    partial void OnSelectedWavelengthOptionChanged(int value) => Render();

    private bool _dirty = true;

    /// <summary>
    /// True when the most recent Render() call returned early because
    /// the system was uncomputable (missing glass, etc.). Tracked so
    /// that the next system change which transitions the system back
    /// to a computable state can trigger an automatic re-render — the
    /// user fixed their glass name in the Lens Editor and expects to
    /// see the layout reappear without having to switch tabs or hit Redraw.
    /// </summary>
    private bool _wasBlocked;

    public LayoutViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += OnSystemChanged;
        RefreshWavelengthOptions();
        Render();
    }

    private void OnSystemChanged(string sender)
    {
        // Mark dirty only — the actual render happens on tab switch via
        // RenderIfDirty(), or when the user hits Redraw. Auto-rendering
        // here was causing a full ray-traced layout draw on every cell
        // edit elsewhere in the app, which made unrelated edits feel
        // sluggish even when the Layout tab wasn't visible.
        _dirty = true;
        RefreshWavelengthOptions();

        // Recovery path: if the previous Render() bailed out because
        // CannotCompute was true (e.g. missing glass on import) and the
        // user has now fixed it, render immediately so the layout
        // appears without having to switch tabs or click Redraw.
        if (_wasBlocked && !_session.CannotCompute)
            Render();
    }

    private void RefreshWavelengthOptions()
    {
        int prior = SelectedWavelengthOption;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("Primary");
        if (_session.System != null)
        {
            for (int i = 0; i < _session.System.Wavelengths.Count; i++)
            {
                double wl = _session.System.Wavelengths[i].Value;
                bool isPrimary = i == _session.System.PrimaryWavelengthIndex;
                string label = $"W{i + 1}: {wl.ToString("0.###", CultureInfo.InvariantCulture)} µm"
                               + (isPrimary ? " (primary)" : "");
                WavelengthOptions.Add(label);
            }
        }
        // Restore prior selection if still in range; else Primary.
        SelectedWavelengthOption = prior < WavelengthOptions.Count ? prior : 0;
    }

    /// <summary>
    /// Render only if the system has changed since the last render.
    /// Called on tab switch; use Render() to force a redraw (e.g. F5).
    /// </summary>
    public void RenderIfDirty()
    {
        if (_dirty)
            Render();
    }

    [RelayCommand]
    public void Render()
    {
        if (_session.CannotCompute)
        {
            LayoutImage = null;
            _dirty = false;
            _wasBlocked = true;
            return;
        }
        _wasBlocked = false;
        try
        {
            bool fromSurf1 = StartSurface > 0 || StartFromSurface1;
            // SelectedWavelengthOption: 0 = "Primary" → pass -1 to engine
            // (engine treats -1 as "use primary"); n ≥ 1 maps to wavelength n-1.
            int wIdx = SelectedWavelengthOption <= 0 ? -1 : SelectedWavelengthOption - 1;
            var layout = SystemLayout.ComputeLayout(
                _session.System, _session.GlassCatalog,
                numRays: NumRays,
                startFromSurface1: fromSurf1,
                wavelengthIndex: wIdx);
            // Pass the system's field list so the renderer can show the
            // per-field summary panel (and flag fields with 0 traced rays).
            var fieldYs = _session.System.Fields.Select(f => f.Y).ToList();
            string fieldUnit = _session.System.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string svg = SystemLayoutRenderer.Render(layout, width: 1600, height: 800,
                fieldYs: fieldYs, fieldUnit: fieldUnit);

            using var skSvg = new SKSvg();
            skSvg.FromSvg(svg);

            if (skSvg.Picture != null)
            {
                var bounds = skSvg.Picture.CullRect;
                int w = (int)bounds.Width;
                int h = (int)bounds.Height;
                if (w < 100) w = 1600;
                if (h < 100) h = 800;

                // Render at 3x for sharp display on high-DPI screens
                const int scale = 3;
                using var bitmap = new SkiaSharp.SKBitmap(w * scale, h * scale);
                using var canvas = new SkiaSharp.SKCanvas(bitmap);
                canvas.Clear(SkiaSharp.SKColors.White);
                canvas.Scale(scale);
                canvas.DrawPicture(skSvg.Picture);

                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var stream = new System.IO.MemoryStream(data.ToArray());
                LayoutImage = new Bitmap(stream);
            }

            _dirty = false;
        }
        catch
        {
            LayoutImage = null;
        }
    }
}
