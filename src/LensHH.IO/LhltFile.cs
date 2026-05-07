using System.Collections.Generic;
using LensHH.Core.Configuration;
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
        public List<string> GlassCatalogs { get; set; } = new List<string>();

        // Glass substitution settings
        public List<LhltGlassSubstitution>? GlassSubstitutions { get; set; }

        // Merit function
        public LhltMeritFunction? MeritFunction { get; set; }

        // Configuration editor
        public LhltConfigurationEditor? ConfigurationEditor { get; set; }
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

    public class LhltConfigurationEditor
    {
        public int ActiveConfiguration { get; set; }
        public List<LhltConfigOperand> Operands { get; set; } = new List<LhltConfigOperand>();
        public List<LhltConfigValues> Configurations { get; set; } = new List<LhltConfigValues>();
    }

    public class LhltConfigOperand
    {
        public ConfigOperandType Type { get; set; }
        public int SurfaceIndex { get; set; }
        public int AsphericTermIndex { get; set; }
    }

    public class LhltConfigValues
    {
        public double[] Values { get; set; } = System.Array.Empty<double>();
        public string?[] GlassValues { get; set; } = System.Array.Empty<string?>();
        public bool[]? VariableFlags { get; set; }
        public double?[]? MinValues { get; set; }
        public double?[]? MaxValues { get; set; }
    }
}
