using System.Collections.Generic;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Data transfer object representing the complete state of a LensHH-LT session.
    /// Serialized as JSON with the .lhlt extension.
    /// </summary>
    public class LhltFile
    {
        public int FormatVersion { get; set; } = 1;
        public string Title { get; set; } = string.Empty;

        /// <summary>Free-form multi-line description (newline-joined).</summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>Designer / source attribution.</summary>
        public string Designer { get; set; } = string.Empty;

        // Optical system
        public LhltAperture Aperture { get; set; } = new LhltAperture();
        public FieldType FieldType { get; set; }
        public List<LhltSurface> Surfaces { get; set; } = new List<LhltSurface>();
        public List<LhltWavelength> Wavelengths { get; set; } = new List<LhltWavelength>();
        public List<LhltField> Fields { get; set; } = new List<LhltField>();
        public List<LhltPickup> Pickups { get; set; } = new List<LhltPickup>();
        public RayAimingMode RayAiming { get; set; }
        public bool IsAfocal { get; set; }
        /// <summary>Object-space telecentric (entrance pupil at infinity). Only meaningful with the
        /// Object Space NA aperture. Default false = conventional (non-telecentric).</summary>
        public bool TelecentricObjectSpace { get; set; }
        /// <summary>
        /// When true, the merit-function evaluator emits a stiff per-ray
        /// penalty for every vignetted ray (on- AND off-axis). Default false
        /// preserves the legacy "off-axis vignetting is free" behavior. See
        /// OpticalSystem.PenalizeVignetting for the runtime semantics.
        /// </summary>
        public bool PenalizeVignetting { get; set; }

        /// <summary>
        /// When true, per-field vignetting factors are auto-computed after each semi-diameter solve
        /// and applied as an entrance-pupil remap. The factors themselves are derived state and are
        /// NOT serialized — only this flag is; the factors are recomputed on load. See
        /// OpticalSystem.UseAutomaticVignettingFactors.
        /// </summary>
        public bool UseAutomaticVignettingFactors { get; set; }
        public List<string> GlassCatalogs { get; set; } = new List<string>();

        // Glass substitution settings
        public List<LhltGlassSubstitution>? GlassSubstitutions { get; set; }

        // Merit function
        public LhltMeritFunction? MeritFunction { get; set; }

        // Multi-configuration (MCE) is an advanced-edition feature — the shared .lhlt
        // format is single-config. The advanced edition persists configurations separately.
    }

    public class LhltAperture
    {
        public ApertureType Type { get; set; }
        public double Value { get; set; }
    }

    public class LhltSurface
    {
        public int Index { get; set; }
        public SurfaceType Type { get; set; }
        public string Comment { get; set; } = string.Empty;
        public double Radius { get; set; } = double.PositiveInfinity;
        public double Thickness { get; set; }
        public string Material { get; set; } = string.Empty;
        public double SemiDiameter { get; set; }
        public SemiDiameterMode SemiDiameterMode { get; set; }
        public double ClearAperturePercent { get; set; } = 100.0;
        public double Conic { get; set; }
        public bool IsStop { get; set; }

        // Aperture properties
        public double InnerRadius { get; set; }
        public double ObscurationRadius { get; set; }
        public double FloatingApertureRadius { get; set; }

        // Aspheric coefficients (only serialized if non-zero)
        public double[]? AsphericCoefficients { get; set; }

        // Variable flags
        public bool CurvatureVariable { get; set; }
        public bool ThicknessVariable { get; set; }
        public bool ConicVariable { get; set; }
        public bool[]? AsphericVariable { get; set; }
        // Aperture solve flags: SemiDiameter (Fixed surfaces) / ClearAperturePercent (Auto surfaces).
        public bool SemiDiameterVariable { get; set; }
        public bool ClearAperturePercentVariable { get; set; }

        /// <summary>OSLO CALLBACK 1 marginal-ray-height solve marker.</summary>
        public bool HasMarginalRaySolve { get; set; }

        // Variable bounds
        public double? CurvatureMin { get; set; }
        public double? CurvatureMax { get; set; }
        public double? ThicknessMin { get; set; }
        public double? ThicknessMax { get; set; }
        public double? ConicMin { get; set; }
        public double? ConicMax { get; set; }
        public double?[]? AsphericMin { get; set; }
        public double?[]? AsphericMax { get; set; }
        public double? SemiDiameterMin { get; set; }
        public double? SemiDiameterMax { get; set; }
        public double? ClearAperturePercentMin { get; set; }
        public double? ClearAperturePercentMax { get; set; }

        // Model glass (Nd/Vd/dPgF) — when enabled the refractive index is computed
        // from these three parameters instead of a catalog Material. Each can be a
        // Variable (bounds below) or a Pickup (carried in the pickups list).
        public bool ModelIndexEnabled { get; set; }
        public double ModelNd { get; set; }
        public double ModelVd { get; set; }
        public double ModelDPgF { get; set; }
        public bool ModelNdVariable { get; set; }
        public bool ModelVdVariable { get; set; }
        public bool ModelDPgFVariable { get; set; }
        public double? ModelNdMin { get; set; }
        public double? ModelNdMax { get; set; }
        public double? ModelVdMin { get; set; }
        public double? ModelVdMax { get; set; }
        public double? ModelDPgFMin { get; set; }
        public double? ModelDPgFMax { get; set; }

        // Paraxial (ideal thin lens) — only meaningful when Type == Paraxial.
        // FocalLength = the single shape parameter f (PositiveInfinity = zero power).
        // Can be a Variable (bounds below) or a Pickup (carried in the pickups list).
        public double FocalLength { get; set; } = double.PositiveInfinity;
        public bool FocalLengthVariable { get; set; }
        // Optimization bounds are on the power (diopters), not the focal length.
        public double? FocalPowerMin { get; set; }
        public double? FocalPowerMax { get; set; }

        // Generic indexed parameters for PRO surface types (Coordinate Break, …).
        // Null when unused (standard surfaces) to keep files clean. 0-based arrays;
        // the UI/ZEMAX are 1-based. See Surface.Parameters/Settings.
        public double[]? Parameters { get; set; }
        public int[]? Settings { get; set; }
        public bool[]? ParameterVariable { get; set; }
        public double?[]? ParameterMin { get; set; }
        public double?[]? ParameterMax { get; set; }
    }

    public class LhltWavelength
    {
        public double Value { get; set; }
        public double Weight { get; set; } = 1.0;
        public bool IsPrimary { get; set; }
    }

    public class LhltField
    {
        public double Y { get; set; }
        public double Weight { get; set; } = 1.0;
        public bool Variable { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
    }

    public class LhltPickup
    {
        public int TargetSurfaceIndex { get; set; }
        public PickupParameter Parameter { get; set; }
        public int SourceSurfaceIndex { get; set; }
        public int SourceConfigurationIndex { get; set; } = -1;
        public double ScaleFactor { get; set; } = 1.0;
        public double Offset { get; set; }
    }

    public class LhltMeritFunction
    {
        public List<LhltOperand> Operands { get; set; } = new List<LhltOperand>();
    }

    public class LhltOperand
    {
        public OperandType Type { get; set; }
        public double Target { get; set; }
        public double Weight { get; set; } = 1.0;
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public OperationCode OpCode { get; set; }
        public int ConfigurationNo { get; set; } = -1;

        public int SurfaceIndex { get; set; }
        public int WaveIndex { get; set; }
        public double Hx { get; set; }
        public double Hy { get; set; }
        public double Px { get; set; }
        public double Py { get; set; }

        public int Rings { get; set; }
        public int Arms { get; set; }
        public int GridSize { get; set; }

        public int OperandNo { get; set; }
        public int OperandNo2 { get; set; }
        public double Factor { get; set; }

        public int Surface1 { get; set; }
        public int Surface2 { get; set; }
    }

    public class LhltGlassSubstitution
    {
        public int SurfaceIndex { get; set; }
        public bool Substitute { get; set; }
        public string CatalogName { get; set; } = string.Empty;
    }

}
