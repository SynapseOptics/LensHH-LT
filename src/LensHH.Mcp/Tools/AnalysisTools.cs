using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class AnalysisTools
    {
        private readonly McpSession _session;
        public AnalysisTools(McpSession session) => _session = session;

        private string? CheckGlass() => _session.ValidateGlass();

        [McpServerTool, Description("Trace a single ray through the system. Returns per-surface X, Y, Z coordinates, direction cosines (L, M, N), surface normals, angle of incidence, path length, and OPL. fieldIndex and wavelengthIndex are 0-based. px and py are normalized pupil coordinates [-1, 1].")]
        public string SingleRayTrace(int fieldIndex = 0, double px = 0, double py = 0, int wavelengthIndex = -1)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            var glassMgr = _session.GlassCatalog;
            double fieldY = sys.Fields[fieldIndex].Y;
            var result = RayTraceListing.Trace(sys, glassMgr, fieldY, px, py, wavelengthIndex);

            if (!result.Success)
                return "Ray trace failed.";

            var sb = new StringBuilder();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectAngle ? "deg" : "mm";
            sb.AppendLine($"Field {fieldIndex + 1}: {result.FieldY} {fieldUnit}, Px={px}, Py={py}, Wave: {result.Wavelength:F6} um");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,5} {1,16} {2,16} {3,16} {4,14} {5,14} {6,14} {7,14} {8,14} {9,14} {10,10} {11,14} {12,14} {13,14} {14}",
                "Surf", "X", "Y", "Z", "L", "M", "N", "Ln", "Mn", "Nn", "AOI", "Path", "OPL", "Cumul OPL", "Comment"));

            for (int i = 0; i < result.Surfaces.Count; i++)
            {
                var s = result.Surfaces[i];
                // Compute AOI from previous surface direction and current normal
                string aoi = "";
                if (i > 0 && (s.Ln != 0 || s.Mn != 0 || s.Nn != 0))
                {
                    var prev = result.Surfaces[i - 1];
                    double dot = Math.Abs(prev.L * s.Ln + prev.M * s.Mn + prev.N * s.Nn);
                    if (dot > 1.0) dot = 1.0;
                    aoi = $"{Math.Acos(dot) * 180.0 / Math.PI:F6}";
                }

                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,5} {1,16:E10} {2,16:E10} {3,16:E10} {4,14:F10} {5,14:F10} {6,14:F10} {7,14:F10} {8,14:F10} {9,14:F10} {10,10} {11,14:F6} {12,14:F6} {13,14:F6} {14}",
                    s.SurfaceIndex, s.X, s.Y, s.Z, s.L, s.M, s.N, s.Ln, s.Mn, s.Nn, aoi, s.PathLength, s.OPL, s.CumulativeOPL, s.Vignetted ? "Vignetted" : ""));
            }

            return sb.ToString();
        }

        [McpServerTool, Description("Compute spot diagram for a field point. Returns RMS/GEO radius, centroid, and ray count. fieldIndex is 0-based. wavelengthIndex (-1 = polychromatic, 0..N-1 = single wavelength) picks the wavelength mode.")]
        public string SpotDiagram(int fieldIndex = 0,
            [Description("Wavelength index (0-based). -1 (default) = polychromatic.")] int wavelengthIndex = -1)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            var result = Core.Analysis.SpotDiagram.Compute(sys, _session.GlassCatalog, fieldIndex,
                numRings: 6, numArms: 12, wavelengthIndex: wavelengthIndex);

            var sb = new StringBuilder();
            sb.AppendLine($"Spot Diagram - Field {fieldIndex} (Y={sys.Fields[fieldIndex].Y})");
            sb.AppendLine($"  RMS Radius: {result.RmsRadius:E4} mm");
            sb.AppendLine($"  GEO Radius: {result.GeoRadius:E4} mm");
            sb.AppendLine($"  Centroid: ({result.CentroidX:E4}, {result.CentroidY:E4}) mm");
            sb.AppendLine($"  Chief Ray: ({result.ChiefRayX:E4}, {result.ChiefRayY:E4}) mm");
            sb.AppendLine($"  Total Rays: {result.Points.Count}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute transverse ray aberration fans (tangential and sagittal) for a field point. Returns max aberration and fan data.")]
        public string RayFan(int fieldIndex = 0, int numPoints = 32)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            var result = TransverseRayFan.Compute(sys, _session.GlassCatalog, fieldIndex, numPoints);

            var sb = new StringBuilder();
            sb.AppendLine($"Ray Fan - Field {fieldIndex} (Y={sys.Fields[fieldIndex].Y})");
            sb.AppendLine($"Max Aberration: {result.MaxAberration:E4} mm");
            sb.AppendLine();
            sb.AppendLine("Tangential (PY → EY):");
            sb.AppendLine($"{"PY",8}  {"EY (mm)",12}");

            var primary = result.TangentialFan.Where(p => p.WavelengthIndex == sys.PrimaryWavelengthIndex).ToList();
            foreach (var pt in primary)
                sb.AppendLine($"{pt.PupilCoordinate,8:F3}  {pt.Aberration,12:E4}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute pupil aberration fans (tangential and sagittal) for a field point. Shows how much real rays deviate from paraxial pupil coordinates, in percent. With ray aiming on, aberrations should be near zero.")]
        public string PupilAberrationFan(int fieldIndex = 0, int numPoints = 40)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            var result = Core.Analysis.PupilAberrationFan.Compute(sys, _session.GlassCatalog, fieldIndex, numPoints);

            var sb = new StringBuilder();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            sb.AppendLine($"Pupil Aberration Fan - Field {fieldIndex} (Y={sys.Fields[fieldIndex].Y} {fieldUnit})");
            sb.AppendLine($"Max Aberration: {result.MaxAberration:F4} %");
            sb.AppendLine();
            sb.AppendLine("Tangential (PY → Aberration %):");
            sb.AppendLine($"{"PY",8}  {"Aberr (%)",12}");

            var primary = result.TangentialFan.Where(p => p.WavelengthIndex == sys.PrimaryWavelengthIndex).ToList();
            foreach (var pt in primary)
                sb.AppendLine($"{pt.PupilCoordinate,8:F3}  {pt.Aberration,12:F4}");

            sb.AppendLine();
            sb.AppendLine("Sagittal (PX → Aberration %):");
            sb.AppendLine($"{"PX",8}  {"Aberr (%)",12}");

            var primarySag = result.SagittalFan.Where(p => p.WavelengthIndex == sys.PrimaryWavelengthIndex).ToList();
            foreach (var pt in primarySag)
                sb.AppendLine($"{pt.PupilCoordinate,8:F3}  {pt.Aberration,12:F4}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute Seidel aberration coefficients (S1-S5, CL, CT) per surface and totals.")]
        public string SeidelCoefficients()
        {
            var result = SeidelCalculator.Calculate(_session.System, _session.GlassCatalog);

            var sb = new StringBuilder();
            sb.AppendLine("Seidel Aberration Coefficients");
            sb.AppendLine($"{"Surf",5} {"S1",10} {"S2",10} {"S3",10} {"S4",10} {"S5",10} {"CL",10} {"CT",10}");

            foreach (var s in result.SurfaceData)
            {
                sb.AppendLine($"{s.SurfaceIndex,5} {s.S1,10:E3} {s.S2,10:E3} {s.S3,10:E3} " +
                              $"{s.S4,10:E3} {s.S5,10:E3} {s.CL,10:E3} {s.CT,10:E3}");
            }

            sb.AppendLine();
            sb.AppendLine("Totals:");
            sb.AppendLine($"  S1 (Spherical):   {result.S1:E4}");
            sb.AppendLine($"  S2 (Coma):        {result.S2:E4}");
            sb.AppendLine($"  S3 (Astigmatism): {result.S3:E4}");
            sb.AppendLine($"  S4 (Petzval):     {result.S4:E4}");
            sb.AppendLine($"  S5 (Distortion):  {result.S5:E4}");
            sb.AppendLine($"  CL (Long. Chrom): {result.CL:E4}");
            sb.AppendLine($"  CT (Trans. Chrom):{result.CT:E4}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute chromatic focal shift across the wavelength range.")]
        public string ChromaticFocalShift()
        {
            var result = Core.Analysis.ChromaticFocalShift.Compute(_session.System, _session.GlassCatalog);

            var sb = new StringBuilder();
            sb.AppendLine($"Chromatic Focal Shift (max: {result.MaxShift:E4} mm)");
            sb.AppendLine($"{"Wavelength",12} {"Shift (mm)",12} {"EFL (mm)",12}");

            int step = Math.Max(1, result.Points.Count / 10);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                sb.AppendLine($"{pt.Wavelength, 12:F6} {pt.FocalShift,12:E4} {pt.Efl,12:F4}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Compute OPD (optical path difference) ray fans in waves for a field point.")]
        public string OpdFanAnalysis(int fieldIndex = 0, int numPoints = 32)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            var result = Core.Analysis.OpdFan.Compute(sys, _session.GlassCatalog, fieldIndex, numPoints);

            var sb = new StringBuilder();
            sb.AppendLine($"OPD Fan - Field {fieldIndex} (max: {result.MaxOpd:F4} waves)");
            sb.AppendLine($"{"PY",8}  {"OPD (waves)",12}");

            var primary = result.TangentialFan.Where(p => p.WavelengthIndex == sys.PrimaryWavelengthIndex).ToList();
            foreach (var pt in primary)
                sb.AppendLine($"{pt.PupilCoordinate,8:F3}  {pt.Opd,12:F4}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute field curvature (tangential, sagittal, medial) and distortion across the field of view.")]
        public string FieldCurvatureAndDistortion()
        {
            var result = FieldCurvatureDistortion.Compute(_session.System, _session.GlassCatalog);

            var sb = new StringBuilder();
            sb.AppendLine("Field Curvature:");
            sb.AppendLine($"{"Field Y",10} {"Tang",10} {"Sag",10} {"Medial",10}");

            foreach (var pt in result.FieldCurvaturePoints)
                sb.AppendLine($"{pt.FieldY,10:F3} {pt.TangentialFocus,10:F4} {pt.SagittalFocus,10:F4} {pt.MedialFocus,10:F4}");

            sb.AppendLine();
            sb.AppendLine("Distortion:");
            sb.AppendLine($"{"Field Y",10} {"Distortion %",12}");
            foreach (var pt in result.DistortionPoints)
                sb.AppendLine($"{pt.FieldY,10:F3} {pt.Distortion,12:F3}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute wavefront OPD map over the exit pupil. Returns RMS and P-V wavefront error in waves.")]
        public string WavefrontMap(int fieldIndex = 0)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = WavefrontMapCalculator.Compute(sys, _session.GlassCatalog, fieldIndex, wl);

            var sb = new StringBuilder();
            sb.AppendLine($"Wavefront Map - Field {fieldIndex}");
            sb.AppendLine($"  Peak-to-Valley: {result.PeakToValley:F4} waves");
            sb.AppendLine($"  RMS Wavefront:  {result.RmsWavefront:F4} waves");
            sb.AppendLine($"  Grid Size:      {result.GridSize}");
            sb.AppendLine($"  Wavelength:     {result.Wavelength:F6} um");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute FFT PSF (Point Spread Function) and Strehl ratio for a field point.")]
        public string FftPsf(int fieldIndex = 0, int gridSize = 64)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = FftPsfCalculator.Compute(sys, _session.GlassCatalog, fieldIndex, wl, gridSize);

            var sb = new StringBuilder();
            sb.AppendLine($"FFT PSF - Field {fieldIndex}");
            sb.AppendLine($"  Strehl Ratio:   {result.StrehlRatio:F6}");
            sb.AppendLine($"  Peak Intensity: {result.PeakIntensity:F6}");
            sb.AppendLine($"  Pixel Size:     {result.PixelSizeMm:E4} mm");
            sb.AppendLine($"  Grid Size:      {result.GridSize}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute FFT MTF (Modulation Transfer Function) vs spatial frequency for a field point. Returns tangential and sagittal MTF curves.")]
        public string FftMtf(int fieldIndex = 0, int gridSize = 64)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = FftMtfCalculator.ComputeVsFrequency(sys, _session.GlassCatalog, fieldIndex, wl, gridSize);

            var sb = new StringBuilder();
            sb.AppendLine($"FFT MTF - Field {fieldIndex} (cutoff: {result.MaxFrequency:F1} cy/mm)");
            sb.AppendLine($"{"Freq (cy/mm)",14} {"Tangential",12} {"Sagittal",12}");

            int step = Math.Max(1, result.Points.Count / 15);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                sb.AppendLine($"{pt.SpatialFrequency,14:F1} {pt.Tangential,12:F4} {pt.Sagittal,12:F4}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Compute FFT MTF vs field at a given spatial frequency in cycles/mm. Returns tangential and sagittal MTF across the field.")]
        public string FftMtfVsField(double spatialFrequency = 50, int gridSize = 64)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = FftMtfCalculator.ComputeVsField(sys, _session.GlassCatalog, spatialFrequency, wl, gridSize);

            var sb = new StringBuilder();
            sb.AppendLine($"FFT MTF vs Field at {spatialFrequency:F1} cy/mm");
            sb.AppendLine($"{"Field Y",10} {"Tangential",12} {"Sagittal",12}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.FieldY,10:F3} {pt.Tangential,12:F4} {pt.Sagittal,12:F4}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute FFT MTF through focus at a given spatial frequency and field. Scans focus position to show how MTF varies with defocus. Supports monochromatic and polychromatic modes.")]
        public string FftMtfThroughFocus(int fieldIndex = 0, double spatialFrequency = 20.0,
            double focusRange = 0.5, int numSteps = 21, int gridSize = 256, bool polychromatic = false)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            if (sys.IsAfocal)
                return "FFT MTF through focus is not supported for afocal systems.";
            MtfThroughFocusResult result;
            string modeLabel;

            if (polychromatic)
            {
                result = FftMtfCalculator.ComputeThroughFocusPolychromatic(
                    sys, _session.GlassCatalog, fieldIndex, spatialFrequency, focusRange, numSteps, gridSize);
                modeLabel = "Polychromatic";
            }
            else
            {
                int wl = sys.PrimaryWavelengthIndex;
                result = FftMtfCalculator.ComputeThroughFocus(
                    sys, _session.GlassCatalog, fieldIndex, spatialFrequency, wl, focusRange, numSteps, gridSize);
                modeLabel = $"{sys.Wavelengths[wl].Value:F6} um";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"FFT Through Focus MTF - Field {fieldIndex}, {modeLabel}");
            sb.AppendLine($"Spatial Frequency: {spatialFrequency:F1} cy/mm, Range: +/-{focusRange:F3} mm");
            sb.AppendLine($"{"Focus Shift (mm)",18} {"Tangential",12} {"Sagittal",12}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.FocusShift,18:F6} {pt.Tangential,12:F6} {pt.Sagittal,12:F6}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute geometric MTF vs spatial frequency for a field point. By default multiplies by diffraction limit (standard convention). Supports monochromatic and polychromatic modes.")]
        public string GeometricMtf(int fieldIndex = 0, bool polychromatic = false,
            bool multiplyByDiffractionLimit = true)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            MtfResult result;
            string modeLabel;

            if (polychromatic)
            {
                result = GeometricMtfKidger.ComputePolychromatic(
                    sys, _session.GlassCatalog, fieldIndex,
                    multiplyByDiffractionLimit: multiplyByDiffractionLimit);
                modeLabel = "Polychromatic";
            }
            else
            {
                int wl = sys.PrimaryWavelengthIndex;
                result = GeometricMtfKidger.Compute(
                    sys, _session.GlassCatalog, fieldIndex, wl,
                    multiplyByDiffractionLimit: multiplyByDiffractionLimit);
                modeLabel = $"{sys.Wavelengths[wl].Value:F6} um";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Geometric MTF - Field {fieldIndex}, {modeLabel} (cutoff: {result.MaxFrequency:F1} cy/mm)");
            sb.AppendLine($"Multiply by Diffraction Limit: {multiplyByDiffractionLimit}");
            sb.AppendLine($"{"Freq (cy/mm)",14} {"Tangential",12} {"Sagittal",12}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.SpatialFrequency,14:F1} {pt.Tangential,12:F6} {pt.Sagittal,12:F6}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute geometric MTF vs field angle at one or more spatial frequencies. Returns tangential and sagittal MTF at each field point.")]
        public string GeometricMtfVsField(double spatialFrequency = 30, int numFieldPoints = 20,
            bool multiplyByDiffractionLimit = true)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var frequencies = new[] { spatialFrequency };
            var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(
                sys, _session.GlassCatalog, frequencies, wl,
                numFieldPoints: numFieldPoints,
                multiplyByDiffractionLimit: multiplyByDiffractionLimit);

            string freqUnit = sys.IsAfocal ? "cy/mrad" : "cy/mm";
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            var sb = new StringBuilder();
            sb.AppendLine($"Geometric MTF vs Field - {spatialFrequency:F1} {freqUnit}");
            sb.AppendLine($"{"Field (" + fieldUnit + ")",14} {"Tangential",12} {"Sagittal",12}");

            foreach (var pt in result.Points)
            {
                if (pt.Item2.Length > 0)
                    sb.AppendLine($"{pt.fieldY,14:F2} {pt.Item2[0].tang,12:F6} {pt.Item2[0].sag,12:F6}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Compute geometric MTF through focus at a specific spatial frequency and field point. Returns tangential and sagittal MTF vs focus shift.")]
        public string GeometricMtfThroughFocus(int fieldIndex = 0, double spatialFrequency = 30,
            double focusRange = 0.1, int numSteps = 21,
            bool multiplyByDiffractionLimit = true)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            if (sys.IsAfocal)
                return "Geometric MTF through focus is not supported for afocal systems.";
            int wl = sys.PrimaryWavelengthIndex;
            var result = GeometricMtfKidger.ComputeThroughFocus(
                sys, _session.GlassCatalog, fieldIndex, spatialFrequency, wl,
                focusRange, numSteps,
                multiplyByDiffractionLimit: multiplyByDiffractionLimit);

            string freqUnit = sys.IsAfocal ? "cy/mrad" : "cy/mm";
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            var sb = new StringBuilder();
            sb.AppendLine($"Geometric MTF Through Focus - Field {fieldIndex} ({sys.Fields[fieldIndex].Y} {fieldUnit}), {spatialFrequency:F1} {freqUnit}");
            sb.AppendLine($"{"Focus Shift (mm)",18} {"Tangential",12} {"Sagittal",12}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.FocusShift,18:F6} {pt.Tangential,12:F6} {pt.Sagittal,12:F6}");
            return sb.ToString();
        }

        [McpServerTool, Description("Compute Standard Zernike coefficients (Noll ordering) from wavefront for a field point. Returns coefficients in waves.")]
        public string ZernikeStandard(int fieldIndex = 0, int numTerms = 16)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = ZernikeCalculator.ComputeStandard(sys, _session.GlassCatalog, fieldIndex, wl, numTerms);

            var sb = new StringBuilder();
            sb.AppendLine($"Standard Zernike Coefficients - Field {fieldIndex}");
            sb.AppendLine($"  RMS Wavefront: {result.RmsWavefront:F4} waves");
            sb.AppendLine($"  P-V Wavefront: {result.PeakToValley:F4} waves");
            sb.AppendLine($"  RMS Fit Residual: {result.RmsFit:F6} waves");
            sb.AppendLine();
            sb.AppendLine($"{"Term",6} {"Name",-22} {"Coefficient",14}");

            for (int i = 0; i < result.Coefficients.Length; i++)
            {
                string name = ZernikeCalculator.GetStandardTermName(i + 1);
                sb.AppendLine($"{"Z" + (i + 1),6} {name,-22} {result.Coefficients[i],14:F6}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Compute Fringe Zernike coefficients from wavefront for a field point. Returns coefficients in waves.")]
        public string ZernikeFringe(int fieldIndex = 0, int numTerms = 16)
        {
            { var ge = CheckGlass(); if (ge != null) return ge; }
            var sys = _session.System;
            int wl = sys.PrimaryWavelengthIndex;
            var result = ZernikeCalculator.ComputeFringe(sys, _session.GlassCatalog, fieldIndex, wl, numTerms);

            var sb = new StringBuilder();
            sb.AppendLine($"Fringe Zernike Coefficients - Field {fieldIndex}");
            sb.AppendLine($"  RMS Wavefront: {result.RmsWavefront:F4} waves");
            sb.AppendLine($"  P-V Wavefront: {result.PeakToValley:F4} waves");
            sb.AppendLine($"  RMS Fit Residual: {result.RmsFit:F6} waves");
            sb.AppendLine();
            sb.AppendLine($"{"Term",6} {"Name",-22} {"Coefficient",14}");

            for (int i = 0; i < result.Coefficients.Length; i++)
            {
                string name = ZernikeCalculator.GetFringeTermName(i + 1);
                sb.AppendLine($"{"Z" + (i + 1),6} {name,-22} {result.Coefficients[i],14:F6}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Compute lateral color (transverse chromatic aberration) across the field.")]
        public string LateralColor()
        {
            var result = LateralColorCalculator.Compute(_session.System, _session.GlassCatalog);

            var sb = new StringBuilder();
            sb.AppendLine($"Lateral Color (max: {result.MaxLateralColor:E4} mm)");
            sb.AppendLine($"{"Field Y",10} {"Lateral Shift (mm)",18}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.FieldY,10:F3} {pt.LateralShift,18:E4}");

            return sb.ToString();
        }

        [McpServerTool, Description("Compute relative illumination across the field of view. numFieldPoints (default 50) sets the field-axis resolution. numPupilRays (default 36) is the number of pupil-boundary directions sampled per field point — increase for smoother curves on vignetted systems.")]
        public string RelativeIllumination(int numFieldPoints = 50, int numPupilRays = 36)
        {
            var result = RelativeIlluminationCalculator.Compute(_session.System, _session.GlassCatalog,
                numFieldPoints: numFieldPoints, numPupilRays: numPupilRays);

            var sb = new StringBuilder();
            sb.AppendLine("Relative Illumination");
            sb.AppendLine($"{"Field Y",10} {"Rel. Illum.",12}");

            foreach (var pt in result.Points)
                sb.AppendLine($"{pt.FieldY,10:F3} {pt.RelativeIllumination,12:F4}");

            return sb.ToString();
        }

    }
}
