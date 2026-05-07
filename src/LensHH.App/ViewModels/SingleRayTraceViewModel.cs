using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;

namespace LensHH.App.ViewModels;

public class RayTraceRow
{
    public int Surf { get; set; }
    public string X { get; set; } = "";
    public string Y { get; set; } = "";
    public string Z { get; set; } = "";
    public string L { get; set; } = "";
    public string M { get; set; } = "";
    public string N { get; set; } = "";
    public string Ln { get; set; } = "";
    public string Mn { get; set; } = "";
    public string Nn { get; set; } = "";
    public string AOI { get; set; } = "";
    public string Path { get; set; } = "";
    public string OPL { get; set; } = "";
    public string CumulOPL { get; set; } = "";
    public string Comment { get; set; } = "";
}

public partial class SingleRayTraceViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    // Input mode: true = field point index, false = Hy (normalized)
    [ObservableProperty] private bool _useFieldIndex = true;

    // Inputs — field point mode (dropdown)
    [ObservableProperty] private int _selectedFieldIndex;

    // Inputs — Hy mode
    [ObservableProperty] private double _hy;

    // Common inputs
    [ObservableProperty] private double _px;
    [ObservableProperty] private double _py;
    [ObservableProperty] private int _selectedWaveIndex;

    // Dropdown items
    public ObservableCollection<string> FieldOptions { get; } = new();
    public ObservableCollection<string> WaveOptions { get; } = new();

    public ObservableCollection<RayTraceRow> Rows { get; } = new();

    public SingleRayTraceViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += _ => RefreshOptions();
    }

    public void RefreshOptions()
    {
        FieldOptions.Clear();
        WaveOptions.Clear();

        var system = _session.System;

        for (int i = 0; i < system.Fields.Count; i++)
        {
            string unit = system.FieldType == Core.Enums.FieldType.ObjectAngle ? "\u00b0" : " mm";
            FieldOptions.Add($"{i + 1}: {system.Fields[i].Y}{unit}");
        }
        if (SelectedFieldIndex >= FieldOptions.Count)
            SelectedFieldIndex = 0;

        for (int i = 0; i < system.Wavelengths.Count; i++)
        {
            string primary = i == system.PrimaryWavelengthIndex ? " *" : "";
            WaveOptions.Add($"{i + 1}: {system.Wavelengths[i].Value:F4} \u00b5m{primary}");
        }
        if (SelectedWaveIndex >= WaveOptions.Count)
            SelectedWaveIndex = system.PrimaryWavelengthIndex;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        IsBusy = true;
        StatusText = "";
        Rows.Clear();

        try
        {
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;

            // Refresh dropdowns if empty
            if (FieldOptions.Count == 0) RefreshOptions();

            // Determine field value
            double fieldY;
            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectAngle ? "deg" : "mm";
            if (UseFieldIndex)
            {
                int idx = Math.Clamp(SelectedFieldIndex, 0, system.Fields.Count - 1);
                fieldY = system.Fields[idx].Y;
            }
            else
            {
                double maxField = 0;
                foreach (var f in system.Fields)
                    if (Math.Abs(f.Y) > Math.Abs(maxField)) maxField = f.Y;
                fieldY = Hy * maxField;
            }

            int waveIdx = Math.Clamp(SelectedWaveIndex, 0, system.Wavelengths.Count - 1);

            var result = await Task.Run(() =>
                RayTraceListing.Trace(system, glassMgr, fieldY, Px, Py, waveIdx));

            if (!result.Success)
            {
                StatusText = "Ray trace failed.";
                return;
            }

            StatusText = $"Field: {Fmt(fieldY)} {fieldUnit}, Px={Fmt(Px)}, Py={Fmt(Py)}, " +
                         $"Wave {waveIdx + 1}: {result.Wavelength.ToString("F4", CultureInfo.InvariantCulture)} \u00b5m";

            foreach (var s in result.Surfaces)
            {
                // AOI = angle between incoming ray direction and surface normal
                // cos(AOI) = |dot(ray_dir, normal)| — use direction BEFORE refraction
                // For surface 0 (object), no AOI.
                string aoi = "";
                if (s.SurfaceIndex > 0 && (s.Ln != 0 || s.Mn != 0 || s.Nn != 0))
                {
                    // The direction cosines stored are AFTER refraction/reflection.
                    // For AOI we need the INCOMING direction (before this surface).
                    // Use previous surface's direction cosines as the incoming direction.
                    int prevIdx = s.SurfaceIndex - 1;
                    if (prevIdx >= 0 && prevIdx < result.Surfaces.Count)
                    {
                        var prev = result.Surfaces[prevIdx];
                        double dot = Math.Abs(prev.L * s.Ln + prev.M * s.Mn + prev.N * s.Nn);
                        if (dot > 1.0) dot = 1.0;
                        double angleDeg = Math.Acos(dot) * 180.0 / Math.PI;
                        aoi = angleDeg.ToString("F6", CultureInfo.InvariantCulture);
                    }
                }

                Rows.Add(new RayTraceRow
                {
                    Surf = s.SurfaceIndex,
                    X = Fmt(s.X),
                    Y = Fmt(s.Y),
                    Z = Fmt(s.Z),
                    L = Fmt(s.L),
                    M = Fmt(s.M),
                    N = Fmt(s.N),
                    Ln = s.SurfaceIndex > 0 ? Fmt(s.Ln) : "",
                    Mn = s.SurfaceIndex > 0 ? Fmt(s.Mn) : "",
                    Nn = s.SurfaceIndex > 0 ? Fmt(s.Nn) : "",
                    AOI = aoi,
                    Path = Fmt(s.PathLength),
                    OPL = Fmt(s.OPL),
                    CumulOPL = Fmt(s.CumulativeOPL),
                    Comment = s.Vignetted ? "Vignetted" : "",
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copy the ray-trace table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (Rows.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(StatusText))
            sb.AppendLine(StatusText);
        sb.AppendLine("Surf\tX\tY\tZ\tL\tM\tN\tLn\tMn\tNn\tAOI(deg)\tPath\tOPL\tCumulOPL\tComment");
        foreach (var r in Rows)
        {
            sb.Append(r.Surf).Append('\t')
              .Append(r.X).Append('\t').Append(r.Y).Append('\t').Append(r.Z).Append('\t')
              .Append(r.L).Append('\t').Append(r.M).Append('\t').Append(r.N).Append('\t')
              .Append(r.Ln).Append('\t').Append(r.Mn).Append('\t').Append(r.Nn).Append('\t')
              .Append(r.AOI).Append('\t').Append(r.Path).Append('\t')
              .Append(r.OPL).Append('\t').Append(r.CumulOPL).Append('\t')
              .Append(r.Comment).AppendLine();
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(sb.ToString());
    }

    private static string Fmt(double v)
    {
        double abs = Math.Abs(v);
        if (abs == 0) return "0";
        if (abs >= 100) return v.ToString("F6", CultureInfo.InvariantCulture);
        if (abs >= 1) return v.ToString("F8", CultureInfo.InvariantCulture);
        if (abs >= 0.01) return v.ToString("F10", CultureInfo.InvariantCulture);
        return v.ToString("E10", CultureInfo.InvariantCulture);
    }
}
