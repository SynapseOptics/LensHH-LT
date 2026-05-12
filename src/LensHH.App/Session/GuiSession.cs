using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LensHH.Core.Activation;
using LensHH.Core.Configuration;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;

namespace LensHH.App.Session;

/// <summary>
/// Central session that owns the OpticalSystem. All editors communicate
/// through this session to prevent cascading event issues.
///
/// Pattern: editors call NotifySystemChanged() after modifying the system.
/// All listeners receive the SystemChanged event with a sender tag so they
/// can skip refreshing if they initiated the change.
/// </summary>
public class GuiSession
{
    private OpticalSystem _system;
    private GlassCatalogManager _glassCatalog;
    private string? _filePath;
    private MeritFunction? _meritFunction;

    public OpticalSystem System => _system;
    public GlassCatalogManager GlassCatalog => _glassCatalog;
    public string? FilePath => _filePath;
    public bool HasSystem => _system != null;

    /// <summary>
    /// Filename (without extension) of the current system, used to seed
    /// Save As / Export dialogs. Set to the base name on every Open /
    /// Import / Save and cleared on New.
    /// </summary>
    public string? CurrentFileName { get; private set; }

    /// <summary>
    /// True when the system has been modified since the last successful
    /// New / Open / Save. Drives the "save changes?" prompt on actions
    /// that would discard the system.
    /// </summary>
    public bool IsDirty { get; private set; }

    public MeritFunction MeritFunction
    {
        get => _meritFunction ??= new MeritFunction();
        set => _meritFunction = value;
    }

    public ConfigurationEditor? ConfigEditor { get; set; }

    public string Title => _system?.Title ?? "Untitled";
    public string WindowTitle
    {
        get
        {
            var dirty = IsDirty ? "*" : "";
            var name = _filePath != null
                ? Path.GetFileName(_filePath)
                : (CurrentFileName != null ? CurrentFileName + " (unsaved)" : "New System");
            return $"LensHH-LT — {dirty}{name}";
        }
    }

    /// <summary>
    /// Raised when the system is modified. The sender string identifies
    /// which editor made the change so it can skip its own refresh.
    /// </summary>
    public event Action<string>? SystemChanged;

    /// <summary>
    /// Raised when the file state (path / dirty flag) changes without a
    /// system mutation — e.g. after Save. Listeners that only care about
    /// the window title subscribe here in addition to SystemChanged.
    /// </summary>
    public event Action? FileStateChanged;

    /// <summary>True if any surface has a glass name that cannot be resolved from loaded catalogs.</summary>
    public bool HasUnresolvedGlass { get; private set; }
    /// <summary>Error message listing which surfaces have unresolved glass.</summary>
    public string UnresolvedGlassMessage { get; private set; } = "";

    /// <summary>Glass substitutions applied during the most recent
    /// import (raw numeric nd/V codes resolved against loaded
    /// catalogs). Empty if no substitutions were needed. The GUI
    /// inspects this after OpenFile to surface a summary message
    /// box. Cleared on the next file open.</summary>
    public IReadOnlyList<LensHH.App.GlassCatalog.GlassNumericResolver.Substitution> LastImportSubstitutions { get; private set; }
        = Array.Empty<LensHH.App.GlassCatalog.GlassNumericResolver.Substitution>();

    /// <summary>True if any system wavelength falls outside the declared
    /// dispersion range of at least one material on the system. Outside
    /// that range glass-index formulas extrapolate and silently produce
    /// garbage — analyses must be blocked, not just warned about.</summary>
    public bool HasOutOfRangeWavelengths { get; private set; }
    /// <summary>Error message listing the offending wavelength(s) and the material(s) whose range they fall outside.</summary>
    public string OutOfRangeWavelengthsMessage { get; private set; } = "";

    /// <summary>True if no system field is on-axis (Y = 0). Without an
    /// on-axis baseline the merit function has no reference for
    /// spherical aberration or paraxial focus — almost always a
    /// configuration mistake.</summary>
    public bool HasNoOnAxisField { get; private set; }
    /// <summary>Error message when no field is on-axis.</summary>
    public string NoOnAxisFieldMessage { get; private set; } = "";

    /// <summary>True if the system cannot be analyzed (no license, or any validation rule fails).</summary>
    public bool CannotCompute =>
        !IsLicensed || HasUnresolvedGlass || HasOutOfRangeWavelengths || HasNoOnAxisField;
    /// <summary>User-facing error message explaining why computation is blocked.</summary>
    public string CannotComputeMessage =>
        !IsLicensed ? "No active license." :
        HasUnresolvedGlass ? UnresolvedGlassMessage :
        HasOutOfRangeWavelengths ? OutOfRangeWavelengthsMessage :
        HasNoOnAxisField ? NoOnAxisFieldMessage : "";
    /// <summary>True if the software has an active license or trial.</summary>
    public bool IsLicensed => ActivationManager.IsActivated || TrialClock.IsTrialActive;

    public GuiSession()
    {
        _glassCatalog = new GlassCatalogManager();
        LoadGlassCatalogs();
        NewSystem();
    }

    public void NewSystem()
    {
        // Starter system: equiconvex N-BK7 lens, f ≈ 50 mm, EPD = 12.5 mm
        // (f/4), stop at the front surface, single d-line wavelength, single
        // on-axis field. Image distance is solved at the end by tracing a
        // marginal ray to its axis crossing (best-marginal-focus solve).
        const double R = 50.97;          // gives paraxial f = 50 with t = 4 and N-BK7
        const double lensThickness = 4.0;
        const double yMarginal = 12.5 / 2.0;
        const double waveUm = 0.5876;    // d-line

        _system = new OpticalSystem
        {
            Title = "New System",
            Aperture = new Aperture(ApertureType.EPD, 12.5),
            FieldType = FieldType.ObjectAngle
        };
        _system.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        _system.Surfaces.Add(new Surface
        {
            Index = 1,
            IsStop = true,
            Radius = R,
            Thickness = lensThickness,
            Material = "N-BK7",
            SemiDiameterMode = SemiDiameterMode.Auto
        });
        _system.Surfaces.Add(new Surface
        {
            Index = 2,
            Radius = -R,
            Thickness = 50.0,            // placeholder, refined below
            SemiDiameterMode = SemiDiameterMode.Auto
        });
        _system.Surfaces.Add(new Surface { Index = 3 });
        _system.Wavelengths.Add(new Wavelength(waveUm, 1.0) { IsPrimary = true });
        _system.Fields.Add(new Field(0, 1.0));

        // Best marginal focus: trace a real marginal ray (parallel to axis,
        // y = +EPD/2) through both lens surfaces and place the image plane
        // where the refracted ray crosses the axis. Falls back to a paraxial-
        // ish 47 mm if the trace can't be completed (e.g. catalog missing).
        try
        {
            var indices = _glassCatalog.BuildRefractiveIndexArray(_system, waveUm);
            var tracer = new RayTracer(_system, indices);
            var result = tracer.Trace(0, yMarginal, 0, 0, 1,
                startSurface: 1, endSurface: 2);
            if (result.Success && result.SurfaceRays.Count > 2)
            {
                var rayAtBack = result.SurfaceRays[2];
                if (Math.Abs(rayAtBack.M) > 1e-9)
                {
                    double tToAxis = -rayAtBack.Y / rayAtBack.M;
                    double zCrossing = rayAtBack.Z + tToAxis * rayAtBack.N;
                    if (zCrossing > 0 && zCrossing < 1000)
                        _system.Surfaces[2].Thickness = zCrossing;
                }
            }
        }
        catch
        {
            _system.Surfaces[2].Thickness = 47.0;
        }

        _filePath = null;
        _meritFunction = null;
        CurrentFileName = null;
        IsDirty = false;
        NotifySystemChanged("session", markDirty: false);
        FileStateChanged?.Invoke();
    }

    public void OpenFile(string path, string format = "lhlt")
    {
        // Reset import-state carried across opens.
        LastImportSubstitutions = Array.Empty<LensHH.App.GlassCatalog.GlassNumericResolver.Substitution>();

        bool isImport = false;
        switch (format.ToLowerInvariant())
        {
            case "lhlt":
                var result = LhltReader.Read(path);
                _system = result.System;
                _meritFunction = result.MeritFunction;
                _filePath = path;
                break;
            case "zmx":
                _system = ZmxReader.Read(path);
                _meritFunction = null;
                _filePath = null; // imported, not native
                isImport = true;
                break;
            case "codev":
                _system = CodeVReader.Read(path);
                _meritFunction = null;
                _filePath = null;
                isImport = true;
                break;
            case "oslo":
                _system = OsloReader.Read(path);
                _meritFunction = null;
                _filePath = null;
                isImport = true;
                break;
            case "optalix":
                _system = OptalixReader.Read(path, _glassCatalog);
                _meritFunction = null;
                _filePath = null;
                isImport = true;
                break;
            case "optiland":
                _system = OptilandReader.Read(path);
                _meritFunction = null;
                _filePath = null;
                isImport = true;
                break;
            default:
                throw new ArgumentException($"Unknown format: {format}");
        }

        // Foreign-format imports may carry numeric glass codes
        // (e.g. "620.364") instead of catalog names. Resolve those
        // against the loaded catalogs before validation runs, so
        // surfaces don't show as unresolved-glass when the engine
        // can actually find a near match.
        if (isImport && _system != null)
            ApplyGlassSubstitutionsOnImport();

        // Track the source name for all formats so Save As / Export can
        // suggest it. Imports keep _filePath null (no native round-trip
        // path) but still seed CurrentFileName from the imported file.
        CurrentFileName = Path.GetFileNameWithoutExtension(path);
        IsDirty = false;
        NotifySystemChanged("session", markDirty: false);
        FileStateChanged?.Invoke();
    }

    /// <summary>
    /// Walk every surface, attempt glass-name resolution against the
    /// loaded catalogs. For each surface whose material doesn't
    /// resolve directly, ask GlassNumericResolver to interpret it as
    /// a numeric nd/V code and find the nearest catalog match. The
    /// surface's Material is rewritten to the matched name and the
    /// substitution is recorded for the GUI to display.
    /// </summary>
    private void ApplyGlassSubstitutionsOnImport()
    {
        var subs = new List<LensHH.App.GlassCatalog.GlassNumericResolver.Substitution>();
        var preferred = _system!.GlassCatalogs.Count > 0 ? _system.GlassCatalogs : null;
        for (int i = 0; i < _system.Surfaces.Count; i++)
        {
            var s = _system.Surfaces[i];
            var mat = s.Material;
            if (string.IsNullOrEmpty(mat)) continue;
            if (mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;

            var sub = LensHH.App.GlassCatalog.GlassNumericResolver.TryResolve(
                surfaceIndex: i,
                rawName: mat,
                catalogs: _glassCatalog,
                preferredCatalogs: preferred);
            if (sub == null) continue;

            s.Material = sub.Replacement;
            subs.Add(sub);
        }
        LastImportSubstitutions = subs;
    }

    public void SaveFile(string path, string format = "lhlt")
    {
        switch (format.ToLowerInvariant())
        {
            case "lhlt":
                LhltWriter.Write(_system, path, _meritFunction);
                _filePath = path;
                break;
            case "zmx":
                ZmxWriter.Write(_system, path);
                break;
            case "codev":
                CodeVWriter.Write(_system, path);
                break;
            case "oslo":
                OsloWriter.Write(_system, path);
                break;
            case "optalix":
                OptalixWriter.Write(_system, path);
                break;
            case "optiland":
                OptilandWriter.Write(_system, path);
                break;
            default:
                throw new ArgumentException($"Unknown format: {format}");
        }
        // Saving updates the suggested name even for foreign-format
        // exports — the user usually wants the next Save As to default
        // to whatever they last named the design.
        CurrentFileName = Path.GetFileNameWithoutExtension(path);
        // Only a native save clears dirty state; foreign exports keep
        // the dirty flag set since they aren't a round-trip save.
        if (format.Equals("lhlt", StringComparison.OrdinalIgnoreCase))
            IsDirty = false;
        FileStateChanged?.Invoke();
    }

    /// <summary>
    /// Call this after modifying the system from any editor.
    /// The sender string prevents the calling editor from re-refreshing.
    /// Pass markDirty: false from internal load/new flows that have just
    /// installed a freshly-loaded system and shouldn't be flagged dirty.
    /// </summary>
    public void NotifySystemChanged(string sender, bool markDirty = true)
    {
        // Apply pickup solves first (target = source * scale + offset)
        try { LensHH.Core.Analysis.PickupSolver.Solve(_system); }
        catch { /* ignore if system is incomplete */ }

        // Recompute AUTO semi-diameters on every system change
        try { LensHH.Core.Analysis.SemiDiameterSolver.Solve(_system, _glassCatalog); }
        catch { /* ignore if system is incomplete */ }

        // Run the unified engine validator and route each error code
        // back to the matching banner property. Order in the validator
        // matters: glass first, then wavelength range, then on-axis
        // field — so one root cause doesn't masquerade as several.
        RunSystemValidator();

        if (markDirty) IsDirty = true;

        SystemChanged?.Invoke(sender);
    }

    private void RunSystemValidator()
    {
        HasUnresolvedGlass = false; UnresolvedGlassMessage = "";
        HasOutOfRangeWavelengths = false; OutOfRangeWavelengthsMessage = "";
        HasNoOnAxisField = false; NoOnAxisFieldMessage = "";

        var errors = LensHH.Core.Validation.SystemValidator.Validate(_system, _glassCatalog);
        foreach (var e in errors)
        {
            switch (e.Code)
            {
                case "MissingGlass":
                    HasUnresolvedGlass = true; UnresolvedGlassMessage = e.Message; break;
                case "WavelengthOutOfRange":
                    HasOutOfRangeWavelengths = true; OutOfRangeWavelengthsMessage = e.Message; break;
                case "NoOnAxisField":
                    HasNoOnAxisField = true; NoOnAxisFieldMessage = e.Message; break;
            }
        }
    }

    private void LoadGlassCatalogs()
    {
        // Search for glass catalogs in multiple locations, in priority order:
        // 1. LENSHH_CATALOGS env var — set by the Linux AppImage AppRun, and
        //    also useful for ad-hoc testing on any platform.
        // 2. catalogs/Glass next to the exe (Windows installer layout).
        // 3. ../catalogs/Glass (alt Windows layout, exe in subfolder).
        // 4. Dev layout: walk up from bin/Debug/net8.0 to the repo root.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var envCatalogs = Environment.GetEnvironmentVariable("LENSHH_CATALOGS");

        var searchPaths = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(envCatalogs))
        {
            // The env var may point at either the parent catalogs/ folder
            // (AppImage convention) or directly at catalogs/Glass/.
            searchPaths.Add(Path.Combine(envCatalogs, "Glass"));
            searchPaths.Add(envCatalogs);
        }
        searchPaths.Add(Path.Combine(baseDir, "catalogs", "Glass"));
        searchPaths.Add(Path.Combine(baseDir, "..", "catalogs", "Glass"));
        searchPaths.Add(Path.Combine(baseDir, "catalogs"));
        // Dev: bin/Debug/net8.0 → src/LensHH.App → src → LensHH-LT → catalogs/Glass
        searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "Glass"));
        searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs"));
        // AppImage layout: bin/LensHH.App → ../share/lenshh-lt/catalogs/Glass
        searchPaths.Add(Path.Combine(baseDir, "..", "share", "lenshh-lt", "catalogs", "Glass"));

        foreach (var path in searchPaths)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                _glassCatalog.LoadCatalogsFromFolder(full);
                break;
            }
        }
    }

    /// <summary>
    /// Snapshot all variable values (curvature, thickness, conic, aspheric)
    /// so they can be restored if the user cancels optimization.
    /// </summary>
    public List<(int surfIdx, double curv, double thick, double conic, double[] asph)> SnapshotVariableValues()
    {
        var snap = new List<(int, double, double, double, double[])>();
        foreach (var s in _system.Surfaces)
            snap.Add((s.Index, s.Curvature, s.Thickness, s.Conic,
                (double[])s.AsphericCoefficients.Clone()));
        return snap;
    }

    /// <summary>Restore variable values from a previous snapshot.</summary>
    public void RestoreVariableValues(List<(int surfIdx, double curv, double thick, double conic, double[] asph)> snapshot)
    {
        foreach (var (surfIdx, curv, thick, conic, asph) in snapshot)
        {
            if (surfIdx < _system.Surfaces.Count)
            {
                var s = _system.Surfaces[surfIdx];
                s.Curvature = curv;
                s.Thickness = thick;
                s.Conic = conic;
                Array.Copy(asph, s.AsphericCoefficients, Math.Min(asph.Length, s.AsphericCoefficients.Length));
            }
        }
    }

    private static readonly JsonSerializerOptions _snapshotJsonOptions = new()
    {
        // Skip only true nulls, NOT type-default values. The earlier
        // WhenWritingDefault setting dropped any property equal to its
        // CLR default — so a merit operand explicitly set to Weight=0
        // would round-trip back to Weight=1 on Cancel/Revert, silently
        // re-enabling operands the user had disabled.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Full system snapshot via JSON round-trip. Use for operations that
    /// add/remove surfaces (e.g. split element) where variable-only
    /// snapshots are insufficient.
    /// </summary>
    public string SnapshotSystem()
    {
        var file = LhltWriter.ToLhltFile(_system, _meritFunction, ConfigEditor);
        return JsonSerializer.Serialize(file, _snapshotJsonOptions);
    }

    /// <summary>Restore entire system from a JSON snapshot.</summary>
    public void RestoreSystemSnapshot(string json)
    {
        var file = JsonSerializer.Deserialize<LhltFile>(json, _snapshotJsonOptions);
        if (file == null) return;
        var result = LhltReader.FromLhltFile(file);
        _system = result.System;
        _meritFunction = result.MeritFunction;
        ConfigEditor = result.ConfigEditor;
    }

}
