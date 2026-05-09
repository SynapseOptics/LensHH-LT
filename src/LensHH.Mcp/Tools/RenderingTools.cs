using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class RenderingTools
    {
        private readonly McpSession _session;
        public RenderingTools(McpSession session) => _session = session;

        /// <summary>Send render request and track the analysis for refresh.</summary>
        private async Task<string> RenderAndTrack(string analysis, Dictionary<string, object>? parms, string successMsg, string errorPrefix = "Render error")
        {
            var response = await RenderAppClient.SendAsync(_session.System, analysis, parms);
            if (response.Success)
            {
                _session.LastRenderedAnalysis = analysis;
                _session.LastRenderedParams = parms != null ? new Dictionary<string, object>(parms) : null;
            }
            return response.Success ? successMsg : $"{errorPrefix}: {response.Error}";
        }

        [McpServerTool, Description("Clear the render window. Automatically called when a new system is loaded.")]
        public async Task<string> ClearRender()
        {
            _session.ClearLastRender();
            try
            {
                // Send Clear with a minimal dummy system — RenderApp handles Clear
                // before deserializing the system, so this is safe
                await RenderAppClient.SendAsync(new LensHH.Core.Models.OpticalSystem(), "Clear");
            }
            catch
            {
                // RenderApp may not be running — that's fine, nothing to clear
            }
            return "Render window cleared.";
        }

        [McpServerTool, Description("Refresh the render window by re-running the last rendered analysis with the current system. Useful after loading a new file or modifying the system.")]
        public async Task<string> RefreshRender()
        {
            if (_session.LastRenderedAnalysis == null)
                return "No analysis to refresh. Render an analysis first.";

            var response = await RenderAppClient.SendAsync(
                _session.System, _session.LastRenderedAnalysis, _session.LastRenderedParams);
            return response.Success
                ? $"Refreshed: {_session.LastRenderedAnalysis}"
                : $"Refresh error: {response.Error}";
        }

        // ─── Native rendering (via RenderApp) ─────────────────────────────

        [McpServerTool, Description("Render spot diagram natively in the render window for all fields. wavelengthIndex (-1 = polychromatic, 0..N-1 = single wavelength) selects which wavelength(s) to trace.")]
        public async Task<string> RenderSpotDiagram(
            [Description("Wavelength index (0-based). -1 (default) = polychromatic.")] int wavelengthIndex = -1)
            => await RenderAndTrack("SpotDiagram",
                new Dictionary<string, object> { ["WavelengthIndex"] = wavelengthIndex },
                "Spot diagram displayed in render window.");

        [McpServerTool, Description("Render OPD fan natively in the render window for all fields.")]
        public async Task<string> RenderOpdFan()
            => await RenderAndTrack("OpdFan", null, "OPD fan displayed in render window.");

        [McpServerTool, Description("Render transverse ray fan natively in the render window for all fields.")]
        public async Task<string> RenderRayFan()
            => await RenderAndTrack("RayFan", null, "Ray fan displayed in render window.");

        [McpServerTool, Description("Render pupil aberration fan natively in the render window for all fields. Shows real-vs-paraxial pupil coordinate deviation (percent).")]
        public async Task<string> RenderPupilAberrationFan()
            => await RenderAndTrack("PupilAberrationFan", null, "Pupil aberration fan displayed in render window.");

        [McpServerTool, Description("Render 2D system layout natively in the render window with lens elements and rays. wavelengthIndex selects which wavelength to trace (0-based); -1 (default) uses the primary wavelength.")]
        public async Task<string> RenderSystemLayout(int numRays = 15,
            [Description("If true, draw from surface 1 to image (omitting object distance). Default true.")] bool startFromSurface1 = true,
            [Description("Wavelength index (0-based) to trace. -1 (default) = primary wavelength.")] int wavelengthIndex = -1)
            => await RenderAndTrack("SystemLayout", new Dictionary<string, object>
                { ["NumRays"] = numRays, ["StartFromSurface1"] = startFromSurface1, ["WavelengthIndex"] = wavelengthIndex },
                "System layout displayed in render window.");

        [McpServerTool, Description("Render wavefront map natively in the render window for a field and wavelength.")]
        public async Task<string> RenderWavefrontMap(int fieldIndex = 0, int wavelengthIndex = -1, int gridSize = 64)
            => await RenderAndTrack("WavefrontMap", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["WavelengthIndex"] = wavelengthIndex, ["GridSize"] = gridSize },
                "Wavefront map displayed in render window.");

        [McpServerTool, Description("Render FFT MTF vs field natively. frequencies is a comma-separated list of spatial frequencies.")]
        public async Task<string> RenderFftMtfVsField(
            [Description("Spatial frequencies to evaluate (e.g. 10,20,30,40)")] string frequencies = "10,20,30,40",
            int wavelengthIndex = -1, bool polychromatic = false)
            => await RenderAndTrack("FftMtfVsField", new Dictionary<string, object>
                { ["Frequencies"] = frequencies, ["WavelengthIndex"] = wavelengthIndex, ["Polychromatic"] = polychromatic },
                "FFT MTF vs field displayed in render window.");

        [McpServerTool, Description("Render FFT PSF natively for a field and wavelength.")]
        public async Task<string> RenderFftPsf(int fieldIndex = 0, int wavelengthIndex = -1)
            => await RenderAndTrack("FftPsf", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["WavelengthIndex"] = wavelengthIndex },
                "FFT PSF displayed in render window.");

        [McpServerTool, Description("Render lateral color natively.")]
        public async Task<string> RenderLateralColor()
            => await RenderAndTrack("LateralColor", null, "Lateral color displayed in render window.");

        [McpServerTool, Description("Render field curvature natively.")]
        public async Task<string> RenderFieldCurvature()
            => await RenderAndTrack("FieldCurvature", null, "Field curvature displayed in render window.");

        [McpServerTool, Description("Render distortion natively.")]
        public async Task<string> RenderDistortion()
            => await RenderAndTrack("Distortion", null, "Distortion displayed in render window.");

        [McpServerTool, Description("Render chromatic focal shift natively.")]
        public async Task<string> RenderChromaticFocalShift()
            => await RenderAndTrack("ChromaticFocalShift", null, "Chromatic focal shift displayed in render window.");

        [McpServerTool, Description("Render relative illumination natively. numFieldPoints (default 50) sets the field-axis resolution. numPupilRays (default 36) is the number of pupil-boundary directions sampled per field point — increase for smoother curves on vignetted systems.")]
        public async Task<string> RenderRelativeIllumination(int numFieldPoints = 50, int numPupilRays = 36)
            => await RenderAndTrack("RelativeIllumination", new Dictionary<string, object>
                { ["NumFieldPoints"] = numFieldPoints, ["NumPupilRays"] = numPupilRays },
                "Relative illumination displayed in render window.");

        [McpServerTool, Description("Render system data (first-order properties) natively. Shows focal or afocal data depending on system mode.")]
        public async Task<string> RenderSystemData()
            => await RenderAndTrack("SystemData", null, "System data displayed in render window.");

        [McpServerTool, Description("Render Seidel (3rd order) aberration coefficients natively.")]
        public async Task<string> RenderSeidel()
            => await RenderAndTrack("Seidel", null, "Seidel coefficients displayed in render window.");

        [McpServerTool, Description("Render FFT MTF vs spatial frequency natively. Shows T/S curves for one or all fields. Set fieldIndex to -1 for all fields. maxFrequency caps the X axis in cycles/mm; 0 (default) uses the diffraction cutoff per result, matching the GUI.")]
        public async Task<string> RenderFftMtf(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = -1,
            int wavelengthIndex = -1,
            [Description("Max spatial frequency in cycles/mm for the X axis. 0 (default) = use the diffraction cutoff.")] double maxFrequency = 0)
            => await RenderAndTrack("FftMtf", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["WavelengthIndex"] = wavelengthIndex, ["MaxFrequency"] = maxFrequency },
                "FFT MTF vs spatial frequency displayed in render window.");

        [McpServerTool, Description("Render FFT MTF through focus natively for a field. Uses polychromatic by default.")]
        public async Task<string> RenderFftMtfThroughFocus(int fieldIndex = 0,
            double spatialFrequency = 50, double focusRange = 0.1)
            => await RenderAndTrack("FftMtfThroughFocus", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["SpatialFrequency"] = spatialFrequency, ["FocusRange"] = focusRange },
                "FFT MTF through focus displayed in render window.");

        // ─── Kidger Geometric MTF (fast) ───────────────────────────────

        [McpServerTool, Description("Render fast geometric MTF (Kidger method) natively. 50-500x faster than standard method.")]
        public async Task<string> RenderGeometricMtfKidger(int fieldIndex = 0, int wavelengthIndex = -1, int numRings = 30)
            => await RenderAndTrack("GeoMtf", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["WavelengthIndex"] = wavelengthIndex, ["NumRings"] = numRings },
                "Geometric MTF displayed in render window.");

        [McpServerTool, Description("Render fast geometric MTF vs field (Kidger method) natively.")]
        public async Task<string> RenderGeometricMtfKidgerVsField(
            [Description("Spatial frequencies (e.g. 10,20,30,40)")] string frequencies = "10,20,30,40",
            int wavelengthIndex = -1, int numRings = 30)
            => await RenderAndTrack("GeoMtfVsField", new Dictionary<string, object>
                { ["Frequencies"] = frequencies, ["WavelengthIndex"] = wavelengthIndex, ["NumRings"] = numRings },
                "Geometric MTF vs field displayed in render window.");

        [McpServerTool, Description("Render fast geometric MTF through focus (Kidger method) natively.")]
        public async Task<string> RenderGeometricMtfKidgerThroughFocus(int fieldIndex = 0,
            double spatialFrequency = 30, int wavelengthIndex = -1,
            double focusRange = 0.1, int numRings = 30)
            => await RenderAndTrack("GeoMtfThroughFocus", new Dictionary<string, object>
                { ["FieldIndex"] = fieldIndex, ["SpatialFrequency"] = spatialFrequency,
                  ["WavelengthIndex"] = wavelengthIndex, ["FocusRange"] = focusRange, ["NumRings"] = numRings },
                "Geometric MTF through focus displayed in render window.");

        // ─── PNG save (via RenderApp) ────────────────────────────────────

        [McpServerTool, Description("Save any analysis as a PNG image file via the RenderApp. analysisName: spot, rayfan, opdfan, fftmtf, fftmtf-field, fftmtf-focus, fftpsf, geomtf, geomtf-field, geomtf-focus, layout, seidel, relillum, lateralcolor, fieldcurvature, distortion, wavefront, chromaticfocalshift. Provide a full output path ending in .png. wavelengthIndex (-1 = primary) applies to analyses that honor it (layout, fftmtf, fftpsf, geomtf variants, wavefront).")]
        public async Task<string> SaveRenderPng(
            [Description("Analysis name (e.g. spot, rayfan, fftmtf)")] string analysisName,
            [Description("Full file path for the PNG output")] string outputPath,
            [Description("Wavelength index (0-based); -1 uses the primary wavelength.")] int wavelengthIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return "Error: outputPath is required.";

            // Map CLI-style names to RenderApp analysis keys
            string renderAnalysis = analysisName.ToLowerInvariant() switch
            {
                "spot" => "SpotDiagram",
                "rayfan" => "RayFan",
                "pupilaberration" => "PupilAberrationFan",
                "opdfan" => "OpdFan",
                "fftmtf" => "FftMtf",
                "fftmtf-field" => "FftMtfVsField",
                "fftmtf-focus" => "FftMtfThroughFocus",
                "fftpsf" => "FftPsf",
                "geomtf" => "GeoMtf",
                "geomtf-field" => "GeoMtfVsField",
                "geomtf-focus" => "GeoMtfThroughFocus",
                "layout" => "SystemLayout",
                "seidel" => "Seidel",
                "relillum" => "RelativeIllumination",
                "lateralcolor" => "LateralColor",
                "fieldcurvature" => "FieldCurvature",
                "distortion" => "Distortion",
                "wavefront" => "WavefrontMap",
                "chromaticfocalshift" => "ChromaticFocalShift",
                _ => ""
            };

            if (string.IsNullOrEmpty(renderAnalysis))
                return $"Unknown analysis: {analysisName}. Valid: spot, rayfan, pupilaberration, opdfan, fftmtf, fftmtf-field, fftmtf-focus, fftpsf, geomtf, geomtf-field, geomtf-focus, layout, seidel, relillum, lateralcolor, fieldcurvature, distortion, wavefront, chromaticfocalshift";

            var parms = new Dictionary<string, object> { ["SavePngPath"] = outputPath };
            if (wavelengthIndex >= 0) parms["WavelengthIndex"] = wavelengthIndex;
            var response = await RenderAppClient.SendAsync(_session.System, renderAnalysis, parms);
            return response.Success ? $"PNG saved to: {outputPath}" : $"Render error: {response.Error}";
        }

        // ─── HTML fallback (save to file) ─────────────────────────────────

        [McpServerTool, Description("Save any rendered HTML analysis to a file. analysisName: systemdata, spot, rayfan, opdfan, fftmtf, layout, seidel, relillum, lateralcolor, fieldcurvature, distortion, wavefront, fftpsf, chromaticfocalshift. Returns the file path.")]
        public string SaveRender(string analysisName, string outputPath = "")
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = Path.Combine(Path.GetTempPath(),
                    $"lenshh_{analysisName}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            string html = analysisName.ToLowerInvariant() switch
            {
                "spot" => RenderSpotHtml(),
                "rayfan" => RenderRayFanHtml(),
                "opdfan" => RenderOpdFanHtml(),
                "fftmtf" => RenderGeoMtfKidgerHtml(),
                "layout" => RenderLayoutHtml(),
                "systemdata" => RenderSystemDataHtml(),
                "seidel" => RenderSeidelHtml(),
                "relillum" => RenderRelativeIlluminationHtml(),
                "lateralcolor" => RenderLateralColorHtml(),
                "fieldcurvature" => RenderFieldCurvatureHtml(),
                "distortion" => RenderDistortionHtml(),
                "chromaticfocalshift" => RenderChromaticFocalShiftHtml(),
                _ => $"<html><body>Unknown analysis: {analysisName}</body></html>"
            };

            File.WriteAllText(outputPath, html);
            return $"Saved to: {outputPath}";
        }

        // ─── Text export ─────────────────────────────────────────────────

        [McpServerTool, Description("Export spot diagram as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file and returns the path; otherwise returns the text.")]
        public string ExportSpotDiagramText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var wls = sys.Wavelengths.Select(w => w.Value).ToArray();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = SpotDiagram.Compute(sys, _session.GlassCatalog, fi);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(SpotDiagramTextExport.Export(result, $"Spot Diagram \u2014 F{fi + 1}",
                        wls, sys.Fields[fi].Y, fieldUnit));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = SpotDiagram.Compute(sys, _session.GlassCatalog, fieldIndex);
            var text = SpotDiagramTextExport.Export(singleResult, $"Spot Diagram \u2014 F{fieldIndex + 1}",
                wls, sys.Fields[fieldIndex].Y, fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export OPD fan as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportOpdFanText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var wls = sys.Wavelengths.Select(w => w.Value).ToArray();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = OpdFan.Compute(sys, _session.GlassCatalog, fi);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(OpdFanTextExport.Export(result, $"OPD Fan \u2014 F{fi + 1}",
                        wls, sys.Fields[fi].Y, fieldUnit));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = OpdFan.Compute(sys, _session.GlassCatalog, fieldIndex);
            var text = OpdFanTextExport.Export(singleResult, $"OPD Fan \u2014 F{fieldIndex + 1}",
                wls, sys.Fields[fieldIndex].Y, fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export ray fan as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportRayFanText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var wls = sys.Wavelengths.Select(w => w.Value).ToArray();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = TransverseRayFan.Compute(sys, _session.GlassCatalog, fi);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(RayFanTextExport.Export(result, $"Ray Fan \u2014 F{fi + 1}",
                        wls, sys.Fields[fi].Y, fieldUnit));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = TransverseRayFan.Compute(sys, _session.GlassCatalog, fieldIndex);
            var text = RayFanTextExport.Export(singleResult, $"Ray Fan \u2014 F{fieldIndex + 1}",
                wls, sys.Fields[fieldIndex].Y, fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export FFT MTF as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportFftMtfText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            int wavelengthIndex = -1,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = FftMtfCalculator.ComputeVsFrequency(sys, _session.GlassCatalog, fi, wIdx);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(FftMtfTextExport.Export(result, $"FFT MTF \u2014 F{fi + 1} W{wIdx + 1}",
                        fieldValue: sys.Fields[fi].Y, wavelengthUm: sys.Wavelengths[wIdx].Value,
                        fieldUnit: fieldUnit, isAfocal: sys.IsAfocal));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = FftMtfCalculator.ComputeVsFrequency(sys, _session.GlassCatalog, fieldIndex, wIdx);
            var text = FftMtfTextExport.Export(singleResult, $"FFT MTF \u2014 F{fieldIndex + 1} W{wIdx + 1}",
                fieldValue: sys.Fields[fieldIndex].Y, wavelengthUm: sys.Wavelengths[wIdx].Value,
                fieldUnit: fieldUnit, isAfocal: sys.IsAfocal);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export system data (first-order properties) as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportSystemDataText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = SystemDataCalculator.Calculate(sys, _session.GlassCatalog);
            var text = SystemDataTextExport.Export(result, sys, _session.GlassCatalog, "System Data");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export Seidel coefficients as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportSeidelText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = SeidelCalculator.Calculate(sys, _session.GlassCatalog);
            var text = SeidelTextExport.Export(result, "Seidel Coefficients");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export lateral color as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportLateralColorText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = LateralColorCalculator.Compute(sys, _session.GlassCatalog);
            var wls = sys.Wavelengths.Select(w => w.Value).ToArray();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = LateralColorTextExport.Export(result, "Lateral Color", wls, fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export field curvature as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportFieldCurvatureText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = FieldCurvatureCalculator.Compute(sys, _session.GlassCatalog);
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = FieldCurvatureTextExport.Export(result, "Field Curvature", fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export distortion as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportDistortionText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = DistortionCalculator.Compute(sys, _session.GlassCatalog);
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = DistortionTextExport.Export(result, "Distortion", fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export FFT MTF vs field as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportFftMtfVsFieldText(
            [Description("Spatial frequencies (e.g. 10,20,30,40)")] string frequencies = "10,20,30,40",
            int wavelengthIndex = -1, bool polychromatic = false,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;
            var freqs = frequencies.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
            var result = FftMtfCalculator.ComputeVsFieldMultiFreq(sys, _session.GlassCatalog,
                freqs, wIdx, 256, 200, polychromatic);
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = MtfVsFieldTextExport.Export(result, "FFT MTF vs Field", fieldUnit, sys.IsAfocal);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export FFT PSF as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportFftPsfText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            int wavelengthIndex = -1,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = FftPsfCalculator.Compute(sys, _session.GlassCatalog, fi, wIdx, 64);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(FftPsfTextExport.Export(result, $"FFT PSF \u2014 F{fi + 1} W{wIdx + 1}"));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = FftPsfCalculator.Compute(sys, _session.GlassCatalog, fieldIndex, wIdx, 64);
            var text = FftPsfTextExport.Export(singleResult, $"FFT PSF \u2014 F{fieldIndex + 1} W{wIdx + 1}");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export relative illumination as tab-delimited text. If outputPath is provided, writes to file. numFieldPoints (default 50) sets the field-axis resolution. numPupilRays (default 36) is the number of pupil-boundary directions sampled per field point.")]
        public string ExportRelativeIlluminationText(
            [Description("Optional file path to write the text export to.")] string outputPath = "",
            int numFieldPoints = 50, int numPupilRays = 36)
        {
            var sys = _session.System;
            var result = RelativeIlluminationCalculator.Compute(sys, _session.GlassCatalog,
                numFieldPoints: numFieldPoints, numPupilRays: numPupilRays);
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = RelativeIlluminationTextExport.Export(result, "Relative Illumination", fieldUnit);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export chromatic focal shift as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportChromaticFocalShiftText(
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            var result = ChromaticFocalShift.Compute(sys, _session.GlassCatalog);
            var text = ChromaticFocalShiftTextExport.Export(result, "Chromatic Focal Shift");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export FFT MTF through focus as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportFftMtfThroughFocusText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            double spatialFrequency = 50, double focusRange = 0.1,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = FftMtfCalculator.ComputeThroughFocusPolychromatic(sys, _session.GlassCatalog,
                        fi, spatialFrequency, focusRange, 21, 64);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(MtfThroughFocusTextExport.Export(result, $"FFT MTF Through Focus \u2014 F{fi + 1}"));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = FftMtfCalculator.ComputeThroughFocusPolychromatic(sys, _session.GlassCatalog,
                fieldIndex, spatialFrequency, focusRange, 21, 64);
            var text = MtfThroughFocusTextExport.Export(singleResult, "FFT MTF Through Focus");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export fast geometric MTF through focus as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportGeometricMtfKidgerThroughFocusText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            double spatialFrequency = 30, int wavelengthIndex = -1,
            double focusRange = 0.1, int numRings = 30,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = GeometricMtfKidger.ComputeThroughFocus(sys, _session.GlassCatalog,
                        fi, spatialFrequency, wIdx, focusRange, 21, numRings);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(MtfThroughFocusTextExport.Export(result, $"Geometric MTF Through Focus (Kidger) \u2014 F{fi + 1}"));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = GeometricMtfKidger.ComputeThroughFocus(sys, _session.GlassCatalog,
                fieldIndex, spatialFrequency, wIdx, focusRange, 21, numRings);
            var text = MtfThroughFocusTextExport.Export(singleResult, "Geometric MTF Through Focus (Kidger)");
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export geometric MTF vs spatial frequency (Kidger) as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportGeometricMtfKidgerText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            int wavelengthIndex = -1, int numRings = 30,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = GeometricMtfKidger.Compute(sys, _session.GlassCatalog, fi, wIdx, numRings);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(FftMtfTextExport.Export(result, $"Geometric MTF (Kidger) \u2014 F{fi + 1} W{wIdx + 1}",
                        cutoffT: result.CutoffT, cutoffS: result.CutoffS,
                        fieldValue: sys.Fields[fi].Y, wavelengthUm: sys.Wavelengths[wIdx].Value,
                        fieldUnit: fieldUnit, isAfocal: sys.IsAfocal));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = GeometricMtfKidger.Compute(sys, _session.GlassCatalog, fieldIndex, wIdx, numRings);
            var singleText = FftMtfTextExport.Export(singleResult, $"Geometric MTF (Kidger) \u2014 F{fieldIndex + 1} W{wIdx + 1}",
                cutoffT: singleResult.CutoffT, cutoffS: singleResult.CutoffS,
                fieldValue: sys.Fields[fieldIndex].Y, wavelengthUm: sys.Wavelengths[wIdx].Value,
                fieldUnit: fieldUnit, isAfocal: sys.IsAfocal);
            return WriteTextExport(singleText, outputPath);
        }

        [McpServerTool, Description("Export geometric MTF vs field (Kidger) as tab-delimited text. If outputPath is provided, writes to file.")]
        public string ExportGeometricMtfKidgerVsFieldText(
            [Description("Spatial frequencies (e.g. 10,20,30,40)")] string frequencies = "10,20,30,40",
            int wavelengthIndex = -1, int numRings = 30, bool polychromatic = false,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;
            var freqs = frequencies.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
            var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(sys, _session.GlassCatalog,
                freqs, wIdx, numRings, 20, polychromatic: polychromatic);
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var text = MtfVsFieldTextExport.Export(result, "Geometric MTF vs Field (Kidger)", fieldUnit, sys.IsAfocal);
            return WriteTextExport(text, outputPath);
        }

        [McpServerTool, Description("Export wavefront map as tab-delimited text. Set fieldIndex to -1 for all fields. If outputPath is provided, writes to file.")]
        public string ExportWavefrontMapText(
            [Description("Field index (0-based), or -1 for all fields.")] int fieldIndex = 0,
            int wavelengthIndex = -1, int gridSize = 64,
            [Description("Optional file path to write the text export to.")] string outputPath = "")
        {
            var sys = _session.System;
            int wIdx = wavelengthIndex < 0 ? sys.PrimaryWavelengthIndex : wavelengthIndex;

            if (fieldIndex < 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int fi = 0; fi < sys.Fields.Count; fi++)
                {
                    var result = WavefrontMapCalculator.Compute(sys, _session.GlassCatalog, fi, wIdx, gridSize);
                    if (fi > 0) sb.AppendLine();
                    sb.Append(WavefrontMapTextExport.Export(result, $"Wavefront Map \u2014 F{fi + 1} W{wIdx + 1}"));
                }
                return WriteTextExport(sb.ToString(), outputPath);
            }

            var singleResult = WavefrontMapCalculator.Compute(sys, _session.GlassCatalog, fieldIndex, wIdx, gridSize);
            var text = WavefrontMapTextExport.Export(singleResult, $"Wavefront Map \u2014 F{fieldIndex + 1} W{wIdx + 1}");
            return WriteTextExport(text, outputPath);
        }

        // ─── Private helpers ─────────────────────────────────────────────

        private static string WriteTextExport(string text, string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
                return text;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(outputPath, text);
            return $"Text exported to: {outputPath}";
        }

        private string RenderSpotHtml()
        {
            var sys = _session.System;
            var results = new SpotDiagramResult[sys.Fields.Count];
            for (int i = 0; i < sys.Fields.Count; i++)
                results[i] = SpotDiagram.Compute(sys, _session.GlassCatalog, i);
            var titles = results.Select((r, i) => $"F{i + 1} ({sys.Fields[i].Y})").ToArray();
            return SpotDiagramRenderer.RenderPage(results, titles, sys.Title ?? "Spot Diagram");
        }

        private string RenderRayFanHtml()
        {
            var sys = _session.System;
            var results = new RayFanResult[sys.Fields.Count];
            for (int i = 0; i < sys.Fields.Count; i++)
                results[i] = TransverseRayFan.Compute(sys, _session.GlassCatalog, i);
            var titles = results.Select((r, i) => $"F{i + 1} ({sys.Fields[i].Y})").ToArray();
            return RayFanRenderer.RenderPage(results, titles, sys.Title ?? "Ray Fan");
        }

        private string RenderOpdFanHtml()
        {
            var sys = _session.System;
            var results = new OpdFanResult[sys.Fields.Count];
            for (int i = 0; i < sys.Fields.Count; i++)
                results[i] = OpdFan.Compute(sys, _session.GlassCatalog, i);
            var titles = results.Select((r, i) => $"F{i + 1} ({sys.Fields[i].Y})").ToArray();
            return OpdFanRenderer.RenderPage(results, titles, sys.Title ?? "OPD Fan");
        }

        private string RenderLayoutHtml()
        {
            var sys = _session.System;
            var layout = SystemLayout.ComputeLayout(sys, _session.GlassCatalog, 15, startFromSurface1: true);
            var fieldYs = sys.Fields.Select(f => f.Y).ToList();
            string fieldUnit = sys.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            return SystemLayoutRenderer.RenderPage(layout, sys.Title ?? "System Layout",
                fieldYs: fieldYs, fieldUnit: fieldUnit);
        }

        private string RenderSystemDataHtml()
        {
            var sys = _session.System;
            var result = SystemDataCalculator.Calculate(sys, _session.GlassCatalog);
            return SystemDataRenderer.RenderPage(result, sys.Title ?? "System Data");
        }

        private string RenderSeidelHtml()
        {
            var sys = _session.System;
            var result = SeidelCalculator.Calculate(sys, _session.GlassCatalog);
            return SeidelRenderer.RenderPage(result, sys.Title ?? "Seidel Coefficients");
        }

        private string RenderRelativeIlluminationHtml()
        {
            var sys = _session.System;
            // Match the GUI / native-render-window defaults so the HTML
            // export from save_render produces curves of the same fidelity.
            var result = RelativeIlluminationCalculator.Compute(sys, _session.GlassCatalog,
                numFieldPoints: 50, numPupilRays: 36);
            return RelativeIlluminationRenderer.RenderPage(result, sys.Title ?? "Relative Illumination");
        }

        private string RenderLateralColorHtml()
        {
            var sys = _session.System;
            var result = LateralColorCalculator.Compute(sys, _session.GlassCatalog);
            double maxField = sys.Fields.Max(f => Math.Abs(f.Y));
            var wlLabels = sys.Wavelengths.Select(w => $"{w.Value:F4}um").ToArray();
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            return LateralColorRenderer.RenderPage(result, sys.Title ?? "Lateral Color",
                maxField, sys.Wavelengths.Count, wlLabels, fieldUnit: fieldUnit);
        }

        private string RenderFieldCurvatureHtml()
        {
            var sys = _session.System;
            string fieldUnit = sys.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var waveLabels = sys.Wavelengths.Select(w => $"{w.Value:F4} µm").ToArray();
            var mw = FieldCurvatureCalculator.ComputeAllWavelengths(sys, _session.GlassCatalog, numPoints: 100);
            return FieldCurvatureRenderer.RenderMultiWavelengthPage(mw, sys.Title ?? "Field Curvature", waveLabels, fieldUnit: fieldUnit);
        }

        private string RenderDistortionHtml()
        {
            var sys = _session.System;
            var result = DistortionCalculator.Compute(sys, _session.GlassCatalog);
            return DistortionRenderer.RenderPage(result, sys.Title ?? "Distortion");
        }

        private string RenderChromaticFocalShiftHtml()
        {
            var sys = _session.System;
            var result = ChromaticFocalShift.Compute(sys, _session.GlassCatalog);
            return ChromaticFocalShiftRenderer.RenderPage(result, sys.Title ?? "Chromatic Focal Shift");
        }

        private string RenderGeoMtfKidgerHtml()
        {
            var sys = _session.System;
            int wIdx = sys.PrimaryWavelengthIndex;
            var result = GeometricMtfKidger.Compute(sys, _session.GlassCatalog, 0, wIdx, 30);
            return FftMtfRenderer.RenderPage(
                new[] { new[] { result } },
                new[] { "F1" },
                sys.Title ?? "Geometric MTF (Kidger)");
        }
    }
}
