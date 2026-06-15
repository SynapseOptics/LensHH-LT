using System;
using LensHH.Core.Activation;
using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.Optimization;
using LensHH.Core.RayTrace;

namespace LensHH.API
{
    /// <summary>
    /// License and trial status.
    /// </summary>
    public interface ILicenseStatus
    {
        bool IsActivated { get; }
        bool IsTrialActive { get; }
        bool IsTrialExpired { get; }
        int TrialDaysRemaining { get; }
        int LicenseDaysUntilExpiry { get; }
        string MachineId { get; }

        /// <summary>
        /// Load existing license or start/continue trial.
        /// Call this before any computation. Returns true if engine is activated.
        /// </summary>
        bool Initialize();

        /// <summary>
        /// Activate with a license key (requires internet).
        /// Returns null on success, error message on failure.
        /// </summary>
        string Activate(string licenseKey);

        /// <summary>
        /// Activate from an offline token file.
        /// Returns null on success, error message on failure.
        /// </summary>
        string ActivateOffline(string tokenFilePath);
    }

    /// <summary>
    /// File I/O: load, save, import, export optical systems.
    /// </summary>
    public interface IFileIO
    {
        void Load(string filePath);
        void Save();
        void SaveAs(string filePath);
        string? CurrentFilePath { get; }

        void ImportZemax(string filePath);
        void ImportCodeV(string filePath);
        void ImportOslo(string filePath);
        void ImportOptalix(string filePath);
        void ImportOptiland(string filePath);

        void ExportZemax(string filePath);
        void ExportCodeV(string filePath);
        void ExportOslo(string filePath);
        void ExportOptalix(string filePath);
        void ExportOptiland(string filePath);
    }

    /// <summary>
    /// System editing: modify surfaces, wavelengths, fields, aperture.
    /// </summary>
    public interface ISystemEditor
    {
        OpticalSystem System { get; set; }
        bool HasSystem { get; }

        void NewSystem();
        void SetTitle(string title);
        void SetAperture(ApertureType type, double value);
        void SetFieldType(FieldType fieldType);
        void SetRayAiming(RayAimingMode mode);
        void SetAfocal(bool afocal);
        void SetPenalizeVignetting(bool penalize);

        int AddSurface(double radius = double.PositiveInfinity, double thickness = 0,
            string material = "", double semiDiameter = 0);
        void InsertSurface(int index, double radius = double.PositiveInfinity,
            double thickness = 0, string material = "", double semiDiameter = 0);
        void RemoveSurface(int index);
        void SetSurface(int index, double? radius = null, double? thickness = null,
            string? material = null, double? semiDiameter = null, double? conic = null,
            bool? isStop = null);

        void SetWavelengths(double[] wavelengthsUm, int primaryIndex = 0);
        void AddWavelength(double wavelengthUm, double weight = 1.0, bool isPrimary = false);
        void SetFields(double[] fieldValues);
        void AddField(double value, double weight = 1.0);

        void SetCurvatureVariable(int surfaceIndex, bool variable = true, double? min = null, double? max = null);
        void SetThicknessVariable(int surfaceIndex, bool variable = true, double? min = null, double? max = null);
        void SetConicVariable(int surfaceIndex, bool variable = true, double? min = null, double? max = null);
        void SetAsphericVariable(int surfaceIndex, int termIndex, bool variable = true, double? min = null, double? max = null);
        void SetCurvatureVariableRange(int surface1, int surface2, bool variable = true, bool skipInfiniteRadius = true);
        void SetThicknessVariableRange(int surface1, int surface2, bool variable = true);
        void SetThicknessConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all");
        void SetCurvatureConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all");
        void ClearAllVariables();

        void SetSemiDiameterMode(int surfaceIndex, SemiDiameterMode mode);
        void SetSurfaceAperture(int surfaceIndex, double? innerRadius = null, double? obscurationRadius = null);
        void ScaleLens(double factor);

        void AddPickup(int targetSurface, PickupParameter parameter, int sourceSurface, double scale = 1.0, double offset = 0.0);
        void RemovePickup(int index);
        System.Collections.Generic.List<Pickup> GetPickups();
        void ApplyPickups();
    }

    /// <summary>
    /// Optical analyses: compute aberrations, MTF, wavefront, etc.
    /// </summary>
    public interface IAnalysis
    {
        RayListingResult SingleRayTrace(int fieldIndex, double px, double py, int wavelengthIndex = -1);
        SpotDiagramResult SpotDiagram(int fieldIndex, int numRings = 6, int numArms = 12, int wavelengthIndex = -1);
        OpdFanResult OpdFan(int fieldIndex, int numPoints = 64);
        RayFanResult RayFan(int fieldIndex, int numPoints = 64);
        PupilAberrationResult PupilAberrationFan(int fieldIndex, int numPoints = 40);
        MtfResult FftMtf(int fieldIndex, int wavelengthIndex, int gridSize = 64);
        MtfResult FftMtfPolychromatic(int fieldIndex, int gridSize = 64);
        MtfVsFieldMultiFreqResult FftMtfVsField(double[] frequencies, int wavelengthIndex = -1,
            int gridSize = 256, int numFieldPoints = 200, bool polychromatic = false);
        PsfResult FftPsf(int fieldIndex, int wavelengthIndex = -1, int gridSize = 64);
        WavefrontResult WavefrontMap(int fieldIndex, int wavelengthIndex, int gridSize = 64);
        ChromaticFocalShiftResult ChromaticFocalShift(int numPoints = 50);
        LongitudinalAberrationResult LongitudinalAberration(int numZones = 32);
        SeidelResult Seidel();
        LateralColorResult LateralColor(int numFieldPoints = 20);
        FieldCurvatureResult FieldCurvature(int numFieldPoints = 20);
        DistortionResult Distortion(int numFieldPoints = 100);
        RelativeIlluminationResult RelativeIllumination(int numFieldPoints = 20, int numPupilRays = 36);
        SystemLayoutResult SystemLayout(int numRays = 5, bool startFromSurface1 = false, int wavelengthIndex = -1);
        ParaxialResult ParaxialData();
        SystemDataResult SystemData();
        ZernikeResult ZernikeStandard(int fieldIndex, int numTerms = 16);
        ZernikeResult ZernikeFringe(int fieldIndex, int numTerms = 16);
    }

    /// <summary>
    /// Merit function definition and optimization.
    /// </summary>
    public interface IOptimization
    {
        MeritFunction? MeritFunction { get; set; }
        void NewMeritFunction();
        void AddOperand(OperandType type, double target = 0, double weight = 1.0,
            double hx = 0, double hy = 0, double px = 0, double py = 0,
            int waveIndex = -1, int surfaceIndex = 0,
            int rings = 3, int arms = 6, int gridSize = 12);
        void ClearMeritFunction();

        /// <summary>
        /// Save the current merit function's operand settings to an .mft file.
        /// Only settings are written — no evaluated values.
        /// </summary>
        void SaveMeritFunctionTable(string filePath);

        /// <summary>
        /// Load a merit function from an .mft file, replacing the current merit
        /// function. Surface indices past the end of the current optical system
        /// are auto-clamped to the last valid surface.
        /// </summary>
        void LoadMeritFunctionTable(string filePath);

        double EvaluateMerit(bool parallel = true);
        OptimizationResult Optimize(bool parallel = true, System.Threading.CancellationToken cancellationToken = default);
        SplitElementResult SplitElement(SplitElementSettings settings, System.Threading.CancellationToken cancellationToken = default);
        SpcSynthesisResult SpcSynthesis(SpcSynthesisSettings settings,
            System.Action<SpcSynthesisProgress>? onProgress = null,
            System.Threading.CancellationToken stopToken = default,
            System.Threading.CancellationToken skipPhaseToken = default);

        /// <summary>Run Global Search — many seeded restarts from the original
        /// design, returning a pool of distinct locally-optimal designs.</summary>
        GlobalSearchResult GlobalSearch(GlobalSearchSettings? settings = null,
            bool useNativeEngine = false, bool analyticDerivative = false,
            string[]? filteredCatalogPaths = null,
            System.Action<GlobalSearchProgress>? onProgress = null,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>Run the DE starting-design pipeline — Differential-Evolution seed search
        /// (GPU-resident when a CUDA device is present and <see cref="DePipelineSettings.UseGpu"/>
        /// is set — population fills the device — else CPU) with the focus+EFL conditioner, then
        /// Local-LM or Multistart-LM polish of the best candidates. Returns the pre-polish seed
        /// pool plus the polished candidates (best-first). The caller saves the pools.</summary>
        DePipelineResult DeStartingDesignPipeline(DePipelineSettings? settings = null,
            System.Action<GlobalSearchProgress>? onProgress = null,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>Polish a previously-saved DE seed set, skipping the DE search. Every system
        /// must match the loaded design's structure (surface count + merit operands) or this
        /// throws <see cref="System.ArgumentException"/> naming the offender. Same polish defaults
        /// and reporting as <see cref="DeStartingDesignPipeline"/>; result timing reflects no DE
        /// phase.</summary>
        DePipelineResult DePolishSavedSeeds(
            System.Collections.Generic.IReadOnlyList<LensHH.Core.Models.OpticalSystem> savedSystems,
            System.Collections.Generic.IReadOnlyList<string>? labels = null,
            DePipelineSettings? settings = null,
            System.Action<GlobalSearchProgress>? onProgress = null,
            System.Threading.CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// HTML rendering of analysis results (SVG plots in HTML pages).
    /// </summary>
    public interface IRendering
    {
        string RenderSpotDiagram(SpotDiagramResult[] results, string title = "");
        string RenderOpdFan(OpdFanResult[] results, string title = "");
        string RenderFftMtf(MtfResult[][] resultsByField, string title = "");
        string RenderFftMtfVsField(MtfVsFieldMultiFreqResult result, string title = "");
        string RenderFftPsf(PsfResult[] results, string title = "");
        string RenderWavefrontMap(WavefrontResult[] results, string title = "");
        string RenderSystemLayout(SystemLayoutResult layout, string title = "");
        string RenderLateralColor(LateralColorResult result, string title = "");
        string RenderFieldCurvature(FieldCurvatureResult result, string title = "");
        string RenderDistortion(DistortionResult result, string title = "");
        string RenderChromaticFocalShift(ChromaticFocalShiftResult result, string title = "");
        string RenderLongitudinalAberration(LongitudinalAberrationResult result, string title = "");
        string RenderRelativeIllumination(RelativeIlluminationResult result, string title = "");
        string RenderSeidel(SeidelResult result, string title = "");
    }

    /// <summary>
    /// Text export of analysis results (tab-delimited data).
    /// </summary>
    public interface ITextExport
    {
        string ExportSpotDiagramText(SpotDiagramResult result, string title = "");
        string ExportOpdFanText(OpdFanResult result, string title = "");
        string ExportRayFanText(RayFanResult result, string title = "");
        string ExportFftMtfText(MtfResult result, string title = "", double cutoffT = 0, double cutoffS = 0);
        string ExportFftMtfVsFieldText(MtfVsFieldMultiFreqResult result, string title = "");
        string ExportFftPsfText(PsfResult result, string title = "");
        string ExportWavefrontMapText(WavefrontResult result, string title = "");
        string ExportLateralColorText(LateralColorResult result, string title = "");
        string ExportFieldCurvatureText(FieldCurvatureResult result, string title = "");
        string ExportDistortionText(DistortionResult result, string title = "");
        string ExportChromaticFocalShiftText(ChromaticFocalShiftResult result, string title = "");
        string ExportLongitudinalAberrationText(LongitudinalAberrationResult result, string title = "");
        string ExportRelativeIlluminationText(RelativeIlluminationResult result, string title = "");
        string ExportSeidelText(SeidelResult result, string title = "");
    }
}
