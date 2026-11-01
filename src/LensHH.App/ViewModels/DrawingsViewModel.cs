using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Tolerancing;
using LensHH.Tolerancing.Drawings;
using Svg.Skia;

namespace LensHH.App.ViewModels;

/// <summary>
/// One value on a drawing: what the tolerances give it, and what the user put in its place. A
/// blank override keeps the computed value; anything typed goes on the drawing instead.
/// </summary>
public partial class DrawingFieldRow : ObservableObject
{
    private readonly Action _changed;

    public DrawingFieldRow(string key, string label, bool isNumber, Action changed)
    {
        Key = key;
        Label = label;
        IsNumber = isNumber;
        _changed = changed;
    }

    public string Key { get; }
    public string Label { get; }
    public bool IsNumber { get; }

    [ObservableProperty] private string _computed = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverridden), nameof(HasError))]
    private string _text = "";

    public bool IsOverridden => !string.IsNullOrWhiteSpace(Text);

    /// <summary>A number that does not read as one: it is left off the drawing until it does.</summary>
    public bool HasError => IsNumber && IsOverridden && Number == null;

    public double? Number =>
        double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    /// <summary>Set without telling the window (when the element changes).</summary>
    public void Load(string text)
    {
        _loading = true;
        try { Text = text; }
        finally { _loading = false; }
    }

    private bool _loading;

    partial void OnTextChanged(string value)
    {
        if (!_loading) _changed();
    }

    [RelayCommand]
    private void Reset() => Text = "";
}

/// <summary>
/// The element drawings of the lens (Tolerancing ▸ Drawings…): made from its tolerance set, every
/// value on them open to change. The sheet-wide settings and each element's changes are kept in a
/// file beside the lens; the shop's own (title block, classes, notes) can be kept as the defaults
/// for every lens.
/// </summary>
public partial class DrawingsViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private DrawingSettings _base = new();
    private string _tolerances = "";

    // Each element's overrides as typed, by its first surface; and its notes.
    private readonly Dictionary<string, Dictionary<string, string>> _overrides = new();
    private readonly Dictionary<string, string> _notes = new();

    private IReadOnlyList<ElementDrawing> _computed = Array.Empty<ElementDrawing>();
    private DrawingRunResult? _result;
    private bool _loading;

    public DrawingsViewModel(GuiSession session)
    {
        _session = session;
        foreach (var (key, label, number) in ElementFields)
            ElementRows.Add(new DrawingFieldRow(key, label, number, ElementChanged));
        foreach (var side in new[] { "front", "back" })
        {
            var rows = side == "front" ? FrontRows : BackRows;
            foreach (var (key, label, number) in SurfaceFields)
                rows.Add(new DrawingFieldRow(side + "." + key, label, number, ElementChanged));
        }

        // Settings: this lens's own, else the shop's defaults, else the standard ones.
        string? own = SettingsPath;
        if (own != null && File.Exists(own)) LoadSettings(own, quiet: true);
        else if (File.Exists(DefaultsPath)) LoadSettings(DefaultsPath, quiet: true, asDefaults: true);
        else LoadSettings(null, quiet: true);

        // Tolerances: the set the Tolerancing window last had, else a starting set.
        if (!string.IsNullOrWhiteSpace(session.ToleranceSet))
        {
            _tolerances = session.ToleranceSet;
            ToleranceSource = "the Tolerancing window's set";
        }
        else Generate();
        IsDirty = false;
    }

    // ── Sources ──

    public static string[] Grades => ToleranceDialogViewModel.Grades;
    [ObservableProperty] private int _gradeIndex = 1;
    [ObservableProperty] private string _toleranceSource = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isDirty;

    /// <summary>Where this lens's drawing settings are kept: beside it, as name.drawing.json; null for a lens not yet saved.</summary>
    public string? SettingsPath =>
        _session.FilePath is string f
            ? Path.Combine(Path.GetDirectoryName(f) ?? "", Path.GetFileNameWithoutExtension(f) + ".drawing.json")
            : null;

    /// <summary>The shop's defaults for every lens.</summary>
    public static string DefaultsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SynapseOptics", "LensHH-LT", "drawing-defaults.json");

    [RelayCommand]
    private void Generate()
    {
        try
        {
            _tolerances = ToleranceRun.Defaults(_session.System, _session.GlassCatalog, (ShopGrade)GradeIndex, new ToleranceSettings());
            ToleranceSource = $"{Grades[GradeIndex]} tolerances (generated)";
            Redraw();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = e.Message;
        }
    }

    public void LoadTolerances(string path)
    {
        _tolerances = File.ReadAllText(path);
        ToleranceSource = Path.GetFileName(path);
        Redraw();
    }

    // ── The sheet: every drawing ──

    public static string[] Standards { get; } = { "ISO 10110", "ASME Y14.18 (US)" };
    [ObservableProperty] private int _standardIndex;
    [ObservableProperty] private string _project = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _drawnBy = "";
    [ObservableProperty] private string _numberPrefix = "OPT-";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _imperfections = "";
    [ObservableProperty] private string _scratchDig = "";
    [ObservableProperty] private string _stressBirefringence = "";
    [ObservableProperty] private string _bubbles = "";
    [ObservableProperty] private string _inhomogeneity = "";
    [ObservableProperty] private string _edgeMargin = "";
    [ObservableProperty] private string _diameterTolerance = "";
    [ObservableProperty] private string _chamferMin = "";
    [ObservableProperty] private string _chamferMax = "";
    [ObservableProperty] private string _maximumPowerFringes = "";
    [ObservableProperty] private string _coating = "";
    [ObservableProperty] private string _coatingSpecs = "";
    [ObservableProperty] private string _generalNotes = "";
    [ObservableProperty] private string _defaultFringes = "";
    [ObservableProperty] private string _defaultIrregularity = "";
    [ObservableProperty] private string _defaultThickness = "";
    [ObservableProperty] private string _defaultTiltArcmin = "";
    [ObservableProperty] private string _defaultIndex = "";
    [ObservableProperty] private string _defaultAbbePercent = "";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && SheetProperties.Contains(e.PropertyName ?? "")) { IsDirty = true; Redraw(); }
    }

    private static readonly HashSet<string> SheetProperties = new()
    {
        nameof(StandardIndex), nameof(Project), nameof(Company), nameof(DrawnBy), nameof(NumberPrefix), nameof(Date),
        nameof(Imperfections), nameof(ScratchDig), nameof(StressBirefringence), nameof(Bubbles), nameof(Inhomogeneity),
        nameof(EdgeMargin), nameof(DiameterTolerance), nameof(ChamferMin), nameof(ChamferMax), nameof(MaximumPowerFringes),
        nameof(Coating), nameof(CoatingSpecs), nameof(GeneralNotes), nameof(DefaultFringes), nameof(DefaultIrregularity),
        nameof(DefaultThickness), nameof(DefaultTiltArcmin), nameof(DefaultIndex), nameof(DefaultAbbePercent),
    };

    // ── The elements ──

    /// <summary>"Sheet and title block" first, then each element's drawing.</summary>
    public ObservableCollection<string> Items { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetSelected), nameof(IsElementSelected))]
    private int _selectedIndex;

    public bool IsSheetSelected => SelectedIndex <= 0;
    public bool IsElementSelected => SelectedIndex > 0;

    public ObservableCollection<DrawingFieldRow> ElementRows { get; } = new();
    public ObservableCollection<DrawingFieldRow> FrontRows { get; } = new();
    public ObservableCollection<DrawingFieldRow> BackRows { get; } = new();
    [ObservableProperty] private string _elementNotes = "";
    [ObservableProperty] private string _frontHeading = "Front surface";
    [ObservableProperty] private string _backHeading = "Back surface";

    public ObservableCollection<string> Warnings { get; } = new();
    [ObservableProperty] private Bitmap? _preview;

    private static readonly (string Key, string Label, bool Number)[] ElementFields =
    {
        ("title", "Title", false),
        ("number", "Drawing number", false),
        ("description", "Description", false),
        ("glass", "Glass", false),
        ("ndTol", "Index nd ±", true),
        ("vdTol", "Abbe number νd ± %", true),
        ("ctMinus", "Centre thickness, minus (mm)", true),
        ("ctPlus", "Centre thickness, plus (mm)", true),
        ("diameter", "Edge diameter (mm)", true),
        ("diaMinus", "Diameter, minus (mm)", true),
        ("diaPlus", "Diameter, plus (mm)", true),
    };

    private static readonly (string Key, string Label, bool Number)[] SurfaceFields =
    {
        ("power", "Power (fringes)", true),
        ("radiusTol", "Radius ± (mm), in place of power", true),
        ("irr", "Irregularity (fringes)", true),
        ("rsi", "Symmetric irregularity (fringes)", true),
        ("tilt", "Tilt / wedge (arcmin)", true),
        ("decentre", "Decentre (mm)", true),
        ("coating", "Coating (none to remove)", false),
        ("ca", "Clear aperture Ø (mm)", true),
    };

    private string? SelectedKey =>
        SelectedIndex > 0 && SelectedIndex - 1 < _computed.Count
            ? _computed[SelectedIndex - 1].First.ToString(CultureInfo.InvariantCulture) : null;

    partial void OnSelectedIndexChanged(int value) => ShowSelected();

    partial void OnElementNotesChanged(string value)
    {
        if (_loading || SelectedKey is not string key) return;
        _notes[key] = value;
        IsDirty = true;
        Redraw();
    }

    private void ElementChanged()
    {
        if (_loading || SelectedKey is not string key) return;
        var own = _overrides.TryGetValue(key, out var d) ? d : _overrides[key] = new();
        foreach (var row in ElementRows.Concat(FrontRows).Concat(BackRows))
        {
            if (row.IsOverridden) own[row.Key] = row.Text.Trim();
            else own.Remove(row.Key);
        }
        IsDirty = true;
        Redraw();
    }

    // ── Drawing ──

    /// <summary>The settings as they now stand: the sheet's fields, and each element's changes.</summary>
    public DrawingSettings Settings(bool withElements = true)
    {
        double D(string text, double fallback) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        double? N(string text) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
        var defaults = new DrawingSettings();
        var specs = new Dictionary<string, string>();
        foreach (var line in CoatingSpecs.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Length > 0) specs[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        var settings = _base with
        {
            Standard = StandardIndex == 1 ? DrawingStandard.Asme : DrawingStandard.Iso10110,
            Project = Project.Trim(),
            Company = Company.Trim(),
            DrawnBy = DrawnBy.Trim(),
            NumberPrefix = NumberPrefix,
            Date = string.IsNullOrWhiteSpace(Date) ? null : Date.Trim(),
            Imperfections = Imperfections.Trim(),
            ScratchDig = ScratchDig.Trim(),
            StressBirefringence = StressBirefringence.Trim(),
            Bubbles = Bubbles.Trim(),
            Inhomogeneity = Inhomogeneity.Trim(),
            EdgeMargin = D(EdgeMargin, defaults.EdgeMargin),
            DiameterTolerance = D(DiameterTolerance, defaults.DiameterTolerance),
            ChamferMin = N(ChamferMin),
            ChamferMax = N(ChamferMax),
            MaximumPowerFringes = D(MaximumPowerFringes, defaults.MaximumPowerFringes),
            Coating = string.IsNullOrWhiteSpace(Coating) ? null : Coating.Trim(),
            CoatingSpecs = specs,
            Notes = Lines(GeneralNotes),
            DefaultFringes = D(DefaultFringes, defaults.DefaultFringes),
            DefaultIrregularity = D(DefaultIrregularity, defaults.DefaultIrregularity),
            DefaultThickness = D(DefaultThickness, defaults.DefaultThickness),
            DefaultTiltArcmin = D(DefaultTiltArcmin, defaults.DefaultTiltArcmin),
            DefaultIndex = D(DefaultIndex, defaults.DefaultIndex),
            DefaultAbbePercent = D(DefaultAbbePercent, defaults.DefaultAbbePercent),
            Elements = new Dictionary<string, ElementOverride>(),
        };
        if (!withElements) return settings;
        foreach (var key in _overrides.Keys.Union(_notes.Keys))
        {
            var own = _overrides.TryGetValue(key, out var d) ? d : new Dictionary<string, string>();
            string? S(string k) => own.TryGetValue(k, out var v) ? v : null;
            double? X(string k) => S(k) is string v ? N(v) : null;
            SurfaceOverride? Side(string side)
            {
                var o = new SurfaceOverride
                {
                    PowerFringes = X(side + ".power"), RadiusTolerance = X(side + ".radiusTol"),
                    IrregularityFringes = X(side + ".irr"), SymmetricIrregularityFringes = X(side + ".rsi"),
                    TiltArcmin = X(side + ".tilt"), DecentreMm = X(side + ".decentre"),
                    Coating = S(side + ".coating"), ClearAperture = X(side + ".ca"),
                };
                return o == new SurfaceOverride() ? null : o;
            }
            var element = new ElementOverride
            {
                Title = S("title"), Number = S("number"), Description = S("description"), Glass = S("glass"),
                NdTolerance = X("ndTol"), VdTolerancePercent = X("vdTol"),
                ThicknessMinus = X("ctMinus"), ThicknessPlus = X("ctPlus"),
                Diameter = X("diameter"), DiameterMinus = X("diaMinus"), DiameterPlus = X("diaPlus"),
                Notes = _notes.TryGetValue(key, out var n) ? Lines(n) : new List<string>(),
                Front = Side("front"), Back = Side("back"),
            };
            if (own.Count > 0 || element.Notes.Count > 0) settings.Elements[key] = element;
        }
        return settings;
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private void Redraw()
    {
        if (_loading || !_session.HasSystem || _tolerances.Length == 0) return;
        try
        {
            // What the tolerances give each element, and what goes on the drawing.
            _computed = DrawingRun.Draw(_session.System, _session.GlassCatalog, _tolerances, Settings(withElements: false)).Drawings;
            _result = DrawingRun.Draw(_session.System, _session.GlassCatalog, _tolerances, Settings());
            Status = _result.Mirrors.Count > 0 ? $"Mirrors (surface {string.Join(", ", _result.Mirrors)}) are not drawn." : "";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
        {
            Status = e.Message;
            return;
        }

        int keep = SelectedIndex;
        var names = new List<string> { "Sheet and title block" };
        for (int i = 0; i < _result.Drawings.Count; i++)
            names.Add($"{_result.Drawings[i].Number}   {_result.Drawings[i].Title}" + (_result.Drawings[i].Warnings.Count > 0 ? "  ⚠" : ""));
        if (!Items.SequenceEqual(names))
        {
            _loading = true;
            try
            {
                Items.Clear();
                foreach (var name in names) Items.Add(name);
                SelectedIndex = Math.Min(Math.Max(keep, 0), Items.Count - 1);
            }
            finally { _loading = false; }
        }
        ShowSelected(reload: false);
    }

    /// <summary>The selected element's fields, warnings and sheet; the first sheet for the title block.</summary>
    private void ShowSelected(bool reload = true)
    {
        if (_result == null) return;
        int i = Math.Max(0, SelectedIndex - 1);
        Warnings.Clear();
        Preview = null;
        if (i >= _result.Drawings.Count) return;
        var d = _result.Drawings[i];
        var c = i < _computed.Count ? _computed[i] : d;
        foreach (var w in d.Warnings) Warnings.Add(w);
        Preview = Render(SvgWriter.ToText(_result.Sheets[i]));

        if (!IsElementSelected) return;
        string key = d.First.ToString(CultureInfo.InvariantCulture);
        var own = _overrides.TryGetValue(key, out var o) ? o : new Dictionary<string, string>();
        var inv = CultureInfo.InvariantCulture;
        string F(double? v) => v is double x ? x.ToString("0.####", inv) : "—";
        var computed = new Dictionary<string, string>
        {
            ["title"] = c.Title, ["number"] = c.Number, ["description"] = c.Description, ["glass"] = c.Material.Name,
            ["ndTol"] = F(c.Material.NdTolerance), ["vdTol"] = F(c.Material.VdTolerancePercent),
            ["ctMinus"] = F(c.ThicknessMinus), ["ctPlus"] = F(c.ThicknessPlus),
            ["diameter"] = F(c.Diameter), ["diaMinus"] = F(c.DiameterMinus), ["diaPlus"] = F(c.DiameterPlus),
        };
        foreach (var (side, s) in new[] { ("front", c.Front), ("back", c.Back) })
        {
            computed[side + ".power"] = F(s.PowerFringes);
            computed[side + ".radiusTol"] = F(s.RadiusTolerance);
            computed[side + ".irr"] = F(s.IrregularityFringes);
            computed[side + ".rsi"] = F(s.SymmetricIrregularityFringes);
            computed[side + ".tilt"] = F(s.TiltArcmin);
            computed[side + ".decentre"] = F(s.DecentreMm);
            computed[side + ".coating"] = s.Coating ?? "—";
            computed[side + ".ca"] = F(s.ClearAperture);
        }
        FrontHeading = $"Front surface ({c.Front.Surface}), R {F(c.Front.Radius)}";
        BackHeading = $"Back surface ({c.Back.Surface}), R {F(c.Back.Radius)}";
        foreach (var row in ElementRows.Concat(FrontRows).Concat(BackRows))
        {
            row.Computed = computed.TryGetValue(row.Key, out var v) ? v : "";
            if (reload) row.Load(own.TryGetValue(row.Key, out var t) ? t : "");
        }
        if (reload)
        {
            _loading = true;
            try { ElementNotes = _notes.TryGetValue(key, out var n) ? n : ""; }
            finally { _loading = false; }
        }
    }

    private static Bitmap? Render(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);
        if (skSvg.Picture == null) return null;
        var bounds = skSvg.Picture.CullRect;
        float scale = 1600f / Math.Max(1f, bounds.Width);
        int w = (int)(bounds.Width * scale), h = (int)(bounds.Height * scale);
        using var bitmap = new SkiaSharp.SKBitmap(Math.Max(1, w), Math.Max(1, h));
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(skSvg.Picture);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    // ── Files ──

    /// <summary>Reads drawing settings into the window; null for the standard ones. Defaults carry no element changes or project.</summary>
    public void LoadSettings(string? path, bool quiet = false, bool asDefaults = false)
    {
        DrawingSettings s;
        try { s = DrawingRun.ReadSettings(path); }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or NotSupportedException)
        {
            Status = $"Could not read {Path.GetFileName(path)}: {e.Message}";
            return;
        }
        if (asDefaults) s = s with { Elements = new(), Project = "" };
        _base = s;
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.#####", inv);
        _loading = true;
        try
        {
            StandardIndex = s.Standard == DrawingStandard.Asme ? 1 : 0;
            Project = s.Project; Company = s.Company; DrawnBy = s.DrawnBy; NumberPrefix = s.NumberPrefix; Date = s.Date ?? "";
            Imperfections = s.Imperfections; ScratchDig = s.ScratchDig; StressBirefringence = s.StressBirefringence;
            Bubbles = s.Bubbles; Inhomogeneity = s.Inhomogeneity;
            EdgeMargin = F(s.EdgeMargin); DiameterTolerance = F(s.DiameterTolerance);
            ChamferMin = s.ChamferMin is double a ? F(a) : ""; ChamferMax = s.ChamferMax is double b ? F(b) : "";
            MaximumPowerFringes = F(s.MaximumPowerFringes);
            Coating = s.Coating ?? "";
            CoatingSpecs = string.Join("\n", s.CoatingSpecs.Select(kv => $"{kv.Key} = {kv.Value}"));
            GeneralNotes = string.Join("\n", s.Notes);
            DefaultFringes = F(s.DefaultFringes); DefaultIrregularity = F(s.DefaultIrregularity);
            DefaultThickness = F(s.DefaultThickness); DefaultTiltArcmin = F(s.DefaultTiltArcmin);
            DefaultIndex = F(s.DefaultIndex); DefaultAbbePercent = F(s.DefaultAbbePercent);

            _overrides.Clear();
            _notes.Clear();
            foreach (var (key, e) in s.Elements)
            {
                var d = new Dictionary<string, string>();
                void Put(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) d[k] = v; }
                void PutN(string k, double? v) { if (v is double x) d[k] = F(x); }
                Put("title", e.Title); Put("number", e.Number); Put("description", e.Description); Put("glass", e.Glass);
                PutN("ndTol", e.NdTolerance); PutN("vdTol", e.VdTolerancePercent);
                PutN("ctMinus", e.ThicknessMinus); PutN("ctPlus", e.ThicknessPlus);
                PutN("diameter", e.Diameter); PutN("diaMinus", e.DiameterMinus); PutN("diaPlus", e.DiameterPlus);
                foreach (var (side, o) in new[] { ("front", e.Front), ("back", e.Back) })
                {
                    if (o == null) continue;
                    PutN(side + ".power", o.PowerFringes); PutN(side + ".radiusTol", o.RadiusTolerance);
                    PutN(side + ".irr", o.IrregularityFringes); PutN(side + ".rsi", o.SymmetricIrregularityFringes);
                    PutN(side + ".tilt", o.TiltArcmin); PutN(side + ".decentre", o.DecentreMm);
                    Put(side + ".coating", o.Coating); PutN(side + ".ca", o.ClearAperture);
                }
                _overrides[key] = d;
                if (e.Notes.Count > 0) _notes[key] = string.Join("\n", e.Notes);
            }
        }
        finally { _loading = false; }
        if (!quiet) Status = $"Read {Path.GetFileName(path)}.";
        IsDirty = !quiet;
        ShowSelected();
        Redraw();
    }

    public void SaveSettings(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, Settings().ToJson());
        IsDirty = false;
        Status = $"Drawing settings saved to {path}.";
    }

    /// <summary>The shop's part - title block, classes, chamfers, coatings, notes - for every lens; not this lens's project or element changes.</summary>
    [RelayCommand]
    private void SaveDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultsPath)!);
        File.WriteAllText(DefaultsPath, (Settings(withElements: false) with { Project = "", Date = null }).ToJson());
        Status = "Saved as your defaults: a lens with no drawing settings of its own starts from them.";
    }

    public void ExportPdf(string path)
    {
        if (_result == null || _result.Sheets.Count == 0) { Status = "There is nothing to draw."; return; }
        PdfWriter.Write(path, _result.Sheets);
        Status = $"{_result.Sheets.Count} drawing{(_result.Sheets.Count == 1 ? "" : "s")} written to {path}.";
    }

    public void ExportFiles(string folder)
    {
        var r = DrawingRun.Write(_session.System, _session.GlassCatalog, _tolerances, Settings(), folder,
                                 DrawingFormats.Svg | DrawingFormats.Dxf);
        Status = $"{r.Files.Count} files written to {folder}.";
    }
}
