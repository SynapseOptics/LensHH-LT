using System;
using System.IO;
using System.Linq;
using LensHH.Core.Activation;
using LensHH.Core.Analysis;
using LensHH.Core.Configuration;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.Optimization;
using LensHH.Core.RayTrace;

namespace LensHH.API
{
    /// <summary>
    /// Main entry point for the LensHH-LT API.
    /// Manages an optical system session with file I/O, analysis, and optimization.
    /// Implements segregated interfaces so consumers depend only on what they use.
    /// </summary>
    public class LensHHSession : IFileIO, ISystemEditor, IAnalysis, IOptimization, IRendering, ITextExport, ILicenseStatus
    {
        private OpticalSystem? _system;
        private GlassCatalogManager? _glassCatalog;
        private string? _currentFilePath;

        /// <summary>Current optical system. Throws if no system is loaded.</summary>
        public OpticalSystem System
        {
            get => _system ?? throw new InvalidOperationException("No optical system loaded.");
            set => _system = value;
        }

        /// <summary>True if an optical system is loaded.</summary>
        public bool HasSystem => _system != null;

        // ── ILicenseStatus ──────────────────────────────────────────────────────

        public bool IsActivated => ActivationManager.IsActivated;
        public bool IsTrialActive => TrialClock.IsTrialActive;
        public bool IsTrialExpired => TrialClock.IsTrialExpired;
        public int TrialDaysRemaining => TrialClock.DaysRemaining;
        public int LicenseDaysUntilExpiry => ActivationManager.LicenseDaysUntilExpiry;
        public string MachineId => ActivationManager.GetMachineFingerprint();

        public bool Initialize() => ActivationManager.TryLoadExistingActivation();
        public string Activate(string licenseKey) => ActivationManager.Activate(licenseKey);
        public string ActivateOffline(string tokenFilePath) => ActivationManager.ActivateOffline(tokenFilePath);

        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Glass catalog manager with auto-loading from standard paths.</summary>
        public GlassCatalogManager GlassCatalog
        {
            get
            {
                if (_glassCatalog == null)
                {
                    _glassCatalog = new GlassCatalogManager();
                    // Search for glass catalogs in order of preference:
                    // 1. catalogs/Glass/ (new shared layout)
                    // 2. ../catalogs/Glass/ (install layout: app in LensHH-LT/ subfolder)
                    // 3. catalogs/ (legacy flat layout)
                    var searchPaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs", "Glass"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "catalogs", "Glass"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs"),
                        Path.Combine(Directory.GetCurrentDirectory(), "catalogs", "Glass"),
                        Path.Combine(Directory.GetCurrentDirectory(), "catalogs"),
                    };
                    foreach (var path in searchPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            _glassCatalog.LoadCatalogsFromFolder(path);
                            break;
                        }
                    }
                }
                return _glassCatalog;
            }
        }

        /// <summary>Load glass catalogs from a specific folder.</summary>
        public void LoadGlassCatalogs(string folderPath)
        {
            if (_glassCatalog == null) _glassCatalog = new GlassCatalogManager();
            _glassCatalog.LoadCatalogsFromFolder(folderPath);
        }

        /// <summary>Current merit function (null if none defined).</summary>
        public MeritFunction? MeritFunction { get; set; }

        /// <summary>Multi-configuration editor (null if single-config).</summary>
        public ConfigurationEditor? ConfigEditor { get; set; }

        /// <summary>Path to the currently loaded .lhlt file (null if imported/unsaved).</summary>
        public string? CurrentFilePath => _currentFilePath;

        // ─── System lifecycle ────────────────────────────────────────────

        /// <summary>Create a new optical system: a single equiconvex N-BK7
        /// lens with f = 50 mm, EPD = 12.5 mm (f/4), stop at the front
        /// surface, single d-line (587.6 nm) wavelength, single on-axis
        /// field, and the image distance set to the best marginal focus.
        /// </summary>
        public void NewSystem()
        {
            // Equiconvex N-BK7 thick lens for f ≈ 50 mm.
            // Solving 1/f = (n-1) [2/R − (n-1)·t / (n·R²)] for n=1.5168, t=4
            // → R ≈ 50.97 mm gives paraxial f = 50.0 mm exactly.
            const double R = 50.97;
            const double lensThickness = 4.0;
            const double yMarginal = 12.5 / 2.0;     // EPD / 2
            const double waveUm = 0.5876;            // d-line

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
                Thickness = 50.0,                    // placeholder, refined below
                SemiDiameterMode = SemiDiameterMode.Auto
            });
            _system.Surfaces.Add(new Surface { Index = 3 });   // image
            _system.Wavelengths.Add(new Wavelength(waveUm, 1.0, true));
            _system.Fields.Add(new Field(0, 1.0));

            // Solve image distance for best marginal focus by tracing a real
            // marginal ray (y = +EPD/2, parallel to axis) through the lens
            // and extending it to its axis crossing. The axis crossing is
            // closer than the paraxial focus for an undercorrected single
            // element — this places the image plane at the marginal focus
            // rather than the paraxial focus.
            try
            {
                var indices = GlassCatalog.BuildRefractiveIndexArray(_system, waveUm);
                var tracer = new RayTracer(_system, indices);
                var result = tracer.Trace(0, yMarginal, 0, 0, 1,
                    startSurface: 1, endSurface: 2);
                if (result.Success && result.SurfaceRays.Count > 2)
                {
                    var rayAtBack = result.SurfaceRays[2];
                    if (Math.Abs(rayAtBack.M) > 1e-9)
                    {
                        // Distance from surface 2's intersection point to the
                        // axis crossing along the refracted direction. Use Y
                        // and M (the Y direction cosine) to project; the
                        // resulting Z displacement is rayAtBack.Z + (-Y/M)·N.
                        double tToAxis = -rayAtBack.Y / rayAtBack.M;
                        double zCrossing = rayAtBack.Z + tToAxis * rayAtBack.N;
                        if (zCrossing > 0 && zCrossing < 1000)
                            _system.Surfaces[2].Thickness = zCrossing;
                    }
                }
            }
            catch
            {
                // Fall back to a paraxial-ish default if the trace fails for
                // any reason (e.g. glass catalog not loaded). Doesn't block
                // session creation.
                _system.Surfaces[2].Thickness = 47.0;
            }

            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
        }

        // ─── File I/O: Load/Save (native .lhlt format) ─────────────────

        /// <summary>Load system from a .lhlt file (includes merit function and config).</summary>
        public void Load(string filePath)
        {
            var result = LhltReader.Read(filePath);
            _system = result.System;
            MeritFunction = result.MeritFunction;
            ConfigEditor = result.ConfigEditor;
            _currentFilePath = filePath;
            UpdateSemiDiameters();
        }

        /// <summary>Save system to the current .lhlt file.</summary>
        public void Save()
        {
            if (_currentFilePath == null) throw new InvalidOperationException("No file path set. Use SaveAs.");
            SaveAs(_currentFilePath);
        }

        /// <summary>Save system to a .lhlt file.</summary>
        public void SaveAs(string filePath)
        {
            EnsureSystem();
            LhltWriter.Write(_system!, filePath, MeritFunction, ConfigEditor);
            _currentFilePath = filePath;
        }

        // ─── Import (external formats) ─────────────────────────────────

        /// <summary>Import from Zemax .zmx file.</summary>
        public void ImportZemax(string filePath)
        {
            _system = ZmxReader.Read(filePath);
            ClearMeritAndConfig();
            UpdateSemiDiameters();
        }

        /// <summary>Import from Code V .seq file.</summary>
        public void ImportCodeV(string filePath)
        {
            _system = CodeVReader.Read(filePath);
            ClearMeritAndConfig();
            UpdateSemiDiameters();
        }

        /// <summary>Import from OSLO .len file.</summary>
        public void ImportOslo(string filePath)
        {
            _system = OsloReader.Read(filePath);
            ClearMeritAndConfig();
            UpdateSemiDiameters();
        }

        /// <summary>Import from Optalix .otx file.</summary>
        public void ImportOptalix(string filePath)
        {
            _system = OptalixReader.Read(filePath, GlassCatalog);
            ClearMeritAndConfig();
            UpdateSemiDiameters();
        }

        /// <summary>Import from Optiland .json file.</summary>
        public void ImportOptiland(string filePath)
        {
            _system = OptilandReader.Read(filePath);
            ClearMeritAndConfig();
            UpdateSemiDiameters();
        }

        // ─── Export (external formats) ─────────────────────────────────

        /// <summary>Export to Zemax .zmx file.</summary>
        public void ExportZemax(string filePath) { EnsureSystem(); ZmxWriter.Write(_system!, filePath); }

        /// <summary>Export to Code V .seq file.</summary>
        public void ExportCodeV(string filePath) { EnsureSystem(); CodeVWriter.Write(_system!, filePath); }

        /// <summary>Export to OSLO .len file.</summary>
        public void ExportOslo(string filePath) { EnsureSystem(); OsloWriter.Write(_system!, filePath); }

        /// <summary>Export to Optalix .otx file.</summary>
        public void ExportOptalix(string filePath) { EnsureSystem(); OptalixWriter.Write(_system!, filePath); }

        /// <summary>Export to Optiland .json file.</summary>
        public void ExportOptiland(string filePath) { EnsureSystem(); OptilandWriter.Write(_system!, filePath); }

        // ─── System editing ─────────────────────────────────────────────

        /// <summary>Set system title.</summary>
        public void SetTitle(string title) { EnsureSystem(); _system!.Title = title; }

        /// <summary>Set aperture (EPD or F-number).</summary>
        public void SetAperture(ApertureType type, double value)
        {
            EnsureSystem();
            _system!.Aperture = new Aperture(type, value);
        }

        /// <summary>Set field type (ObjectAngle or ObjectHeight).</summary>
        public void SetFieldType(FieldType fieldType)
        {
            EnsureSystem();
            _system!.FieldType = fieldType;
        }

        /// <summary>Set ray aiming mode (Off, Real, or Robust).</summary>
        public void SetRayAiming(RayAimingMode mode)
        {
            EnsureSystem();
            _system!.RayAiming = mode;
        }

        /// <summary>Set afocal mode on or off.</summary>
        public void SetAfocal(bool afocal)
        {
            EnsureSystem();
            _system!.IsAfocal = afocal;
        }

        // ─── Surface editing ───────────────────────────────────────────

        /// <summary>Insert a new surface before the image surface. Returns the new surface index.</summary>
        public int AddSurface(double radius = double.PositiveInfinity, double thickness = 0,
            string material = "", double semiDiameter = 0)
        {
            EnsureSystem();
            int insertIdx = _system!.Surfaces.Count - 1; // before image
            var s = new Surface
            {
                Index = insertIdx,
                Radius = radius,
                Thickness = thickness,
                Material = material,
                SemiDiameter = semiDiameter,
                SemiDiameterMode = semiDiameter > 0 ? SemiDiameterMode.Fixed : SemiDiameterMode.Auto
            };
            _system.Surfaces.Insert(insertIdx, s);
            for (int i = 0; i < _system.Surfaces.Count; i++) _system.Surfaces[i].Index = i;
            return insertIdx;
        }

        /// <summary>Insert a surface at a specific index.</summary>
        public void InsertSurface(int index, double radius = double.PositiveInfinity,
            double thickness = 0, string material = "", double semiDiameter = 0)
        {
            EnsureSystem();
            if (index < 1 || index >= _system!.Surfaces.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var s = new Surface
            {
                Index = index,
                Radius = radius,
                Thickness = thickness,
                Material = material,
                SemiDiameter = semiDiameter,
                SemiDiameterMode = semiDiameter > 0 ? SemiDiameterMode.Fixed : SemiDiameterMode.Auto
            };

            // If caller used defaults and inserting inside a glass element,
            // copy the exit surface shape (standard sequential-design convention).
            if (radius == double.PositiveInfinity && string.IsNullOrEmpty(material))
            {
                var prevSurf = _system.Surfaces[index - 1];
                if (!string.IsNullOrEmpty(prevSurf.Material))
                {
                    var exitSurf = _system.Surfaces[index];
                    s.Radius = exitSurf.Radius;
                    s.Conic = exitSurf.Conic;
                    s.Type = exitSurf.Type;
                    if (exitSurf.AsphericCoefficients != null)
                        s.AsphericCoefficients = (double[])exitSurf.AsphericCoefficients.Clone();
                }
            }

            _system.Surfaces.Insert(index, s);
            for (int i = 0; i < _system.Surfaces.Count; i++) _system.Surfaces[i].Index = i;
        }

        /// <summary>Remove a surface by index.</summary>
        public void RemoveSurface(int index)
        {
            EnsureSystem();
            if (index < 1 || index >= _system!.Surfaces.Count - 1)
                throw new ArgumentOutOfRangeException(nameof(index), "Cannot remove object or image surface.");
            _system.Surfaces.RemoveAt(index);
            for (int i = 0; i < _system.Surfaces.Count; i++) _system.Surfaces[i].Index = i;
        }

        /// <summary>Edit a surface property.</summary>
        public void SetSurface(int index, double? radius = null, double? thickness = null,
            string? material = null, double? semiDiameter = null, double? conic = null,
            bool? isStop = null)
        {
            EnsureSystem();
            if (index < 0 || index >= _system!.Surfaces.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var s = _system.Surfaces[index];
            if (radius.HasValue) s.Radius = radius.Value;
            if (thickness.HasValue) s.Thickness = thickness.Value;
            if (material != null) s.Material = material;
            if (semiDiameter.HasValue)
            {
                s.SemiDiameter = semiDiameter.Value;
                s.SemiDiameterMode = semiDiameter.Value > 0 ? SemiDiameterMode.Fixed : SemiDiameterMode.Auto;
            }
            if (conic.HasValue) s.Conic = conic.Value;
            if (isStop.HasValue) s.IsStop = isStop.Value;
        }

        /// <summary>Set semi-diameter mode for a surface (Auto or Fixed).</summary>
        public void SetSemiDiameterMode(int surfaceIndex, SemiDiameterMode mode)
        {
            EnsureSystem();
            if (surfaceIndex < 0 || surfaceIndex >= _system!.Surfaces.Count)
                throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
            _system.Surfaces[surfaceIndex].SemiDiameterMode = mode;
        }

        /// <summary>Set surface aperture properties (inner radius and/or obscuration radius).</summary>
        public void SetSurfaceAperture(int surfaceIndex, double? innerRadius = null, double? obscurationRadius = null)
        {
            EnsureSystem();
            if (surfaceIndex < 0 || surfaceIndex >= _system!.Surfaces.Count)
                throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
            var s = _system.Surfaces[surfaceIndex];
            if (innerRadius.HasValue) s.InnerRadius = innerRadius.Value;
            if (obscurationRadius.HasValue) s.ObscurationRadius = obscurationRadius.Value;
        }

        // ─── Wavelength editing ────────────────────────────────────────

        /// <summary>Set wavelengths (replaces all). Values in micrometers.</summary>
        public void SetWavelengths(double[] wavelengthsUm, int primaryIndex = 0)
        {
            EnsureSystem();
            _system!.Wavelengths.Clear();
            for (int i = 0; i < wavelengthsUm.Length; i++)
                _system.Wavelengths.Add(new Wavelength(wavelengthsUm[i], 1.0, i == primaryIndex));
        }

        /// <summary>Add a wavelength. Value in micrometers.</summary>
        public void AddWavelength(double wavelengthUm, double weight = 1.0, bool isPrimary = false)
        {
            EnsureSystem();
            _system!.Wavelengths.Add(new Wavelength(wavelengthUm, weight, isPrimary));
        }

        // ─── Field editing ─────────────────────────────────────────────

        /// <summary>Set fields (replaces all). Values in degrees for ObjectAngle, mm for ObjectHeight.
        /// Throws ArgumentException if any value is NaN or infinity.</summary>
        public void SetFields(double[] fieldValues)
        {
            EnsureSystem();
            // Validate all entries before mutating state.
            foreach (double v in fieldValues)
                FieldValidation.ThrowIfInvalid(v, _system!.FieldType, nameof(fieldValues));
            _system!.Fields.Clear();
            foreach (double v in fieldValues)
                _system.Fields.Add(new Field(v, 1.0));
        }

        /// <summary>Add a field point. Throws ArgumentException if value is NaN or infinity.</summary>
        public void AddField(double value, double weight = 1.0)
        {
            EnsureSystem();
            FieldValidation.ThrowIfInvalid(value, _system!.FieldType, nameof(value));
            _system!.Fields.Add(new Field(value, weight));
        }

        // ─── Merit function editing ────────────────────────────────────

        /// <summary>Create a new empty merit function (replaces existing).</summary>
        public void NewMeritFunction()
        {
            MeritFunction = new MeritFunction();
        }

        /// <summary>Add an operand to the merit function.</summary>
        public void AddOperand(OperandType type, double target = 0, double weight = 1.0,
            double hx = 0, double hy = 0, double px = 0, double py = 0,
            int waveIndex = -1, int surfaceIndex = 0,
            int rings = 3, int arms = 6, int gridSize = 12)
        {
            if (MeritFunction == null) NewMeritFunction();
            MeritFunction!.AddOperand(new Operand
            {
                Type = type,
                Target = target,
                Weight = weight,
                Hx = hx, Hy = hy, Px = px, Py = py,
                WaveIndex = waveIndex,
                SurfaceIndex = surfaceIndex,
                Rings = rings,
                Arms = arms,
                GridSize = gridSize
            });
        }

        /// <summary>Clear all operands from the merit function.</summary>
        public void ClearMeritFunction()
        {
            MeritFunction = null;
        }

        /// <summary>Save the current merit function's settings to an .mft file.</summary>
        public void SaveMeritFunctionTable(string filePath)
        {
            if (MeritFunction == null)
                throw new System.InvalidOperationException("No merit function to save.");
            MeritFunctionTableIO.Save(MeritFunction, filePath);
        }

        /// <summary>
        /// Load a merit function from an .mft file. Surface indices past the
        /// end of the current optical system are clamped to the last valid
        /// surface.
        /// </summary>
        public void LoadMeritFunctionTable(string filePath)
        {
            int surfaceCount = _system?.Surfaces.Count ?? 0;
            MeritFunction = MeritFunctionTableIO.Load(filePath, surfaceCount);
        }

        // ─── Variables ─────────────────────────────────────────────────

        /// <summary>Set a surface curvature as variable for optimization.</summary>
        public void SetCurvatureVariable(int surfaceIndex, bool variable = true,
            double? min = null, double? max = null)
        {
            EnsureSystem();
            var s = _system!.Surfaces[surfaceIndex];
            s.CurvatureVariable = variable;
            s.CurvatureMin = min;
            s.CurvatureMax = max;
        }

        /// <summary>Set a surface thickness as variable for optimization.</summary>
        public void SetThicknessVariable(int surfaceIndex, bool variable = true,
            double? min = null, double? max = null)
        {
            EnsureSystem();
            var s = _system!.Surfaces[surfaceIndex];
            s.ThicknessVariable = variable;
            s.ThicknessMin = min;
            s.ThicknessMax = max;
        }

        /// <summary>Set a surface conic constant as variable for optimization.</summary>
        public void SetConicVariable(int surfaceIndex, bool variable = true,
            double? min = null, double? max = null)
        {
            EnsureSystem();
            var s = _system!.Surfaces[surfaceIndex];
            s.ConicVariable = variable;
            s.ConicMin = min;
            s.ConicMax = max;
        }

        /// <summary>Set an aspheric coefficient as variable for optimization.</summary>
        /// <param name="termIndex">Aspheric term index (0=A4, 1=A6, 2=A8, ...7=A18).</param>
        public void SetAsphericVariable(int surfaceIndex, int termIndex, bool variable = true,
            double? min = null, double? max = null)
        {
            EnsureSystem();
            var s = _system!.Surfaces[surfaceIndex];
            if (termIndex < 0 || termIndex >= s.AsphericVariable.Length)
                throw new ArgumentOutOfRangeException(nameof(termIndex));
            s.AsphericVariable[termIndex] = variable;
            s.AsphericMin[termIndex] = min;
            s.AsphericMax[termIndex] = max;
        }

        /// <summary>Set/clear curvature variables for a surface range.</summary>
        public void SetCurvatureVariableRange(int surface1, int surface2, bool variable = true, bool skipInfiniteRadius = true)
        {
            EnsureSystem();
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(_system!.Surfaces.Count - 1, surface2);
            for (int i = s1; i <= s2; i++)
            {
                var s = _system.Surfaces[i];
                if (variable && skipInfiniteRadius && double.IsInfinity(s.Radius))
                    continue;
                s.CurvatureVariable = variable;
            }
        }

        /// <summary>Set/clear thickness variables for a surface range.</summary>
        public void SetThicknessVariableRange(int surface1, int surface2, bool variable = true)
        {
            EnsureSystem();
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(_system!.Surfaces.Count - 1, surface2);
            for (int i = s1; i <= s2; i++)
                _system.Surfaces[i].ThicknessVariable = variable;
        }

        /// <summary>Set constraints on thickness variables in a surface range.</summary>
        public void SetThicknessConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all")
        {
            EnsureSystem();
            SetConstraintsRange(surface1, surface2, "thickness", min, max, filter);
        }

        /// <summary>Set constraints on curvature variables in a surface range.</summary>
        public void SetCurvatureConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all")
        {
            EnsureSystem();
            SetConstraintsRange(surface1, surface2, "curvature", min, max, filter);
        }

        private void SetConstraintsRange(int surface1, int surface2, string param, double? min, double? max, string filter)
        {
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(_system!.Surfaces.Count - 1, surface2);
            for (int i = s1; i <= s2; i++)
            {
                var surf = _system.Surfaces[i];
                if (filter == "glass" && string.IsNullOrEmpty(surf.Material)) continue;
                if (filter == "air" && !string.IsNullOrEmpty(surf.Material)) continue;

                if (param == "thickness" && surf.ThicknessVariable)
                {
                    surf.ThicknessMin = min;
                    surf.ThicknessMax = max;
                }
                else if (param == "curvature" && surf.CurvatureVariable)
                {
                    surf.CurvatureMin = min;
                    surf.CurvatureMax = max;
                }
            }
        }

        /// <summary>Clear all variables (set nothing as variable).</summary>
        public void ClearAllVariables()
        {
            EnsureSystem();
            foreach (var s in _system!.Surfaces)
            {
                s.CurvatureVariable = false;
                s.ThicknessVariable = false;
                s.ConicVariable = false;
                s.CurvatureMin = null; s.CurvatureMax = null;
                s.ThicknessMin = null; s.ThicknessMax = null;
                s.ConicMin = null; s.ConicMax = null;
                for (int i = 0; i < s.AsphericVariable.Length; i++)
                {
                    s.AsphericVariable[i] = false;
                    s.AsphericMin[i] = null;
                    s.AsphericMax[i] = null;
                }
            }
        }

        // ─── Glass Substitution ───────────────────────────────────────

        /// <summary>Set glass substitution for a surface.</summary>
        public void SetGlassSubstitution(int surfaceIndex, bool substitute, string catalogName = "")
        {
            EnsureSystem();
            var existing = _system!.GlassSubstitutions.Find(gs => gs.SurfaceIndex == surfaceIndex);
            if (existing != null)
            {
                existing.Substitute = substitute;
                if (!string.IsNullOrEmpty(catalogName)) existing.CatalogName = catalogName;
            }
            else
            {
                _system.GlassSubstitutions.Add(new GlassSubstitutionSetting
                {
                    SurfaceIndex = surfaceIndex,
                    Substitute = substitute,
                    CatalogName = catalogName
                });
            }
        }

        /// <summary>Set clear aperture percentage for a range of surfaces. Only affects Auto (non-fixed, non-stop) surfaces.</summary>
        public void SetClearAperturePercent(int surface1, int surface2, double caPercent)
        {
            EnsureSystem();
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(_system!.Surfaces.Count - 1, surface2);
            for (int i = s1; i <= s2; i++)
            {
                var surf = _system.Surfaces[i];
                if (surf.SemiDiameterMode == Core.Enums.SemiDiameterMode.Auto && !surf.IsStop)
                    surf.ClearAperturePercent = caPercent;
            }
        }

        /// <summary>Get glass substitution settings for all surfaces with glass.</summary>
        public System.Collections.Generic.List<GlassSubstitutionSetting> GetGlassSubstitutions()
        {
            EnsureSystem();
            return _system!.GlassSubstitutions;
        }

        /// <summary>Clear all glass substitution settings.</summary>
        public void ClearGlassSubstitutions()
        {
            EnsureSystem();
            _system!.GlassSubstitutions.Clear();
        }

        // ─── Analysis ──────────────────────────────────────────────────

        /// <summary>Trace a single ray and return per-surface data.</summary>
        public RayListingResult SingleRayTrace(int fieldIndex, double px, double py, int wavelengthIndex = -1)
        {
            ValidateGlass();
            double fieldY = System.Fields[fieldIndex].Y;
            return RayTraceListing.Trace(System, GlassCatalog, fieldY, px, py, wavelengthIndex);
        }

        /// <summary>
        /// Compute spot diagram for a field point.
        /// wavelengthIndex = -1 (default) = polychromatic; 0..N-1 = single wavelength.
        /// </summary>
        public SpotDiagramResult SpotDiagram(int fieldIndex, int numRings = 6, int numArms = 12, int wavelengthIndex = -1)
        {
            ValidateGlass();
            return LensHH.Core.Analysis.SpotDiagram.Compute(_system!, GlassCatalog,
                fieldIndex, numRings, numArms, wavelengthIndex);
        }

        /// <summary>Compute OPD fan for a field point.</summary>
        public OpdFanResult OpdFan(int fieldIndex, int numPoints = 64)
        {
            ValidateGlass();
            return LensHH.Core.Analysis.OpdFan.Compute(_system!, GlassCatalog, fieldIndex, numPoints);
        }

        /// <summary>Compute transverse ray fan for a field point.</summary>
        public RayFanResult RayFan(int fieldIndex, int numPoints = 64)
        {
            ValidateGlass();
            return TransverseRayFan.Compute(_system!, GlassCatalog, fieldIndex, numPoints);
        }

        /// <summary>Compute pupil aberration fans for a field point.</summary>
        public PupilAberrationResult PupilAberrationFan(int fieldIndex, int numPoints = 40)
        {
            ValidateGlass();
            return LensHH.Core.Analysis.PupilAberrationFan.Compute(_system!, GlassCatalog, fieldIndex, numPoints);
        }

        /// <summary>Compute FFT MTF vs spatial frequency for a field and wavelength.</summary>
        public MtfResult FftMtf(int fieldIndex, int wavelengthIndex, int gridSize = 64)
        {
            ValidateGlass();
            return FftMtfCalculator.ComputeVsFrequency(_system!, GlassCatalog, fieldIndex, wavelengthIndex, gridSize);
        }

        /// <summary>Compute polychromatic FFT MTF for a field.</summary>
        public MtfResult FftMtfPolychromatic(int fieldIndex, int gridSize = 64)
        {
            ValidateGlass();
            return FftMtfCalculator.ComputePolychromatic(_system!, GlassCatalog, fieldIndex, gridSize);
        }

        /// <summary>Compute wavefront map for a field and wavelength.</summary>
        public WavefrontResult WavefrontMap(int fieldIndex, int wavelengthIndex, int gridSize = 64)
        {
            ValidateGlass();
            return WavefrontMapCalculator.Compute(_system!, GlassCatalog, fieldIndex, wavelengthIndex, gridSize);
        }

        /// <summary>Compute chromatic focal shift.</summary>
        public ChromaticFocalShiftResult ChromaticFocalShift(int numPoints = 50)
        {
            ValidateGlass();
            return LensHH.Core.Analysis.ChromaticFocalShift.Compute(_system!, GlassCatalog, numPoints);
        }

        /// <summary>Compute Seidel (3rd order) aberration coefficients.</summary>
        public SeidelResult Seidel()
        {
            ValidateGlass();
            return LensHH.Core.Analysis.SeidelCalculator.Calculate(_system!, GlassCatalog);
        }

        /// <summary>Compute 2D system layout for visualization. wavelengthIndex = -1 uses the primary wavelength.</summary>
        public SystemLayoutResult SystemLayout(int numRays = 5, bool startFromSurface1 = false, int wavelengthIndex = -1)
        {
            ValidateGlass();
            return LensHH.Core.Analysis.SystemLayout.ComputeLayout(_system!, GlassCatalog, numRays,
                startFromSurface1: startFromSurface1, wavelengthIndex: wavelengthIndex);
        }

        /// <summary>Compute FFT MTF vs field at multiple spatial frequencies.</summary>
        public MtfVsFieldMultiFreqResult FftMtfVsField(double[] frequencies, int wavelengthIndex = -1,
            int gridSize = 256, int numFieldPoints = 200, bool polychromatic = false)
        {
            ValidateGlass();
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return FftMtfCalculator.ComputeVsFieldMultiFreq(_system!, GlassCatalog,
                frequencies, wIdx, gridSize, numFieldPoints, polychromatic);
        }

        /// <summary>Compute FFT PSF for a field and wavelength.</summary>
        public PsfResult FftPsf(int fieldIndex, int wavelengthIndex = -1, int gridSize = 64)
        {
            ValidateGlass();
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return FftPsfCalculator.Compute(_system!, GlassCatalog, fieldIndex, wIdx, gridSize);
        }

        /// <summary>Compute lateral color (transverse chromatic aberration).</summary>
        public LateralColorResult LateralColor(int numFieldPoints = 20)
        {
            ValidateGlass();
            return LateralColorCalculator.Compute(_system!, GlassCatalog, numFieldPoints);
        }

        /// <summary>Compute field curvature (tangential, sagittal, medial focus vs field).</summary>
        public FieldCurvatureResult FieldCurvature(int numFieldPoints = 20)
        {
            ValidateGlass();
            return FieldCurvatureCalculator.Compute(_system!, GlassCatalog, numFieldPoints);
        }

        /// <summary>Compute distortion vs field.</summary>
        public DistortionResult Distortion(int numFieldPoints = 100)
        {
            ValidateGlass();
            return DistortionCalculator.Compute(_system!, GlassCatalog, numPoints: numFieldPoints);
        }

        /// <summary>Compute relative illumination vs field.</summary>
        public RelativeIlluminationResult RelativeIllumination(int numFieldPoints = 20, int numPupilRays = 36)
        {
            ValidateGlass();
            return RelativeIlluminationCalculator.Compute(_system!, GlassCatalog,
                numFieldPoints, numPupilRays);
        }

        /// <summary>Compute paraxial data (EFL, BFL, pupil positions, etc.).</summary>
        public ParaxialResult ParaxialData()
        {
            ValidateGlass();
            var indices = GlassCatalog.BuildRefractiveIndexArray(_system!,
                _system!.Wavelengths[_system.PrimaryWavelengthIndex].Value);
            var tracer = new ParaxialRayTracer(_system, indices);
            return tracer.Solve();
        }

        /// <summary>Compute system data: first-order properties for focal and afocal systems.</summary>
        public SystemDataResult SystemData()
        {
            ValidateGlass();
            return SystemDataCalculator.Calculate(_system!, GlassCatalog);
        }

        /// <summary>Compute FFT MTF through focus for a field and wavelength.</summary>
        public MtfThroughFocusResult FftMtfThroughFocus(int fieldIndex,
            double spatialFrequency = 50, int wavelengthIndex = -1,
            double focusRange = 0.1, int numSteps = 21, int gridSize = 64)
        {
            ValidateGlass();
            if (_system!.IsAfocal)
                throw new InvalidOperationException(
                    "FFT MTF through focus is not supported for afocal systems.");
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return FftMtfCalculator.ComputeThroughFocus(_system!, GlassCatalog,
                fieldIndex, spatialFrequency, wIdx, focusRange, numSteps, gridSize);
        }

        /// <summary>Compute polychromatic FFT MTF through focus for a field.</summary>
        public MtfThroughFocusResult FftMtfThroughFocusPolychromatic(int fieldIndex,
            double spatialFrequency = 50, double focusRange = 0.1, int numSteps = 21, int gridSize = 64)
        {
            ValidateGlass();
            if (_system!.IsAfocal)
                throw new InvalidOperationException(
                    "FFT MTF through focus is not supported for afocal systems.");
            return FftMtfCalculator.ComputeThroughFocusPolychromatic(_system!, GlassCatalog,
                fieldIndex, spatialFrequency, focusRange, numSteps, gridSize);
        }

        // ─── Kidger Geometric MTF (fast) ───────────────────────────────

        /// <summary>Compute fast geometric MTF using Kidger direct summation.</summary>
        public MtfResult GeometricMtfKidger(int fieldIndex, int wavelengthIndex = -1,
            int numRings = 30, double maxFrequency = 0, int numFreqPoints = 50,
            bool multiplyByDiffractionLimit = true)
        {
            ValidateGlass();
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return LensHH.Core.Analysis.GeometricMtfKidger.Compute(_system!, GlassCatalog,
                fieldIndex, wIdx, numRings, maxFrequency, numFreqPoints, multiplyByDiffractionLimit);
        }

        /// <summary>Compute fast geometric MTF vs field using Kidger method.</summary>
        public MtfVsFieldMultiFreqResult GeometricMtfKidgerVsField(double[] frequencies,
            int wavelengthIndex = -1, int numRings = 30, int numFieldPoints = 20,
            bool multiplyByDiffractionLimit = true)
        {
            ValidateGlass();
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return LensHH.Core.Analysis.GeometricMtfKidger.ComputeVsFieldMultiFreq(_system!, GlassCatalog,
                frequencies, wIdx, numRings, numFieldPoints, multiplyByDiffractionLimit);
        }

        /// <summary>Compute fast geometric MTF through focus using Kidger method.</summary>
        public MtfThroughFocusResult GeometricMtfKidgerThroughFocus(int fieldIndex,
            double spatialFrequency, int wavelengthIndex = -1,
            double focusRange = 0.1, int numSteps = 21, int numRings = 30,
            bool multiplyByDiffractionLimit = true)
        {
            ValidateGlass();
            if (_system!.IsAfocal)
                throw new InvalidOperationException(
                    "Geometric MTF through focus is not supported for afocal systems.");
            int wIdx = wavelengthIndex < 0 ? _system!.PrimaryWavelengthIndex : wavelengthIndex;
            return LensHH.Core.Analysis.GeometricMtfKidger.ComputeThroughFocus(_system!, GlassCatalog,
                fieldIndex, spatialFrequency, wIdx, focusRange, numSteps, numRings, multiplyByDiffractionLimit);
        }

        /// <summary>Compute Standard Zernike coefficients (Noll ordering).</summary>
        public ZernikeResult ZernikeStandard(int fieldIndex, int numTerms = 16)
        {
            ValidateGlass();
            int wIdx = _system!.PrimaryWavelengthIndex;
            return ZernikeCalculator.ComputeStandard(_system, GlassCatalog, fieldIndex, wIdx, numTerms);
        }

        /// <summary>Compute Fringe Zernike coefficients.</summary>
        public ZernikeResult ZernikeFringe(int fieldIndex, int numTerms = 16)
        {
            ValidateGlass();
            int wIdx = _system!.PrimaryWavelengthIndex;
            return ZernikeCalculator.ComputeFringe(_system, GlassCatalog, fieldIndex, wIdx, numTerms);
        }

        // ─── Optimization ──────────────────────────────────────────────

        /// <summary>Evaluate the current merit function. Returns RMS merit value.</summary>
        public double EvaluateMerit(bool parallel = true)
        {
            ValidateGlass();
            if (MeritFunction == null) throw new InvalidOperationException("No merit function defined.");
            var evaluator = new MeritFunctionEvaluator(_system!, GlassCatalog, ConfigEditor)
                { ParallelEvaluation = parallel };
            return evaluator.Evaluate(MeritFunction);
        }

        /// <summary>Run local optimization (Levenberg-Marquardt).</summary>
        public OptimizationResult Optimize(bool parallel = true, System.Threading.CancellationToken cancellationToken = default)
        {
            ValidateGlass();
            if (MeritFunction == null) throw new InvalidOperationException("No merit function defined.");
            var optimizer = new LocalOptimizer(_system!, MeritFunction, GlassCatalog, ConfigEditor)
                { ParallelEvaluation = parallel };
            return optimizer.Optimize(cancellationToken);
        }

        /// <summary>Run multistart constrained optimization.</summary>
        public MultistartResult MultistartOptimize(
            MultistartSettings? settings = null,
            string[]? filteredCatalogPaths = null,
            Action<MultistartProgress>? onProgress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            ValidateGlass();
            if (MeritFunction == null) throw new InvalidOperationException("No merit function defined.");
            var optimizer = new MultistartOptimizer(_system!, MeritFunction, GlassCatalog, ConfigEditor)
            {
                Settings = settings ?? new MultistartSettings(),
                OnProgress = onProgress,
                FilteredCatalogSearchPaths = filteredCatalogPaths ?? new string[0]
            };
            return optimizer.Optimize(cancellationToken);
        }

        /// <summary>Run split element synthesis.</summary>
        public SplitElementResult SplitElement(SplitElementSettings settings,
            System.Threading.CancellationToken cancellationToken = default)
        {
            ValidateGlass();
            if (MeritFunction == null) throw new InvalidOperationException("No merit function defined.");
            var service = new SplitElementService(_system!, MeritFunction, GlassCatalog, ConfigEditor)
            {
                Settings = settings
            };
            return service.Execute(cancellationToken);
        }

        /// <summary>Run Synthesis by Saddle Point Construction (SPC).</summary>
        public SpcSynthesisResult SpcSynthesis(SpcSynthesisSettings settings,
            Action<SpcSynthesisProgress>? onProgress = null,
            System.Threading.CancellationToken stopToken = default,
            System.Threading.CancellationToken skipPhaseToken = default)
        {
            ValidateGlass();
            if (MeritFunction == null) throw new InvalidOperationException("No merit function defined.");
            var service = new SpcSynthesisService(_system!, MeritFunction, GlassCatalog, ConfigEditor)
            {
                Settings = settings,
                OnProgress = onProgress
            };
            return service.Execute(stopToken, skipPhaseToken);
        }

        // ─── Rendering ─────────────────────────────────────────────────

        /// <summary>Render spot diagram as SVG string.</summary>
        public string RenderSpotDiagram(SpotDiagramResult[] results, string title = "")
        {
            return Rendering.SpotDiagramRenderer.RenderPage(results,
                results.Select((r, i) => $"F{r.FieldIndex + 1}").ToArray(), title);
        }

        /// <summary>Render OPD fan as HTML page.</summary>
        public string RenderOpdFan(OpdFanResult[] results, string title = "")
        {
            return Rendering.OpdFanRenderer.RenderPage(results,
                results.Select((r, i) => $"F{r.FieldIndex + 1}").ToArray(), title);
        }

        /// <summary>Render FFT MTF as HTML page. Results grouped by field (one array per field).</summary>
        public string RenderFftMtf(MtfResult[][] resultsByField, string title = "")
        {
            var labels = resultsByField.Select((r, i) => $"F{i + 1}").ToArray();
            return Rendering.FftMtfRenderer.RenderPage(resultsByField, labels, title);
        }

        /// <summary>Render 2D system layout as HTML page.</summary>
        public string RenderSystemLayout(SystemLayoutResult layout, string title = "")
        {
            return Rendering.SystemLayoutRenderer.RenderPage(layout, title);
        }

        /// <summary>Render wavefront map as HTML page.</summary>
        public string RenderWavefrontMap(WavefrontResult[] results, string title = "")
        {
            return Rendering.WavefrontMapRenderer.RenderPage(results,
                results.Select((r, i) => $"F{r.FieldIndex + 1} W{r.WavelengthIndex + 1}").ToArray(), title);
        }

        /// <summary>Render FFT MTF vs field as HTML page.</summary>
        public string RenderFftMtfVsField(MtfVsFieldMultiFreqResult result, string title = "")
        {
            return Rendering.MtfVsFieldRenderer.RenderPage(result, title);
        }

        /// <summary>Render FFT PSF as HTML page.</summary>
        public string RenderFftPsf(PsfResult[] results, string title = "")
        {
            var titles = results.Select((r, i) => $"F{r.FieldIndex + 1} W{r.WavelengthIndex + 1}").ToArray();
            return Rendering.FftPsfRenderer.RenderPage(results, titles, title);
        }

        /// <summary>Render lateral color as HTML page.</summary>
        public string RenderLateralColor(LateralColorResult result, string title = "")
        {
            EnsureSystem();
            double maxField = _system!.Fields.Max(f => Math.Abs(f.Y));
            string fieldUnit = _system.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            var wlLabels = _system.Wavelengths.Select(w => $"{w.Value:F4}um").ToArray();
            return Rendering.LateralColorRenderer.RenderPage(result, title,
                maxField, _system.Wavelengths.Count, wlLabels, fieldUnit: fieldUnit);
        }

        /// <summary>Render field curvature as HTML page.</summary>
        public string RenderFieldCurvature(FieldCurvatureResult result, string title = "")
        {
            return Rendering.FieldCurvatureRenderer.RenderPage(result, title);
        }

        /// <summary>Render distortion as HTML page.</summary>
        public string RenderDistortion(DistortionResult result, string title = "")
        {
            return Rendering.DistortionRenderer.RenderPage(result, title);
        }

        /// <summary>Render chromatic focal shift as HTML page.</summary>
        public string RenderChromaticFocalShift(ChromaticFocalShiftResult result, string title = "")
        {
            return Rendering.ChromaticFocalShiftRenderer.RenderPage(result, title);
        }

        /// <summary>Render relative illumination as HTML page.</summary>
        public string RenderRelativeIllumination(RelativeIlluminationResult result, string title = "")
        {
            return Rendering.RelativeIlluminationRenderer.RenderPage(result, title);
        }

        /// <summary>Render Seidel coefficients as HTML page.</summary>
        public string RenderSeidel(SeidelResult result, string title = "")
        {
            return Rendering.SeidelRenderer.RenderPage(result, title);
        }

        /// <summary>Render MTF through focus as HTML page.</summary>
        public string RenderMtfThroughFocus(MtfThroughFocusResult result, string title = "")
        {
            return Rendering.MtfThroughFocusRenderer.RenderPage(result, title);
        }

        // ─── Text export ───────────────────────────────────────────────

        /// <summary>Export MTF through focus as tab-delimited text.</summary>
        public string ExportMtfThroughFocusText(MtfThroughFocusResult result, string title = "")
        {
            return Rendering.TextExport.MtfThroughFocusTextExport.Export(result, title);
        }

        /// <summary>Export spot diagram as tab-delimited text.</summary>
        public string ExportSpotDiagramText(SpotDiagramResult result, string title = "")
        {
            EnsureSystem();
            var wls = _system!.Wavelengths.Select(w => w.Value).ToArray();
            return Rendering.TextExport.SpotDiagramTextExport.Export(result, title, wls);
        }

        /// <summary>Export OPD fan as tab-delimited text.</summary>
        public string ExportOpdFanText(OpdFanResult result, string title = "")
        {
            EnsureSystem();
            var wls = _system!.Wavelengths.Select(w => w.Value).ToArray();
            return Rendering.TextExport.OpdFanTextExport.Export(result, title, wls);
        }

        /// <summary>Export ray fan as tab-delimited text.</summary>
        public string ExportRayFanText(RayFanResult result, string title = "")
        {
            EnsureSystem();
            var wls = _system!.Wavelengths.Select(w => w.Value).ToArray();
            return Rendering.TextExport.RayFanTextExport.Export(result, title, wls);
        }

        /// <summary>Export FFT MTF as tab-delimited text.</summary>
        public string ExportFftMtfText(MtfResult result, string title = "",
            double cutoffT = 0, double cutoffS = 0)
        {
            EnsureSystem();
            return Rendering.TextExport.FftMtfTextExport.Export(result, title, cutoffT, cutoffS,
                isAfocal: _system!.IsAfocal);
        }

        /// <summary>Export FFT MTF vs field as tab-delimited text.</summary>
        public string ExportFftMtfVsFieldText(MtfVsFieldMultiFreqResult result, string title = "")
        {
            EnsureSystem();
            string fieldUnit = _system!.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            return Rendering.TextExport.MtfVsFieldTextExport.Export(result, title, fieldUnit,
                _system.IsAfocal);
        }

        /// <summary>Export lateral color as tab-delimited text.</summary>
        public string ExportLateralColorText(LateralColorResult result, string title = "")
        {
            EnsureSystem();
            var wls = _system!.Wavelengths.Select(w => w.Value).ToArray();
            string fieldUnit = _system.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            return Rendering.TextExport.LateralColorTextExport.Export(result, title, wls, fieldUnit);
        }

        /// <summary>Export field curvature as tab-delimited text.</summary>
        public string ExportFieldCurvatureText(FieldCurvatureResult result, string title = "")
        {
            EnsureSystem();
            string fieldUnit = _system!.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            return Rendering.TextExport.FieldCurvatureTextExport.Export(result, title, fieldUnit);
        }

        /// <summary>Export distortion as tab-delimited text.</summary>
        public string ExportDistortionText(DistortionResult result, string title = "")
        {
            EnsureSystem();
            string fieldUnit = _system!.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            return Rendering.TextExport.DistortionTextExport.Export(result, title, fieldUnit);
        }

        /// <summary>Export wavefront map as tab-delimited text.</summary>
        public string ExportWavefrontMapText(WavefrontResult result, string title = "")
        {
            return Rendering.TextExport.WavefrontMapTextExport.Export(result, title);
        }

        /// <summary>Export relative illumination as tab-delimited text.</summary>
        public string ExportRelativeIlluminationText(RelativeIlluminationResult result, string title = "")
        {
            EnsureSystem();
            string fieldUnit = _system!.FieldType == FieldType.ObjectHeight ? "mm" : "deg";
            return Rendering.TextExport.RelativeIlluminationTextExport.Export(result, title, fieldUnit);
        }

        /// <summary>Export chromatic focal shift as tab-delimited text.</summary>
        public string ExportChromaticFocalShiftText(ChromaticFocalShiftResult result, string title = "")
        {
            return Rendering.TextExport.ChromaticFocalShiftTextExport.Export(result, title);
        }

        /// <summary>Export FFT PSF as tab-delimited text.</summary>
        public string ExportFftPsfText(PsfResult result, string title = "")
        {
            return Rendering.TextExport.FftPsfTextExport.Export(result, title);
        }

        /// <summary>Export Seidel coefficients as tab-delimited text.</summary>
        public string ExportSeidelText(SeidelResult result, string title = "")
        {
            return Rendering.TextExport.SeidelTextExport.Export(result, title);
        }

        // ─── Scale ─────────────────────────────────────────────────────

        /// <summary>Scale the entire optical system by a factor. Scales radii, thicknesses,
        /// semi-diameters, aspheric coefficients, aperture, and field heights.</summary>
        public void ScaleLens(double factor)
        {
            EnsureSystem();
            if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor), "Scale factor must be positive.");
            if (factor == 1.0) return;

            double s = factor;
            foreach (var surf in _system!.Surfaces)
            {
                if (!double.IsInfinity(surf.Radius) && surf.Radius != 0) surf.Radius *= s;
                if (!double.IsInfinity(surf.Thickness) && !double.IsNaN(surf.Thickness)) surf.Thickness *= s;
                if (surf.SemiDiameterMode == SemiDiameterMode.Fixed && surf.SemiDiameter > 0) surf.SemiDiameter *= s;
                if (surf.InnerRadius > 0) surf.InnerRadius *= s;
                if (surf.ClapOuterRadius > 0) surf.ClapOuterRadius *= s;
                if (surf.ObscurationRadius > 0) surf.ObscurationRadius *= s;
                if (surf.FloatingApertureRadius > 0) surf.FloatingApertureRadius *= s;

                if (surf.AsphericCoefficients != null)
                {
                    for (int j = 0; j < surf.AsphericCoefficients.Length; j++)
                    {
                        if (surf.AsphericCoefficients[j] != 0)
                        {
                            int twoN = (j + 1) * 2;
                            surf.AsphericCoefficients[j] *= Math.Pow(s, 1.0 - twoN);
                        }
                    }
                }

                if (surf.ThicknessMin.HasValue) surf.ThicknessMin *= s;
                if (surf.ThicknessMax.HasValue) surf.ThicknessMax *= s;
                if (surf.CurvatureMin.HasValue) surf.CurvatureMin /= s;
                if (surf.CurvatureMax.HasValue) surf.CurvatureMax /= s;
            }

            if (_system.Aperture.Type == ApertureType.EPD) _system.Aperture.Value *= s;
            if (_system.FieldType == FieldType.ObjectHeight)
                foreach (var field in _system.Fields) field.Y *= s;
        }

        // ─── Pickup Solves ────────────────────────────────────────────

        /// <summary>Add a pickup solve. target = source * scale + offset.</summary>
        public void AddPickup(int targetSurface, PickupParameter parameter, int sourceSurface, double scale = 1.0, double offset = 0.0)
        {
            EnsureSystem();
            _system!.Pickups.Add(new Pickup
            {
                TargetSurfaceIndex = targetSurface,
                SourceSurfaceIndex = sourceSurface,
                Parameter = parameter,
                ScaleFactor = scale,
                Offset = offset
            });
            try { PickupSolver.Solve(_system); } catch { }
        }

        /// <summary>Remove a pickup solve by index.</summary>
        public void RemovePickup(int index)
        {
            EnsureSystem();
            if (index < 0 || index >= _system!.Pickups.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _system.Pickups.RemoveAt(index);
        }

        /// <summary>Get all pickup solves.</summary>
        public System.Collections.Generic.List<Pickup> GetPickups()
        {
            EnsureSystem();
            return _system!.Pickups;
        }

        /// <summary>Apply all pickup solves.</summary>
        public void ApplyPickups()
        {
            EnsureSystem();
            PickupSolver.Solve(_system!);
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private void EnsureSystem()
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
        }

        /// <summary>
        /// Run every system validation rule (glass resolution, wavelength
        /// range, on-axis field) and throw if any fails. Called by every
        /// analysis / optimization entry point so CLI, MCP, and direct
        /// API consumers all enforce the same gate the GUI does.
        /// </summary>
        private void ValidateGlass()
        {
            EnsureSystem();
            LensHH.Core.Validation.SystemValidator.ValidateOrThrow(_system!, GlassCatalog);
        }

        /// <summary>
        /// Returns the list of validation errors for the currently-loaded
        /// system (empty list = OK). Use this to pre-check before running
        /// long operations or to surface the same errors the GUI shows on
        /// its banner. CLI and MCP front-ends call this after Load to
        /// fail-fast instead of catching exceptions later.
        /// </summary>
        public global::System.Collections.Generic.IReadOnlyList<LensHH.Core.Validation.SystemValidator.ValidationError> Validate()
        {
            if (_system == null) return global::System.Array.Empty<LensHH.Core.Validation.SystemValidator.ValidationError>();
            return LensHH.Core.Validation.SystemValidator.Validate(_system, GlassCatalog);
        }

        /// <summary>
        /// Recompute AUTO semi-diameters by tracing marginal rays at all fields.
        /// Called automatically after load/import, or manually after editing surfaces.
        /// </summary>
        public void UpdateSemiDiameters()
        {
            if (_system != null)
            {
                try { SemiDiameterSolver.Solve(_system, GlassCatalog); }
                catch { /* ignore if system is incomplete */ }
            }
        }

        private void ClearMeritAndConfig()
        {
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
        }
    }
}
