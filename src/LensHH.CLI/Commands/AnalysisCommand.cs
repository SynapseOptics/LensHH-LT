using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LensHH.Core.Analysis;
using LensHH.Core.Glass;
using LensHH.Core.RayTrace;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class AnalysisCommand : ICommand
    {
        public string Name => "analysis";
        public string Description => "Run analyses: paraxial, spot, ray-fan, pupil-fan, seidel, chromatic-shift, opd-fan, field-curve, wavefront, psf, fft-mtf, fft-mtf-focus, fft-mtf-field, geo-mtf, geo-mtf-field, geo-mtf-focus, zernike";

        public string Help => @"[bold]analysis[/] - Run optical analyses
  [green]analysis paraxial[/]                    Paraxial data (EFL, BFL, pupils)
  [green]analysis spot [[field_num]][/]            Spot diagram (default field 1)
  [green]analysis ray-fan [[field_num]][/]         Transverse ray aberration fans
  [green]analysis pupil-fan [[field_num]][/]       Pupil aberration fans (%)
  [green]analysis opd-fan [[field_num]][/]         OPD fan (waves vs pupil)
  [green]analysis seidel[/]                      Seidel aberration coefficients
  [green]analysis chromatic-shift[/]             Chromatic focal shift
  [green]analysis field-curve[/]                 Field curvature and distortion
  [green]analysis wavefront [[field_num]][/]       Wavefront OPD map
  [green]analysis psf [[field_num]][/]             FFT PSF and Strehl ratio
  [green]analysis fft-mtf [[field_num]][/]         FFT MTF vs spatial frequency
  [green]analysis fft-mtf-focus [[grid]] [[wl|poly]] [[freq]] [[range]] [[steps]][/]  FFT MTF through focus
  [green]analysis fft-mtf-field [[freq]][/]        FFT MTF vs field at given frequency
  [green]analysis geo-mtf [[field_num]] [[wl|poly]][/]  Geometric MTF vs spatial frequency
  [green]analysis geo-mtf-field [[npts]] [[wl|poly]] [[outfile]][/]  Geometric MTF vs field (multi-frequency)
  [green]analysis zernike [[field_num]][/]         Standard Zernike coefficients
  [green]analysis zernike-fringe [[field_num]][/]  Fringe Zernike coefficients
  [green]analysis lateral-color[/]               Lateral color vs field
  [green]analysis ray-trace [[field]] [[px]] [[py]] [[wave]] [[outfile]][/]  Per-surface ray trace listing
  [green]analysis rel-illum [[--field-pts N]] [[--pupil-rays N]][/]  Relative illumination vs field
    --field-pts (default 50) and --pupil-rays (default 36) control plot resolution.
    Also honored by 'analysis render relillum' / 'analysis show relillum' /
    'analysis export-text relillum'.
  [green]analysis render <name> [[outfile.html]] [[--open]] [[--wave N]][/]  Render analysis as HTML file
    Names: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor,
           fieldcurvature, distortion, fftmtf, fftmtf-field, fftmtf-focus,
           geomtf, geomtf-field, geomtf-focus, wavefront
    --wave N selects 1-based wavelength for layout (default: primary)
  [green]analysis show <name> [[--save [[outfile.png]]]] [[--wave N]][/]  Pop up the LensHH-LT Render window
    Names: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor,
           fieldcurvature, distortion, fftmtf, fftmtf-field, fftmtf-focus,
           fftpsf, geomtf, geomtf-field, geomtf-focus, wavefront,
           chromaticfocalshift
    The window stays open and accumulates a tab per call. Pass --save
    (with optional path) to also write a PNG.
  [green]analysis export-text <name> [[outfile.txt]][/]  Export analysis as tab-delimited text
    Names: spot, rayfan, opdfan, fftmtf, seidel, lateralcolor, fieldcurvature,
           distortion, fftmtf-field, fftpsf, relillum, chromaticshift, fftmtf-focus,
           geomtf-focus, wavefront
  Field numbers are 1-based.";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            // Validate all glass materials are resolvable before any analysis
            session.EnsureGlassCatalog();
            session.ValidateGlass();

            switch (args[0].ToLowerInvariant())
            {
                case "paraxial":
                    RunParaxial(session);
                    break;
                case "spot":
                    RunSpot(session, args);
                    break;
                case "ray-fan":
                    RunRayFan(session, args);
                    break;
                case "pupil-fan":
                    RunPupilAberrationFan(session, args);
                    break;
                case "opd-fan":
                    RunOpdFan(session, args);
                    break;
                case "seidel":
                    RunSeidel(session);
                    break;
                case "chromatic-shift":
                    RunChromaticShift(session);
                    break;
                case "field-curve":
                    RunFieldCurvature(session);
                    break;
                case "wavefront":
                    RunWavefront(session, args);
                    break;
                case "psf":
                    RunPsf(session, args);
                    break;
                case "fft-mtf":
                    RunFftMtf(session, args);
                    break;
                case "fft-mtf-poly":
                    RunFftMtfPoly(session, args);
                    break;
                case "fft-mtf-focus":
                    RunFftMtfThroughFocus(session, args);
                    break;
                case "fft-mtf-field":
                    RunFftMtfVsField(session, args);
                    break;
                case "geo-mtf":
                    RunGeoMtf(session, args);
                    break;
                case "geo-mtf-field":
                    RunGeoMtfVsField(session, args);
                    break;
                case "geo-mtf-focus":
                    RunGeoMtfThroughFocus(session, args);
                    break;
                case "zernike":
                    RunZernike(session, args, false);
                    break;
                case "zernike-fringe":
                    RunZernike(session, args, true);
                    break;
                case "lateral-color":
                    RunLateralColor(session);
                    break;
                case "ray-trace":
                    RunRayTrace(session, args);
                    break;
                case "rel-illum":
                    RunRelativeIllumination(session, args);
                    break;
                case "render":
                    RunRender(session, args);
                    break;
                case "show":
                    RunShow(session, args);
                    break;
                case "export-text":
                    RunExportText(session, args);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown analysis: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private static string FieldUnit(LensHH.Core.Models.OpticalSystem system)
            => system.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        private void RunParaxial(Session session)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            var result = SystemDataCalculator.Calculate(system, glassMgr);

            var table = new Table();
            table.Title = new TableTitle(result.IsAfocal ? "System Data (Afocal)" : "System Data (Focal)");
            table.AddColumn("Parameter");
            table.AddColumn(new TableColumn("Value").RightAligned());
            table.AddColumn("Unit");

            if (result.IsAfocal)
            {
                table.AddRow("Angular Magnification", result.AngularMagnification.ToString("G6"), "x");
                table.AddEmptyRow();
                table.AddRow("Entrance Pupil Diameter", result.EntrancePupilDiameter.ToString("F4"), "mm");
                table.AddRow("Entrance Pupil Position", result.EntrancePupilPosition.ToString("F4"), "mm");
                table.AddRow("Exit Pupil Diameter", result.ExitPupilDiameter.ToString("F4"), "mm");
                table.AddRow("Exit Pupil Position", result.ExitPupilPosition.ToString("F4"), "mm");
                table.AddEmptyRow();
                table.AddRow("Total Track", result.TotalTrack.ToString("F4"), "mm");
                table.AddRow("Maximum Field", result.MaximumField.ToString("F4"), result.FieldUnit);
            }
            else
            {
                table.AddRow("Effective Focal Length", result.Efl.ToString("F4"), "mm");
                table.AddRow("Back Focal Length", result.Bfl.ToString("F4"), "mm");
                table.AddRow("Front Focal Length", result.Ffl.ToString("F4"), "mm");
                table.AddEmptyRow();
                table.AddRow("Image Space F/#", result.ImageSpaceFNumber.ToString("F4"), "");
                table.AddRow("Working F/#", result.WorkingFNumber.ToString("F4"), "");
                table.AddRow("Image Space NA", result.ImageSpaceNA.ToString("F6"), "");
                table.AddEmptyRow();
                table.AddRow("Entrance Pupil Diameter", result.EntrancePupilDiameter.ToString("F4"), "mm");
                table.AddRow("Entrance Pupil Position", result.EntrancePupilPosition.ToString("F4"), "mm");
                table.AddRow("Exit Pupil Diameter", result.ExitPupilDiameter.ToString("F4"), "mm");
                table.AddRow("Exit Pupil Position", result.ExitPupilPosition.ToString("F4"), "mm");
                table.AddEmptyRow();
                table.AddRow("Paraxial Image Height", result.ParaxialImageHeight.ToString("F4"), "mm");
                table.AddRow("Paraxial Magnification", result.ParaxialMagnification.ToString("G6"), "");
                table.AddEmptyRow();
                table.AddRow("Total Track", result.TotalTrack.ToString("F4"), "mm");
                table.AddRow("Maximum Field", result.MaximumField.ToString("F4"), result.FieldUnit);
            }

            AnsiConsole.Write(table);
        }

        private void RunSpot(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            // --wave N (1-based, default primary/polychromatic) maps to engine -1 or N-1
            int waveIdx = -1;
            for (int a = 1; a < args.Length; a++)
            {
                if (args[a].StartsWith("--wave="))
                {
                    if (int.TryParse(args[a].Substring("--wave=".Length), out int w)) waveIdx = w - 1;
                }
                else if (args[a] == "--wave" && a + 1 < args.Length &&
                         int.TryParse(args[a + 1], out int w))
                {
                    waveIdx = w - 1;
                    a++;
                }
            }

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            var result = SpotDiagram.Compute(system, glassMgr, fieldIdx,
                numRings: 6, numArms: 12, wavelengthIndex: waveIdx);

            AnsiConsole.MarkupLine($"[bold]Spot Diagram - Field {fieldIdx + 1} (Y = {system.Fields[fieldIdx].Y})[/]");

            var table = new Table();
            table.AddColumn("Metric");
            table.AddColumn("Value");

            table.AddRow("RMS Radius", result.RmsRadius.ToString("E4") + " mm");
            table.AddRow("GEO Radius", result.GeoRadius.ToString("E4") + " mm");
            table.AddRow("Centroid X", result.CentroidX.ToString("E4") + " mm");
            table.AddRow("Centroid Y", result.CentroidY.ToString("E4") + " mm");
            table.AddRow("Chief Ray X", result.ChiefRayX.ToString("E4") + " mm");
            table.AddRow("Chief Ray Y", result.ChiefRayY.ToString("E4") + " mm");
            table.AddRow("Total Rays", result.Points.Count.ToString());

            AnsiConsole.Write(table);
        }

        private void RunRayFan(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            var result = TransverseRayFan.Compute(system, glassMgr, fieldIdx, 32);

            AnsiConsole.MarkupLine($"[bold]Ray Fan - Field {fieldIdx + 1} (Y = {system.Fields[fieldIdx].Y})[/]");
            AnsiConsole.MarkupLine($"Max Aberration: {result.MaxAberration:E4} mm");

            var tangTable = new Table().Title("Tangential Fan (EY vs PY)");
            tangTable.AddColumn("PY");
            foreach (var wl in system.Wavelengths)
                tangTable.AddColumn($"{wl.Value:F3} um");

            var keyPositions = new[] { -1.0, -0.7, -0.5, -0.3, 0.0, 0.3, 0.5, 0.7, 1.0 };
            foreach (double py in keyPositions)
            {
                var row = new[] { py.ToString("F1") }.Concat(
                    system.Wavelengths.Select((wl, wi) =>
                    {
                        var closest = result.TangentialFan
                            .Where(p => p.WavelengthIndex == wi)
                            .OrderBy(p => Math.Abs(p.PupilCoordinate - py))
                            .FirstOrDefault();
                        return closest != null ? closest.Aberration.ToString("E3") : "---";
                    })).ToArray();
                tangTable.AddRow(row);
            }

            AnsiConsole.Write(tangTable);
        }

        private void RunPupilAberrationFan(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            string fieldUnit = system.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var result = LensHH.Core.Analysis.PupilAberrationFan.Compute(system, glassMgr, fieldIdx, 40);

            AnsiConsole.MarkupLine($"[bold]Pupil Aberration Fan - Field {fieldIdx + 1} (Y = {system.Fields[fieldIdx].Y} {fieldUnit})[/]");
            AnsiConsole.MarkupLine($"Max Aberration: [yellow]{result.MaxAberration:F4} %[/]");

            var tangTable = new Table().Title("Tangential (PY → Aberration %)");
            tangTable.AddColumn("PY");
            for (int wi = 0; wi < system.Wavelengths.Count; wi++)
                tangTable.AddColumn($"{system.Wavelengths[wi].Value:F4} um");

            var tangByPy = result.TangentialFan.GroupBy(p => p.PupilCoordinate).OrderBy(g => g.Key);
            foreach (var group in tangByPy)
            {
                var row = new List<string> { group.Key.ToString("F3") };
                for (int wi = 0; wi < system.Wavelengths.Count; wi++)
                {
                    var pt = group.FirstOrDefault(p => p.WavelengthIndex == wi);
                    row.Add(pt != null ? pt.Aberration.ToString("F4") : "—");
                }
                tangTable.AddRow(row.ToArray());
            }
            AnsiConsole.Write(tangTable);

            var sagTable = new Table().Title("Sagittal (PX → Aberration %)");
            sagTable.AddColumn("PX");
            for (int wi = 0; wi < system.Wavelengths.Count; wi++)
                sagTable.AddColumn($"{system.Wavelengths[wi].Value:F4} um");

            var sagByPx = result.SagittalFan.GroupBy(p => p.PupilCoordinate).OrderBy(g => g.Key);
            foreach (var group in sagByPx)
            {
                var row = new List<string> { group.Key.ToString("F3") };
                for (int wi = 0; wi < system.Wavelengths.Count; wi++)
                {
                    var pt = group.FirstOrDefault(p => p.WavelengthIndex == wi);
                    row.Add(pt != null ? pt.Aberration.ToString("F4") : "—");
                }
                sagTable.AddRow(row.ToArray());
            }
            AnsiConsole.Write(sagTable);
        }

        private void RunOpdFan(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            double deltaPupil = 0.05;
            if (argCount > 2 && double.TryParse(args[2], out double dp) && dp > 0 && dp <= 1.0)
                deltaPupil = dp;

            int numPoints = Math.Max(10, (int)Math.Round(2.0 / deltaPupil));
            var result = OpdFan.Compute(system, glassMgr, fieldIdx, numPoints);

            AnsiConsole.MarkupLine($"[bold]OPD Fan - Field {fieldIdx + 1} (Y = {system.Fields[fieldIdx].Y})[/]");
            AnsiConsole.MarkupLine($"Max OPD: {result.MaxOpd:F4} waves");

            // Build pupil positions from -1 to +1 at deltaPupil steps
            var keyPositions = new List<double>();
            for (double p = -1.0; p <= 1.0 + 1e-10; p += deltaPupil)
            {
                double rounded = Math.Round(p, 10);
                if (Math.Abs(rounded) < 1e-10) rounded = 0;
                keyPositions.Add(rounded);
            }

            // Helper to look up OPD at a pupil position for a given wavelength
            string LookupOpd(List<OpdFanPoint> fan, int wi, double coord, string fmt)
            {
                var closest = fan
                    .Where(p => p.WavelengthIndex == wi)
                    .OrderBy(p => Math.Abs(p.PupilCoordinate - coord))
                    .FirstOrDefault();
                return closest != null ? closest.Opd.ToString(fmt) : "---";
            }

            // Tangential fan table
            var tanTable = new Table().Title("Tangential OPD (waves vs PY)");
            tanTable.AddColumn("PY");
            foreach (var wl in system.Wavelengths)
                tanTable.AddColumn($"{wl.Value:F3} um");

            foreach (double py in keyPositions)
            {
                var row = new[] { py.ToString("F2") }.Concat(
                    system.Wavelengths.Select((wl, wi) => LookupOpd(result.TangentialFan, wi, py, "F4"))).ToArray();
                tanTable.AddRow(row);
            }
            AnsiConsole.Write(tanTable);

            // Sagittal fan table
            var sagTable = new Table().Title("Sagittal OPD (waves vs PX)");
            sagTable.AddColumn("PX");
            foreach (var wl in system.Wavelengths)
                sagTable.AddColumn($"{wl.Value:F3} um");

            foreach (double px in keyPositions)
            {
                var row = new[] { px.ToString("F2") }.Concat(
                    system.Wavelengths.Select((wl, wi) => LookupOpd(result.SagittalFan, wi, px, "F4"))).ToArray();
                sagTable.AddRow(row);
            }
            AnsiConsole.Write(sagTable);

            // File export: complete data at every computed pupil position
            if (outFile != null)
            {
                var wlHeaders = system.Wavelengths.Select(w => $"{w.Value:F4}um").ToArray();

                // Get all pupil positions from the computed data
                var allTanPositions = result.TangentialFan
                    .Where(p => p.WavelengthIndex == 0)
                    .Select(p => p.PupilCoordinate)
                    .OrderBy(p => p).ToList();
                var allSagPositions = result.SagittalFan
                    .Where(p => p.WavelengthIndex == 0)
                    .Select(p => p.PupilCoordinate)
                    .OrderBy(p => p).ToList();

                var rows = new List<string[]>();

                // Tangential section
                rows.Add(new[] { "Tangential OPD (waves vs PY)" });
                rows.Add(new[] { "PY" }.Concat(wlHeaders).ToArray());
                foreach (double py in allTanPositions)
                {
                    var row = new[] { py.ToString("F6") }.Concat(
                        system.Wavelengths.Select((wl, wi) => LookupOpd(result.TangentialFan, wi, py, "F6"))).ToArray();
                    rows.Add(row);
                }

                rows.Add(new[] { "" }); // blank separator

                // Sagittal section
                rows.Add(new[] { "Sagittal OPD (waves vs PX)" });
                rows.Add(new[] { "PX" }.Concat(wlHeaders).ToArray());
                foreach (double px in allSagPositions)
                {
                    var row = new[] { px.ToString("F6") }.Concat(
                        system.Wavelengths.Select((wl, wi) => LookupOpd(result.SagittalFan, wi, px, "F6"))).ToArray();
                    rows.Add(row);
                }

                try
                {
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"OPD Fan - Field {fieldIdx + 1} (Y = {system.Fields[fieldIdx].Y} {FieldUnit(system)})");
                    writer.WriteLine($"Max OPD: {result.MaxOpd:F6} waves");
                    writer.WriteLine();
                    foreach (var row in rows)
                        writer.WriteLine(string.Join("\t", row));
                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({allTanPositions.Count} tan + {allSagPositions.Count} sag points)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunSeidel(Session session)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            var result = SeidelCalculator.Calculate(system, glassMgr);

            AnsiConsole.MarkupLine("[bold]Seidel Aberration Coefficients[/]");

            if (result.SurfaceData.Count > 0)
            {
                var surfTable = new Table().Title("Surface Contributions");
                surfTable.AddColumn("Surf");
                surfTable.AddColumn("S1 (Sph)");
                surfTable.AddColumn("S2 (Coma)");
                surfTable.AddColumn("S3 (Ast)");
                surfTable.AddColumn("S4 (Ptz)");
                surfTable.AddColumn("S5 (Dist)");
                surfTable.AddColumn("CL");
                surfTable.AddColumn("CT");

                foreach (var s in result.SurfaceData)
                {
                    surfTable.AddRow(
                        s.SurfaceIndex.ToString(),
                        s.S1.ToString("E3"),
                        s.S2.ToString("E3"),
                        s.S3.ToString("E3"),
                        s.S4.ToString("E3"),
                        s.S5.ToString("E3"),
                        s.CL.ToString("E3"),
                        s.CT.ToString("E3")
                    );
                }

                AnsiConsole.Write(surfTable);
            }

            var totTable = new Table().Title("Totals");
            totTable.AddColumn("Coefficient");
            totTable.AddColumn("Value");
            totTable.AddRow("S1 (Spherical)", result.S1.ToString("E4"));
            totTable.AddRow("S2 (Coma)", result.S2.ToString("E4"));
            totTable.AddRow("S3 (Astigmatism)", result.S3.ToString("E4"));
            totTable.AddRow("S4 (Petzval)", result.S4.ToString("E4"));
            totTable.AddRow("S5 (Distortion)", result.S5.ToString("E4"));
            totTable.AddRow("CL (Long. Chromatic)", result.CL.ToString("E4"));
            totTable.AddRow("CT (Trans. Chromatic)", result.CT.ToString("E4"));

            AnsiConsole.Write(totTable);
        }

        private void RunChromaticShift(Session session)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            var result = ChromaticFocalShift.Compute(system, glassMgr);

            AnsiConsole.MarkupLine("[bold]Chromatic Focal Shift[/]");
            AnsiConsole.MarkupLine($"Maximum Shift: {result.MaxShift:E4} mm");

            var table = new Table();
            table.AddColumn("Wavelength (um)");
            table.AddColumn("Focal Shift (mm)");
            table.AddColumn("EFL (mm)");

            int step = Math.Max(1, result.Points.Count / 10);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                table.AddRow(
                    pt.Wavelength.ToString("F4"),
                    pt.FocalShift.ToString("E4"),
                    pt.Efl.ToString("F4")
                );
            }

            AnsiConsole.Write(table);
        }

        private void RunFieldCurvature(Session session)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            var result = FieldCurvatureDistortion.Compute(system, glassMgr);

            AnsiConsole.MarkupLine("[bold]Field Curvature and Distortion[/]");

            var table = new Table();
            table.AddColumn("Field Y");
            table.AddColumn("Tang Focus");
            table.AddColumn("Sag Focus");
            table.AddColumn("Medial Focus");

            foreach (var pt in result.FieldCurvaturePoints)
            {
                table.AddRow(
                    pt.FieldY.ToString("F3"),
                    pt.TangentialFocus.ToString("F4"),
                    pt.SagittalFocus.ToString("F4"),
                    pt.MedialFocus.ToString("F4")
                );
            }

            AnsiConsole.Write(table);

            var distTable = new Table().Title("Distortion");
            distTable.AddColumn("Field Y");
            distTable.AddColumn("Distortion (%)");

            foreach (var pt in result.DistortionPoints)
            {
                distTable.AddRow(
                    pt.FieldY.ToString("F3"),
                    pt.Distortion.ToString("F3")
                );
            }

            AnsiConsole.Write(distTable);
        }

        private void RunWavefront(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            int primaryWl = system.PrimaryWavelengthIndex;
            int wfGridSize = 256;
            if (argCount > 2 && int.TryParse(args[2], out int gs) && gs > 0)
                wfGridSize = gs;
            var result = WavefrontMapCalculator.Compute(system, glassMgr, fieldIdx, primaryWl, wfGridSize);

            AnsiConsole.MarkupLine($"[bold]Wavefront Map - Field {fieldIdx + 1}[/]");

            var table = new Table();
            table.AddColumn("Parameter");
            table.AddColumn("Value");
            table.AddRow("Peak-to-Valley", result.PeakToValley.ToString("F4") + " waves");
            table.AddRow("RMS Wavefront", result.RmsWavefront.ToString("F4") + " waves");
            table.AddRow("Grid Size", result.GridSize.ToString());
            table.AddRow("Wavelength", result.Wavelength.ToString("F4") + " um");

            AnsiConsole.Write(table);

            if (outFile != null)
            {
                try
                {
                    int n = result.GridSize;
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"Wavefront Map - Field {fieldIdx + 1} ({system.Fields[fieldIdx].Y} {FieldUnit(system)})");
                    writer.WriteLine($"Wavelength: {result.Wavelength:F4} um");
                    writer.WriteLine($"Peak to valley: {result.PeakToValley:F4} waves, RMS: {result.RmsWavefront:F4} waves");
                    writer.WriteLine($"Grid: {n}x{n}");
                    writer.WriteLine();
                    for (int i = 0; i < n; i++)
                    {
                        var vals = new string[n];
                        for (int j = 0; j < n; j++)
                            vals[j] = result.Valid[i, j] ? result.Opd[i, j].ToString("E6") : "NaN";
                        writer.WriteLine(string.Join("\t", vals));
                    }
                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({n}x{n} grid)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunPsf(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            int primaryWl = system.PrimaryWavelengthIndex;
            var result = FftPsfCalculator.Compute(system, glassMgr, fieldIdx, primaryWl);

            AnsiConsole.MarkupLine($"[bold]FFT PSF - Field {fieldIdx + 1}[/]");

            var table = new Table();
            table.AddColumn("Parameter");
            table.AddColumn("Value");
            table.AddRow("Strehl Ratio", result.StrehlRatio.ToString("F6"));
            table.AddRow("Peak Intensity", result.PeakIntensity.ToString("F6"));
            table.AddRow("Pixel Size", result.PixelSizeMm.ToString("E4") + " mm");
            table.AddRow("Grid Size", result.GridSize.ToString());

            AnsiConsole.Write(table);
        }

        private void RunFftMtf(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            int gridSize = 256;
            if (argCount > 2 && int.TryParse(args[2], out int gs) && gs > 0)
                gridSize = gs;

            int wlIdx = system.PrimaryWavelengthIndex;
            if (argCount > 3 && int.TryParse(args[3], out int wi) && wi >= 1 && wi <= system.Wavelengths.Count)
                wlIdx = wi - 1;

            var result = FftMtfCalculator.ComputeVsFrequency(system, glassMgr, fieldIdx, wlIdx, gridSize);

            AnsiConsole.MarkupLine($"[bold]FFT MTF - Field {fieldIdx + 1}, {system.Wavelengths[wlIdx].Value:F4} um[/]");
            AnsiConsole.MarkupLine($"Cutoff Frequency: {result.MaxFrequency:F1} cycles/mm");

            var table = new Table();
            table.AddColumn("Freq (cy/mm)");
            table.AddColumn("Diff Limit");
            table.AddColumn("Tangential");
            table.AddColumn("Sagittal");

            int step = Math.Max(1, result.Points.Count / 30);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                var dl = (i < result.DiffractionLimit.Count) ? result.DiffractionLimit[i] : null;
                table.AddRow(
                    pt.SpatialFrequency.ToString("F1"),
                    dl != null ? dl.Tangential.ToString("F6") : "---",
                    pt.Tangential.ToString("F6"),
                    pt.Sagittal.ToString("F6")
                );
            }
            AnsiConsole.Write(table);

            if (outFile != null)
            {
                var rows = new List<string[]>();
                for (int i = 0; i < result.Points.Count; i++)
                {
                    var pt = result.Points[i];
                    var dl = (i < result.DiffractionLimit.Count) ? result.DiffractionLimit[i] : null;
                    rows.Add(new[] {
                        pt.SpatialFrequency.ToString("F6"),
                        dl?.Tangential.ToString("F6") ?? "",
                        pt.Tangential.ToString("F6"),
                        pt.Sagittal.ToString("F6")
                    });
                }
                WriteDataFile(outFile,
                    $"Diffraction MTF - Field {fieldIdx + 1} ({system.Fields[fieldIdx].Y} {FieldUnit(system)}), {system.Wavelengths[wlIdx].Value:F4} um",
                    new[] { $"Cutoff: {result.MaxFrequency:F1} cy/mm", $"Grid: {gridSize}x{gridSize}" },
                    new[] { "Spatial_Frequency", "Diff_Limit", "Tangential", "Sagittal" },
                    rows);
            }
        }

        private void RunFftMtfPoly(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            int gridSize = 256;
            if (argCount > 2 && int.TryParse(args[2], out int gs) && gs > 0)
                gridSize = gs;

            var result = FftMtfCalculator.ComputePolychromatic(system, glassMgr, fieldIdx, gridSize);

            AnsiConsole.MarkupLine($"[bold]Polychromatic FFT MTF - Field {fieldIdx + 1}[/]");
            AnsiConsole.MarkupLine($"Cutoff Frequency: {result.MaxFrequency:F1} cycles/mm");

            var table = new Table();
            table.AddColumn("Freq (cy/mm)");
            table.AddColumn("Diff Limit");
            table.AddColumn("Tangential");
            table.AddColumn("Sagittal");

            int step = Math.Max(1, result.Points.Count / 30);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                var dl = (i < result.DiffractionLimit.Count) ? result.DiffractionLimit[i] : null;
                table.AddRow(
                    pt.SpatialFrequency.ToString("F1"),
                    dl != null ? dl.Tangential.ToString("F6") : "---",
                    pt.Tangential.ToString("F6"),
                    pt.Sagittal.ToString("F6")
                );
            }

            AnsiConsole.Write(table);

            if (outFile != null)
            {
                var wlNames = system.Wavelengths.Select(w => $"{w.Value:F4}um(w={w.Weight})").ToArray();
                var rows = new List<string[]>();
                for (int i = 0; i < result.Points.Count; i++)
                {
                    var pt = result.Points[i];
                    var dl = (i < result.DiffractionLimit.Count) ? result.DiffractionLimit[i] : null;
                    rows.Add(new[] {
                        pt.SpatialFrequency.ToString("F6"),
                        dl?.Tangential.ToString("F6") ?? "",
                        pt.Tangential.ToString("F6"),
                        pt.Sagittal.ToString("F6")
                    });
                }
                WriteDataFile(outFile,
                    $"Polychromatic Diffraction MTF - Field {fieldIdx + 1} ({system.Fields[fieldIdx].Y} {FieldUnit(system)})",
                    new[] { $"Wavelengths: {string.Join(", ", wlNames)}", $"Cutoff: {result.MaxFrequency:F1} cy/mm", $"Grid: {gridSize}x{gridSize}" },
                    new[] { "Spatial_Frequency", "Diff_Limit", "Tangential", "Sagittal" },
                    rows);
            }
        }

        private void RunFftMtfThroughFocus(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            if (system.IsAfocal)
            {
                AnsiConsole.MarkupLine(
                    "[red]Error:[/] FFT MTF vs Focus is not supported for afocal systems.");
                return;
            }

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            // Parse: fft-mtf-focus [grid] [wl|poly] [freq] [range] [steps]
            int gridSize = 256;
            if (argCount > 1 && int.TryParse(args[1], out int gs) && gs > 0)
                gridSize = gs;

            bool polychromatic = false;
            int wlIdx = system.PrimaryWavelengthIndex;
            if (argCount > 2)
            {
                if (args[2].Equals("poly", StringComparison.OrdinalIgnoreCase))
                    polychromatic = true;
                else if (int.TryParse(args[2], out int wi) && wi >= 1 && wi <= system.Wavelengths.Count)
                    wlIdx = wi - 1;
            }

            double spatialFrequency = 20.0;
            if (argCount > 3 && double.TryParse(args[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double sf))
                spatialFrequency = sf;

            double focusRange = 0.5;
            if (argCount > 4 && double.TryParse(args[4], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double fr))
                focusRange = fr;

            int numSteps = 201;
            if (argCount > 5 && int.TryParse(args[5], out int ns) && ns > 2)
                numSteps = ns;

            string modeLabel = polychromatic
                ? $"Polychromatic ({system.Wavelengths.First().Value:F4}-{system.Wavelengths.Last().Value:F4} um)"
                : $"{system.Wavelengths[wlIdx].Value:F4} um";

            AnsiConsole.MarkupLine($"[bold]FFT Through Focus MTF[/] - {Markup.Escape(modeLabel)}");
            AnsiConsole.MarkupLine($"Spatial Frequency: {spatialFrequency:F1} cycles/mm");
            AnsiConsole.MarkupLine($"Focus Range: +/-{focusRange:F3} mm, {numSteps} steps, Grid: {gridSize}x{gridSize}");
            AnsiConsole.WriteLine();

            // Compute for all defined fields
            var allResults = new List<MtfThroughFocusResult>();
            for (int fi = 0; fi < system.Fields.Count; fi++)
            {
                AnsiConsole.MarkupLine($"Computing field {fi + 1} ({system.Fields[fi].Y:F2} {FieldUnit(system)})...");
                MtfThroughFocusResult tfResult;
                if (polychromatic)
                    tfResult = FftMtfCalculator.ComputeThroughFocusPolychromatic(
                        system, glassMgr, fi, spatialFrequency, focusRange, numSteps, gridSize);
                else
                    tfResult = FftMtfCalculator.ComputeThroughFocus(
                        system, glassMgr, fi, spatialFrequency, wlIdx, focusRange, numSteps, gridSize);
                allResults.Add(tfResult);
            }

            // Display table for each field
            foreach (var tfResult in allResults)
            {
                int fi = tfResult.FieldIndex;
                AnsiConsole.MarkupLine($"\n[bold]Field {fi + 1}: {system.Fields[fi].Y:F2} {FieldUnit(system)}[/]");

                var table = new Table();
                table.AddColumn("Focus Shift (mm)");
                table.AddColumn("Tangential");
                table.AddColumn("Sagittal");

                foreach (var pt in tfResult.Points)
                {
                    table.AddRow(
                        pt.FocusShift.ToString("F6"),
                        pt.Tangential.ToString("F6"),
                        pt.Sagittal.ToString("F6")
                    );
                }
                AnsiConsole.Write(table);
            }

            // File export
            if (outFile != null)
            {
                try
                {
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    if (polychromatic)
                        writer.WriteLine($"Polychromatic Diffraction Through Focus MTF");
                    else
                        writer.WriteLine($"Diffraction Through Focus MTF");
                    writer.WriteLine();
                    writer.WriteLine($"Title: {session.CurrentFilePath ?? ""}");
                    writer.WriteLine();
                    if (polychromatic)
                        writer.WriteLine($"Data for {system.Wavelengths.First().Value:F4} to {system.Wavelengths.Last().Value:F4} um.");
                    else
                        writer.WriteLine($"Data for {system.Wavelengths[wlIdx].Value:F4} um.");
                    writer.WriteLine($"Spatial frequency: {spatialFrequency:F4} cycles per mm.");
                    writer.WriteLine($"Defocus Units: Millimeters.");
                    writer.WriteLine($"Grid: {gridSize}x{gridSize}");
                    writer.WriteLine();

                    foreach (var tfResult in allResults)
                    {
                        int fi = tfResult.FieldIndex;
                        writer.WriteLine($"Field: {system.Fields[fi].Y:F2} ({FieldUnit(system)})");
                        writer.WriteLine("\tFocal_Shift\tTangential\tSagittal");
                        foreach (var pt in tfResult.Points)
                        {
                            writer.WriteLine($"\t{pt.FocusShift:F6}\t{pt.Tangential:F6}\t{pt.Sagittal:F6}");
                        }
                        writer.WriteLine();
                    }

                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({allResults.Sum(r => r.Points.Count)} rows)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunFftMtfVsField(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            // Parse: fft-mtf-field [grid] [wl] [outfile]
            // Or:    fft-mtf-field [grid] poly [outfile]
            int gridSize = 256;
            if (argCount > 1 && int.TryParse(args[1], out int gs) && gs > 0)
                gridSize = gs;

            bool poly = false;
            int wlIdx = system.PrimaryWavelengthIndex;
            if (argCount > 2)
            {
                if (args[2].Equals("poly", StringComparison.OrdinalIgnoreCase))
                    poly = true;
                else if (int.TryParse(args[2], out int wi) && wi >= 1 && wi <= system.Wavelengths.Count)
                    wlIdx = wi - 1;
            }

            // Standard frequencies for MTF vs field analysis
            double[] frequencies = { 5, 10, 15, 20, 25, 30 };
            int numFieldPoints = 200;

            string label = poly ? "Polychromatic" : $"{system.Wavelengths[wlIdx].Value:F4} um";
            AnsiConsole.MarkupLine($"[bold]FFT MTF vs Field - {label}[/]");
            AnsiConsole.MarkupLine($"Grid: {gridSize}x{gridSize}, Field points: {numFieldPoints + 1}");
            AnsiConsole.MarkupLine($"Frequencies: {string.Join(", ", frequencies.Select(f => $"{f:F0}"))} cy/mm");

            var result = FftMtfCalculator.ComputeVsFieldMultiFreq(system, glassMgr,
                frequencies, wlIdx, gridSize, numFieldPoints, poly);

            // Console: show summary table at key field positions
            var table = new Table();
            table.AddColumn($"Field ({FieldUnit(system)})");
            table.AddColumn("Rel Field");
            foreach (var freq in frequencies)
            {
                table.AddColumn($"T {freq:F0}");
                table.AddColumn($"S {freq:F0}");
            }

            int step = Math.Max(1, result.Points.Count / 20);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                double relField = result.MaxFieldY > 0 ? pt.fieldY / result.MaxFieldY : 0;
                var row = new List<string> { pt.fieldY.ToString("F2"), relField.ToString("F3") };
                foreach (var fv in pt.Item2)
                {
                    row.Add(fv.tang.ToString("F4"));
                    row.Add(fv.sag.ToString("F4"));
                }
                table.AddRow(row.ToArray());
            }
            AnsiConsole.Write(table);

            // File export: complete data
            if (outFile != null)
            {
                try
                {
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"FFT MTF vs Field - {label}");
                    writer.WriteLine($"Maximum Y field: {result.MaxFieldY:F4} {FieldUnit(system)}");
                    writer.WriteLine($"Grid: {gridSize}x{gridSize}");
                    writer.WriteLine();

                    // One section per frequency
                    foreach (var freq in frequencies)
                    {
                        int fIdx = Array.IndexOf(frequencies, freq);
                        writer.WriteLine($"Data for spatial frequency: {freq:F4} cycles per mm.");
                        writer.WriteLine("Relative_Field\tTangential\tSagittal");
                        foreach (var pt in result.Points)
                        {
                            double relField = result.MaxFieldY > 0 ? pt.fieldY / result.MaxFieldY : 0;
                            writer.WriteLine($"{relField:F5}\t{pt.Item2[fIdx].tang:F6}\t{pt.Item2[fIdx].sag:F6}");
                        }
                        writer.WriteLine();
                    }

                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({result.Points.Count} field points x {frequencies.Length} frequencies)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunGeoMtf(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            bool polychromatic = false;
            int wlIdx = system.PrimaryWavelengthIndex;
            if (argCount > 2)
            {
                if (args[2].Equals("poly", StringComparison.OrdinalIgnoreCase))
                    polychromatic = true;
                else if (int.TryParse(args[2], out int wi) && wi >= 1 && wi <= system.Wavelengths.Count)
                    wlIdx = wi - 1;
            }

            MtfResult result;
            string modeLabel;
            if (polychromatic)
            {
                result = GeometricMtfKidger.ComputePolychromatic(system, glassMgr, fieldIdx);
                modeLabel = "Polychromatic";
            }
            else
            {
                result = GeometricMtfKidger.Compute(system, glassMgr, fieldIdx, wlIdx);
                modeLabel = $"{system.Wavelengths[wlIdx].Value:F4} um";
            }

            AnsiConsole.MarkupLine($"[bold]Geometric MTF - Field {fieldIdx + 1}, {Markup.Escape(modeLabel)}[/]");
            AnsiConsole.MarkupLine($"Cutoff Frequency: {result.MaxFrequency:F1} cycles/mm");
            AnsiConsole.MarkupLine($"Multiply by Diffraction Limit: Yes");

            var table = new Table();
            table.AddColumn("Frequency (cy/mm)");
            table.AddColumn("Diff Limit");
            table.AddColumn("Tangential");
            table.AddColumn("Sagittal");

            foreach (var pt in result.Points)
            {
                var dl = result.DiffractionLimit.Count > result.Points.IndexOf(pt)
                    ? result.DiffractionLimit[result.Points.IndexOf(pt)] : null;
                table.AddRow(
                    pt.SpatialFrequency.ToString("F1"),
                    dl != null ? dl.Tangential.ToString("F6") : "---",
                    pt.Tangential.ToString("F6"),
                    pt.Sagittal.ToString("F6")
                );
            }

            AnsiConsole.Write(table);

            if (outFile != null)
            {
                var rows = new List<string[]>();
                for (int i = 0; i < result.Points.Count; i++)
                {
                    var pt = result.Points[i];
                    var dl = (i < result.DiffractionLimit.Count) ? result.DiffractionLimit[i] : null;
                    rows.Add(new[] {
                        pt.SpatialFrequency.ToString("F6"),
                        dl?.Tangential.ToString("F6") ?? "",
                        pt.Tangential.ToString("F6"),
                        pt.Sagittal.ToString("F6")
                    });
                }

                string title = polychromatic
                    ? $"Polychromatic Geometric MTF - Field {fieldIdx + 1} ({system.Fields[fieldIdx].Y} {FieldUnit(system)})"
                    : $"Geometric MTF - Field {fieldIdx + 1} ({system.Fields[fieldIdx].Y} {FieldUnit(system)}), {system.Wavelengths[wlIdx].Value:F4} um";

                WriteDataFile(outFile, title,
                    new[] { $"Multiply by Diffraction Limit: Yes",
                            $"Cutoff: {result.MaxFrequency:F1} cy/mm" },
                    new[] { "Spatial_Frequency", "Diff_Limit", "Tangential", "Sagittal" },
                    rows);
            }
        }

        private void RunGeoMtfVsField(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            // Parse: geo-mtf-field [npts] [wl|poly] [outfile]
            int numFieldPoints = 10;
            if (argCount > 1 && int.TryParse(args[1], out int npts) && npts > 0)
                numFieldPoints = Math.Min(npts, 100);

            bool poly = false;
            int wlIdx = system.PrimaryWavelengthIndex;
            if (argCount > 2)
            {
                if (args[2].Equals("poly", StringComparison.OrdinalIgnoreCase))
                    poly = true;
                else if (int.TryParse(args[2], out int wi) && wi >= 1 && wi <= system.Wavelengths.Count)
                    wlIdx = wi - 1;
            }

            // Standard frequencies for geometric MTF vs field
            double[] frequencies = { 10, 20, 30, 40, 50, 60 };

            string label = poly ? "Polychromatic" : $"{system.Wavelengths[wlIdx].Value:F4} um";
            AnsiConsole.MarkupLine($"[bold]Geometric MTF vs Field - {Markup.Escape(label)}[/]");
            AnsiConsole.MarkupLine($"Rings: 30, Field points: {numFieldPoints + 1}, Max: 100");
            AnsiConsole.MarkupLine($"Frequencies: {string.Join(", ", frequencies.Select(f => $"{f:F0}"))} cy/mm");

            var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(system, glassMgr,
                frequencies, wlIdx, numRings: 30, numFieldPoints: numFieldPoints,
                multiplyByDiffractionLimit: true, polychromatic: poly);

            // Console: show summary table at key field positions
            var table = new Table();
            table.AddColumn($"Field ({FieldUnit(system)})");
            table.AddColumn("Rel Field");
            foreach (var freq in frequencies)
            {
                table.AddColumn($"T {freq:F0}");
                table.AddColumn($"S {freq:F0}");
            }

            int step = Math.Max(1, result.Points.Count / 20);
            for (int i = 0; i < result.Points.Count; i += step)
            {
                var pt = result.Points[i];
                double relField = result.MaxFieldY > 0 ? pt.fieldY / result.MaxFieldY : 0;
                var row = new List<string> { pt.fieldY.ToString("F2"), relField.ToString("F3") };
                foreach (var fv in pt.Item2)
                {
                    row.Add(fv.tang.ToString("F4"));
                    row.Add(fv.sag.ToString("F4"));
                }
                table.AddRow(row.ToArray());
            }
            AnsiConsole.Write(table);

            // File export
            if (outFile != null)
            {
                try
                {
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"Geometric MTF vs Field - {label}");
                    writer.WriteLine($"Maximum Y field: {result.MaxFieldY:F4} {FieldUnit(system)}");
                    writer.WriteLine($"Pupil sampling: 128x128");
                    writer.WriteLine($"Multiply by diffraction limit: Yes");
                    writer.WriteLine();

                    // One section per frequency
                    foreach (var freq in frequencies)
                    {
                        int fIdx = Array.IndexOf(frequencies, freq);
                        writer.WriteLine($"Spatial frequency: {freq:F4} cycles/mm");
                        writer.WriteLine("Field_Deg\tRelative_Field\tTangential\tSagittal");
                        foreach (var pt in result.Points)
                        {
                            double relField = result.MaxFieldY > 0 ? pt.fieldY / result.MaxFieldY : 0;
                            writer.WriteLine($"{pt.fieldY:F6}\t{relField:F5}\t{pt.Item2[fIdx].tang:F6}\t{pt.Item2[fIdx].sag:F6}");
                        }
                        writer.WriteLine();
                    }

                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({result.Points.Count} field points x {frequencies.Length} frequencies)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunGeoMtfThroughFocus(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            if (system.IsAfocal)
            {
                AnsiConsole.MarkupLine(
                    "[red]Error:[/] Geometric MTF vs Focus is not supported for afocal systems.");
                return;
            }

            string? outFile = ExtractOutputFile(args);
            int argCount = outFile != null ? args.Length - 1 : args.Length;

            // Parse: geo-mtf-focus [field_num] [freq] [range] [steps]
            int fieldFilter = -1; // -1 = all fields
            if (argCount > 1 && int.TryParse(args[1], out int fn) && fn >= 1 && fn <= system.Fields.Count)
                fieldFilter = fn - 1;

            double spatialFrequency = 30.0;
            if (argCount > 2 && double.TryParse(args[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double sf))
                spatialFrequency = sf;

            double focusRange = 0.1;
            if (argCount > 3 && double.TryParse(args[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double fr))
                focusRange = fr;

            int numSteps = 21;
            if (argCount > 4 && int.TryParse(args[4], out int ns) && ns > 2)
                numSteps = ns;

            int wlIdx = system.PrimaryWavelengthIndex;

            AnsiConsole.MarkupLine($"[bold]Geometric MTF Through Focus[/] - {system.Wavelengths[wlIdx].Value:F4} um");
            AnsiConsole.MarkupLine($"Spatial Frequency: {spatialFrequency:F1} cycles/mm");
            AnsiConsole.MarkupLine($"Focus Range: +/-{focusRange:F3} mm, {numSteps} steps");
            AnsiConsole.WriteLine();

            int startField = fieldFilter >= 0 ? fieldFilter : 0;
            int endField = fieldFilter >= 0 ? fieldFilter + 1 : system.Fields.Count;

            var allResults = new List<MtfThroughFocusResult>();
            for (int fi = startField; fi < endField; fi++)
            {
                AnsiConsole.MarkupLine($"Computing field {fi + 1} ({system.Fields[fi].Y:F2} {FieldUnit(system)})...");
                var tfResult = GeometricMtfKidger.ComputeThroughFocus(
                    system, glassMgr, fi, 30, wlIdx, focusRange, numSteps);
                allResults.Add(tfResult);
            }

            foreach (var tfResult in allResults)
            {
                int fi = tfResult.FieldIndex;
                AnsiConsole.MarkupLine($"\n[bold]Field {fi + 1}: {system.Fields[fi].Y:F2} {FieldUnit(system)}[/]");

                var table = new Table();
                table.AddColumn("Focus Shift (mm)");
                table.AddColumn("Tangential");
                table.AddColumn("Sagittal");

                foreach (var pt in tfResult.Points)
                {
                    table.AddRow(
                        pt.FocusShift.ToString("F6"),
                        pt.Tangential.ToString("F6"),
                        pt.Sagittal.ToString("F6")
                    );
                }
                AnsiConsole.Write(table);
            }

            if (outFile != null)
            {
                try
                {
                    using var writer = new StreamWriter(outFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine("Geometric MTF Through Focus");
                    writer.WriteLine();
                    writer.WriteLine($"Title: {session.CurrentFilePath ?? ""}");
                    writer.WriteLine();
                    writer.WriteLine($"Data for {system.Wavelengths[wlIdx].Value:F4} um.");
                    writer.WriteLine($"Spatial frequency: {spatialFrequency:F4} cycles per mm.");
                    writer.WriteLine($"Defocus Units: Millimeters.");
                    writer.WriteLine();

                    foreach (var tfResult in allResults)
                    {
                        int fi = tfResult.FieldIndex;
                        writer.WriteLine($"Field: {system.Fields[fi].Y:F2} ({FieldUnit(system)})");
                        writer.WriteLine("\tFocal_Shift\tTangential\tSagittal");
                        foreach (var pt in tfResult.Points)
                        {
                            writer.WriteLine($"\t{pt.FocusShift:F6}\t{pt.Tangential:F6}\t{pt.Sagittal:F6}");
                        }
                        writer.WriteLine();
                    }

                    AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(outFile)} ({allResults.Sum(r => r.Points.Count)} rows)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunZernike(Session session, string[] args, bool fringe)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int fieldIdx = ParseFieldIndex(args, system);
            if (fieldIdx < 0) return;

            int primaryWl = system.PrimaryWavelengthIndex;
            int numTerms = 16;

            var result = fringe
                ? ZernikeCalculator.ComputeFringe(system, glassMgr, fieldIdx, primaryWl, numTerms)
                : ZernikeCalculator.ComputeStandard(system, glassMgr, fieldIdx, primaryWl, numTerms);

            string type = fringe ? "Fringe" : "Standard";
            AnsiConsole.MarkupLine($"[bold]{type} Zernike Coefficients - Field {fieldIdx + 1}[/]");
            AnsiConsole.MarkupLine($"RMS Wavefront: {result.RmsWavefront:F4} waves");
            AnsiConsole.MarkupLine($"P-V Wavefront: {result.PeakToValley:F4} waves");
            AnsiConsole.MarkupLine($"RMS Fit Residual: {result.RmsFit:F6} waves");

            var table = new Table();
            table.AddColumn("Term");
            table.AddColumn("Name");
            table.AddColumn("Coefficient (waves)");

            for (int i = 0; i < result.Coefficients.Length; i++)
            {
                string name = fringe
                    ? ZernikeCalculator.GetFringeTermName(i + 1)
                    : ZernikeCalculator.GetStandardTermName(i + 1);
                table.AddRow(
                    $"Z{i + 1}",
                    name,
                    result.Coefficients[i].ToString("F6")
                );
            }

            AnsiConsole.Write(table);
        }

        private void RunLateralColor(Session session)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            var result = LateralColorCalculator.Compute(system, glassMgr);

            AnsiConsole.MarkupLine("[bold]Lateral Color[/]");
            AnsiConsole.MarkupLine($"Max Lateral Color: {result.MaxLateralColor:E4} mm");

            var table = new Table();
            table.AddColumn("Field Y");
            table.AddColumn("Lateral Color (mm)");

            foreach (var pt in result.Points)
            {
                table.AddRow(
                    pt.FieldY.ToString("F3"),
                    pt.LateralShift.ToString("E4")
                );
            }

            AnsiConsole.Write(table);
        }

        private void RunRayTrace(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            // Parse: ray-trace [field] [px] [py] [wave] [outfile]
            int fieldNum = args.Length > 1 && int.TryParse(args[1], out int f) ? f : 1;
            double px = args.Length > 2 && double.TryParse(args[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pxv) ? pxv : 0;
            double py = args.Length > 3 && double.TryParse(args[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pyv) ? pyv : 0;
            int waveNum = args.Length > 4 && int.TryParse(args[4], out int w) ? w : -1;
            // outfile is the last arg if it's not a number
            string outFile = null;
            if (args.Length > 5)
                outFile = args[5];
            else if (args.Length > 4 && !int.TryParse(args[4], out _))
                outFile = args[4];
            int waveIdx = waveNum > 0 ? waveNum - 1 : -1;

            int fieldIdx = Math.Max(0, fieldNum - 1);
            if (fieldIdx >= system.Fields.Count) fieldIdx = 0;
            double fieldY = system.Fields[fieldIdx].Y;
            string fieldUnit = FieldUnit(system);

            var result = RayTraceListing.Trace(system, glassMgr, fieldY, px, py, waveIdx);

            // Build text output
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Real Ray Trace Listing");
            sb.AppendLine($"Field {fieldNum}: {fieldY} {fieldUnit}, Px={px}, Py={py}");
            sb.AppendLine($"Wavelength: {result.Wavelength:F4} um (primary)");
            sb.AppendLine($"Success: {result.Success}");
            sb.AppendLine();

            if (result.Success)
            {
                sb.AppendLine(string.Format("{0,5} {1,16} {2,16} {3,16} {4,16} {5,16} {6,16} {7,16} {8,16} {9,16} {10,14} {11,14} {12,16}",
                    "Surf", "X", "Y", "Z", "L", "M", "N", "Ln", "Mn", "Nn", "Path", "OPL", "Cumul OPL"));
                sb.AppendLine(new string('-', 203));

                foreach (var s in result.Surfaces)
                {
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0,5} {1,16:E10} {2,16:E10} {3,16:E10} {4,16:E10} {5,16:E10} {6,16:E10} {7,16:E10} {8,16:E10} {9,16:E10} {10,14:F6} {11,14:F6} {12,16:F6}",
                        s.SurfaceIndex, s.X, s.Y, s.Z, s.L, s.M, s.N, s.Ln, s.Mn, s.Nn, s.PathLength, s.OPL, s.CumulativeOPL));
                }
            }
            else
            {
                sb.AppendLine("Ray trace failed.");
            }

            if (outFile != null)
            {
                File.WriteAllText(outFile, sb.ToString());
                AnsiConsole.MarkupLine($"[green]Written to: {Markup.Escape(outFile)}[/]");
            }
            else
            {
                AnsiConsole.WriteLine(sb.ToString());
            }
        }

        private void RunRelativeIllumination(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            int numFieldPoints = ParseIntFlag(args, "--field-pts", 50);
            int numPupilRays = ParseIntFlag(args, "--pupil-rays", 36);

            var result = RelativeIlluminationCalculator.Compute(system, glassMgr,
                numFieldPoints: numFieldPoints, numPupilRays: numPupilRays);

            AnsiConsole.MarkupLine("[bold]Relative Illumination[/]");

            var table = new Table();
            table.AddColumn("Field Y");
            table.AddColumn("Relative Illumination");

            foreach (var pt in result.Points)
            {
                table.AddRow(
                    pt.FieldY.ToString("F3"),
                    pt.RelativeIllumination.ToString("F4")
                );
            }

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// Parse an integer-valued flag from CLI args. Accepts both --flag=N
        /// and --flag N. Returns the default if absent or unparseable.
        /// </summary>
        private static int ParseIntFlag(string[] args, string flag, int defaultValue)
        {
            string equalsForm = flag + "=";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(equalsForm))
                {
                    if (int.TryParse(args[i].Substring(equalsForm.Length), out int v)) return v;
                }
                else if (args[i] == flag && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int v)) return v;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Check if the last argument is a file path for data export.
        /// Returns the path if it ends with .txt/.tsv/.csv/.dat, null otherwise.
        /// </summary>
        private static string? ExtractOutputFile(string[] args)
        {
            if (args.Length < 2) return null;
            string last = args[args.Length - 1];
            string ext = Path.GetExtension(last).ToLowerInvariant();
            if (ext == ".txt" || ext == ".tsv" || ext == ".csv" || ext == ".dat")
                return last;
            return null;
        }

        /// <summary>
        /// Write tab-separated data to a file with metadata header.
        /// </summary>
        private static void WriteDataFile(string path, string title,
            string[] metadata, string[] columns, List<string[]> rows)
        {
            try
            {
                using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
                writer.WriteLine(title);
                foreach (var line in metadata)
                    writer.WriteLine(line);
                writer.WriteLine();
                writer.WriteLine(string.Join("\t", columns));
                foreach (var row in rows)
                    writer.WriteLine(string.Join("\t", row));
                AnsiConsole.MarkupLine($"[green]Data written to: {Markup.Escape(path)} ({rows.Count} rows)[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to write file: {Markup.Escape(ex.Message)}[/]");
            }
        }

        private int ParseFieldIndex(string[] args, Core.Models.OpticalSystem system)
        {
            // Field numbers are 1-based in the CLI
            int fieldNum = 1; // default to field 1
            if (args.Length > 1 && int.TryParse(args[1], out int fi))
                fieldNum = fi;

            // Convert from 1-based CLI input to 0-based internal index
            int fieldIdx = fieldNum - 1;
            if (fieldIdx < 0 || fieldIdx >= system.Fields.Count)
            {
                AnsiConsole.MarkupLine($"[red]Field {fieldNum} out of range. Valid range: 1-{system.Fields.Count}.[/]");
                return -1;
            }
            return fieldIdx;
        }

        private void RunRender(Session session, string[] args)
        {
            // render <analysis> [outfile]
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: render <analysis> [outfile.html][/]");
                AnsiConsole.MarkupLine("Analyses: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor, fieldcurvature, distortion, fftmtf, wavefront");
                return;
            }

            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            // Parse flags
            bool openBrowser = false;
            bool fromObject = false;
            int layoutWaveIdx = -1;  // -1 = primary (default); 1-based in CLI
            var filteredArgs = new List<string>();
            for (int a = 1; a < args.Length; a++)
            {
                if (args[a] == "--open") openBrowser = true;
                else if (args[a] == "--from-object") fromObject = true;
                else if (args[a].StartsWith("--wave="))
                {
                    if (int.TryParse(args[a].Substring("--wave=".Length), out int w))
                        layoutWaveIdx = w - 1;  // CLI 1-based → engine 0-based
                }
                else if (args[a] == "--wave" && a + 1 < args.Length)
                {
                    if (int.TryParse(args[a + 1], out int w))
                        layoutWaveIdx = w - 1;
                    a++;
                }
                else filteredArgs.Add(args[a]);
            }

            string analysis = filteredArgs.Count > 0 ? filteredArgs[0].ToLowerInvariant() : "";
            string outFile = filteredArgs.Count > 1 ? filteredArgs[1] : analysis + ".html";

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string html;

            switch (analysis)
            {
                case "spot":
                {
                    var results = new Core.Analysis.SpotDiagramResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = Core.Analysis.SpotDiagram.Compute(system, glassMgr, f,
                            numRings: 6, numArms: 12, wavelengthIndex: layoutWaveIdx);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    string spotTitle = layoutWaveIdx >= 0 && layoutWaveIdx < system.Wavelengths.Count
                        ? $"Spot Diagram (\u03BB = {system.Wavelengths[layoutWaveIdx].Value:G4} \u00B5m)"
                        : "Spot Diagram";
                    html = Rendering.SpotDiagramRenderer.RenderPage(results, labels, spotTitle);
                    break;
                }
                case "rayfan":
                {
                    var results = new Core.Analysis.RayFanResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = Core.Analysis.TransverseRayFan.Compute(system, glassMgr, f);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = Rendering.RayFanRenderer.RenderPage(results, labels, "Transverse Ray Fan",
                        numWavelengths: system.Wavelengths.Count);
                    break;
                }
                case "opdfan":
                {
                    var results = new Core.Analysis.OpdFanResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = Core.Analysis.OpdFan.Compute(system, glassMgr, f);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = Rendering.OpdFanRenderer.RenderPage(results, labels, "OPD Fan",
                        numWavelengths: system.Wavelengths.Count);
                    break;
                }
                case "seidel":
                {
                    var result = Core.Analysis.SeidelCalculator.Calculate(system, glassMgr);
                    html = Rendering.SeidelRenderer.RenderPage(result, "Seidel Coefficients");
                    break;
                }
                case "systemdata":
                case "paraxial":
                {
                    var result = Core.Analysis.SystemDataCalculator.Calculate(system, glassMgr);
                    html = Rendering.SystemDataRenderer.RenderPage(result, "System Data");
                    break;
                }
                case "layout":
                {
                    var layout = Core.Analysis.SystemLayout.ComputeLayout(system, glassMgr,
                        numRays: 15, startFromSurface1: !fromObject, wavelengthIndex: layoutWaveIdx);
                    var layoutFieldYs = system.Fields.Select(f => f.Y).ToList();
                    string layoutFieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
                    html = Rendering.SystemLayoutRenderer.RenderPage(layout, system.Title ?? "System Layout",
                        fieldYs: layoutFieldYs, fieldUnit: layoutFieldUnit);
                    break;
                }
                case "relillum":
                {
                    int riFieldPts = ParseIntFlag(args, "--field-pts", 50);
                    int riPupilRays = ParseIntFlag(args, "--pupil-rays", 36);
                    var result = Core.Analysis.RelativeIlluminationCalculator.Compute(system, glassMgr,
                        numFieldPoints: riFieldPts, numPupilRays: riPupilRays);
                    html = Rendering.RelativeIlluminationRenderer.RenderPage(result, "Relative Illumination");
                    break;
                }
                case "lateralcolor":
                {
                    var result = Core.Analysis.LateralColorCalculator.Compute(system, glassMgr);
                    var wlLabels = new string[system.Wavelengths.Count];
                    for (int i = 0; i < system.Wavelengths.Count; i++)
                        wlLabels[i] = $"{system.Wavelengths[i].Value:F4} \u00b5m";
                    double maxField = 0;
                    foreach (var f in system.Fields)
                        if (System.Math.Abs(f.Y) > maxField) maxField = System.Math.Abs(f.Y);
                    html = Rendering.LateralColorRenderer.RenderPage(result, "Lateral Color",
                        maxField, system.Wavelengths.Count, wlLabels, fieldUnit: fieldUnit);
                    break;
                }
                case "fieldcurvature":
                {
                    var result = Core.Analysis.FieldCurvatureCalculator.Compute(system, glassMgr);
                    html = Rendering.FieldCurvatureRenderer.RenderPage(result, "Field Curvature");
                    break;
                }
                case "distortion":
                {
                    var result = Core.Analysis.DistortionCalculator.Compute(system, glassMgr);
                    html = Rendering.DistortionRenderer.RenderPage(result, "Distortion");
                    break;
                }
                case "fftmtf":
                {
                    var results = new Core.Analysis.MtfResult[system.Fields.Count][];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = new[] { Core.Analysis.FftMtfCalculator.ComputePolychromatic(system, glassMgr, f) };
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = Rendering.FftMtfRenderer.RenderPage(results, labels, "FFT MTF (Polychromatic)");
                    break;
                }
                case "wavefront":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var results = new WavefrontResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = WavefrontMapCalculator.Compute(system, glassMgr, f, wIdx);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = WavefrontMapRenderer.RenderPage(results, labels, "Wavefront Map");
                    break;
                }
                case "geomtf":
                {
                    var results = new MtfResult[system.Fields.Count][];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = new[] { GeometricMtfKidger.Compute(system, glassMgr, f,
                            system.PrimaryWavelengthIndex) };
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = FftMtfRenderer.RenderPage(results, labels, "Geometric MTF");
                    break;
                }
                case "fftmtf-field":
                {
                    double[] freqs = { 10, 20, 30, 40 };
                    var result = FftMtfCalculator.ComputeVsFieldMultiFreq(system, glassMgr,
                        freqs, system.PrimaryWavelengthIndex, 256, 200, polychromatic: true);
                    html = MtfVsFieldRenderer.RenderPage(result, "FFT MTF vs Field");
                    break;
                }
                case "fftmtf-focus":
                {
                    var results = new MtfThroughFocusResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = FftMtfCalculator.ComputeThroughFocusPolychromatic(
                            system, glassMgr, f, 30, 0.1, 21, 64);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = "<!DOCTYPE html><html><body>" +
                        MtfThroughFocusRenderer.RenderAllFields(results, labels, "FFT MTF vs Focus") +
                        "</body></html>";
                    break;
                }
                case "geomtf-field":
                {
                    double[] freqs = { 10, 20, 30, 40 };
                    var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(system, glassMgr,
                        freqs, system.PrimaryWavelengthIndex);
                    html = MtfVsFieldRenderer.RenderPage(result, "Geometric MTF vs Field");
                    break;
                }
                case "geomtf-focus":
                {
                    var results = new MtfThroughFocusResult[system.Fields.Count];
                    var labels = new string[system.Fields.Count];
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        results[f] = GeometricMtfKidger.ComputeThroughFocus(system, glassMgr,
                            f, 30, system.PrimaryWavelengthIndex, 0.1, 21);
                        labels[f] = $"{system.Fields[f].Y} {fieldUnit}";
                    }
                    html = "<!DOCTYPE html><html><body>" +
                        MtfThroughFocusRenderer.RenderAllFields(results, labels, "Geometric MTF vs Focus") +
                        "</body></html>";
                    break;
                }
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown analysis: {analysis}[/]");
                    AnsiConsole.MarkupLine("Valid: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor, fieldcurvature, distortion, fftmtf, fftmtf-field, fftmtf-focus, geomtf, geomtf-field, geomtf-focus, wavefront");
                    return;
            }

            File.WriteAllText(outFile, html);
            AnsiConsole.MarkupLine($"[green]Rendered to: {Markup.Escape(outFile)}[/]");

            if (openBrowser)
            {
                try
                {
                    var fullPath = Path.GetFullPath(outFile);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Could not open browser: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        private void RunShow(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: show <analysis> [--save [outfile.png]] [--wave N] [--max-freq F] [--field-pts N] [--pupil-rays N][/]");
                AnsiConsole.MarkupLine("Analyses: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor, fieldcurvature, distortion, fftmtf, fftmtf-field, fftmtf-focus, fftpsf, geomtf, geomtf-field, geomtf-focus, wavefront, chromaticfocalshift");
                AnsiConsole.MarkupLine("Pops up the LensHH-LT Render window with the analysis. Pass --save to also write a PNG (auto-named <analysis>.png if no path given). --max-freq F (cycles/mm) caps the X axis on fftmtf; 0 (default) uses the diffraction cutoff. --field-pts / --pupil-rays control relillum smoothness (more = smoother, slower).");
                return;
            }

            var system = session.EnsureValidSystem();

            // Parse flags. --save can stand alone (auto-name) or take an
            // explicit path immediately after.
            int layoutWaveIdx = -1;
            string? savePath = null;
            bool saveRequested = false;
            double maxFreq = 0;
            var filteredArgs = new List<string>();
            for (int a = 0; a < args.Length; a++)
            {
                if (args[a].StartsWith("--wave="))
                {
                    if (int.TryParse(args[a].Substring("--wave=".Length), out int w))
                        layoutWaveIdx = w - 1;
                }
                else if (args[a] == "--wave" && a + 1 < args.Length)
                {
                    if (int.TryParse(args[a + 1], out int w))
                        layoutWaveIdx = w - 1;
                    a++;
                }
                else if (args[a].StartsWith("--max-freq="))
                {
                    double.TryParse(args[a].Substring("--max-freq=".Length),
                        System.Globalization.CultureInfo.InvariantCulture, out maxFreq);
                }
                else if (args[a] == "--max-freq" && a + 1 < args.Length)
                {
                    double.TryParse(args[a + 1],
                        System.Globalization.CultureInfo.InvariantCulture, out maxFreq);
                    a++;
                }
                else if (args[a].StartsWith("--save="))
                {
                    saveRequested = true;
                    savePath = args[a].Substring("--save=".Length);
                }
                else if (args[a] == "--save")
                {
                    saveRequested = true;
                    // Take the next token as the path only if it doesn't
                    // look like another flag and isn't the analysis name
                    // already consumed.
                    if (a + 1 < args.Length && !args[a + 1].StartsWith("--"))
                    {
                        savePath = args[a + 1];
                        a++;
                    }
                }
                else filteredArgs.Add(args[a]);
            }

            string analysis = filteredArgs.Count > 1 ? filteredArgs[1].ToLowerInvariant() : "";

            // Map CLI-style names to RenderApp analysis keys
            string renderAnalysis = analysis switch
            {
                "spot" => "SpotDiagram",
                "rayfan" => "RayFan",
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
            {
                AnsiConsole.MarkupLine($"[red]Unknown analysis: {Markup.Escape(analysis)}[/]");
                AnsiConsole.MarkupLine("Valid: spot, rayfan, opdfan, seidel, layout, relillum, lateralcolor, fieldcurvature, distortion, fftmtf, fftmtf-field, fftmtf-focus, fftpsf, geomtf, geomtf-field, geomtf-focus, wavefront, chromaticfocalshift");
                return;
            }

            // Resolve PNG path if --save was passed without an explicit
            // path: auto-name as <analysis>.png in the current dir.
            string? resolvedSavePath = null;
            if (saveRequested)
            {
                var raw = string.IsNullOrWhiteSpace(savePath) ? analysis + ".png" : savePath!;
                resolvedSavePath = Path.GetFullPath(raw);
            }

            var parms = new Dictionary<string, object>();
            if (resolvedSavePath != null) parms["SavePngPath"] = resolvedSavePath;
            if (layoutWaveIdx >= 0) parms["WavelengthIndex"] = layoutWaveIdx;
            if (maxFreq > 0) parms["MaxFrequency"] = maxFreq;
            // Honored by the RelativeIllumination case in AnalysisDispatcher.
            int riFieldPts = ParseIntFlag(args, "--field-pts", 0);
            int riPupilRays = ParseIntFlag(args, "--pupil-rays", 0);
            if (riFieldPts > 0) parms["NumFieldPoints"] = riFieldPts;
            if (riPupilRays > 0) parms["NumPupilRays"] = riPupilRays;
            var response = RenderAppClient.Send(system, renderAnalysis, parms);

            if (!response.Success)
            {
                AnsiConsole.MarkupLine($"[red]Render error: {Markup.Escape(response.Error ?? "unknown")}[/]");
                return;
            }

            if (resolvedSavePath != null)
                AnsiConsole.MarkupLine($"[green]Shown in render window. PNG saved to: {Markup.Escape(resolvedSavePath)}[/]");
            else
                AnsiConsole.MarkupLine("[green]Shown in render window.[/]");
        }

        private void RunExportText(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: export-text <analysis> [outfile.txt][/]");
                AnsiConsole.MarkupLine("Analyses: spot, rayfan, opdfan, fftmtf, seidel, lateralcolor, fieldcurvature, distortion, fftmtf-field, fftpsf, relillum, chromaticshift, fftmtf-focus, geomtf, geomtf-field, geomtf-focus, wavefront");
                return;
            }

            var system = session.EnsureValidSystem();
            var glassMgr = session.EnsureGlassCatalog();

            string analysis = args[1].ToLowerInvariant();
            string outFile = args.Length > 2 ? args[2] : analysis + ".txt";
            string fieldUnit = FieldUnit(system);

            string text;
            switch (analysis)
            {
                case "spot":
                {
                    var wls = system.Wavelengths.Select(w => w.Value).ToArray();
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = SpotDiagram.Compute(system, glassMgr, f);
                        sb.Append(SpotDiagramTextExport.Export(result,
                            $"Spot Diagram \u2014 F{f + 1}", wls, system.Fields[f].Y, fieldUnit));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "rayfan":
                {
                    var wls = system.Wavelengths.Select(w => w.Value).ToArray();
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = TransverseRayFan.Compute(system, glassMgr, f);
                        sb.Append(RayFanTextExport.Export(result,
                            $"Ray Fan \u2014 F{f + 1}", wls, system.Fields[f].Y, fieldUnit));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "opdfan":
                {
                    var wls = system.Wavelengths.Select(w => w.Value).ToArray();
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = OpdFan.Compute(system, glassMgr, f);
                        sb.Append(OpdFanTextExport.Export(result,
                            $"OPD Fan \u2014 F{f + 1}", wls, system.Fields[f].Y, fieldUnit));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "fftmtf":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = FftMtfCalculator.ComputeVsFrequency(system, glassMgr, f, wIdx);
                        sb.Append(FftMtfTextExport.Export(result,
                            $"FFT MTF \u2014 F{f + 1} W{wIdx + 1}",
                            fieldValue: system.Fields[f].Y,
                            wavelengthUm: system.Wavelengths[wIdx].Value,
                            fieldUnit: fieldUnit, isAfocal: system.IsAfocal));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "seidel":
                {
                    var result = SeidelCalculator.Calculate(system, glassMgr);
                    text = SeidelTextExport.Export(result, "Seidel Coefficients");
                    break;
                }
                case "systemdata":
                case "paraxial":
                {
                    var result = SystemDataCalculator.Calculate(system, glassMgr);
                    text = SystemDataTextExport.Export(result, "System Data");
                    break;
                }
                case "lateralcolor":
                {
                    var wls = system.Wavelengths.Select(w => w.Value).ToArray();
                    var result = LateralColorCalculator.Compute(system, glassMgr);
                    text = LateralColorTextExport.Export(result, "Lateral Color", wls, fieldUnit);
                    break;
                }
                case "fieldcurvature":
                {
                    var result = FieldCurvatureCalculator.Compute(system, glassMgr);
                    text = FieldCurvatureTextExport.Export(result, "Field Curvature", fieldUnit);
                    break;
                }
                case "distortion":
                {
                    var result = DistortionCalculator.Compute(system, glassMgr);
                    text = DistortionTextExport.Export(result, "Distortion", fieldUnit);
                    break;
                }
                case "fftmtf-field":
                {
                    double[] freqs = { 10, 20, 30, 40 };
                    var result = FftMtfCalculator.ComputeVsFieldMultiFreq(system, glassMgr,
                        freqs, system.PrimaryWavelengthIndex, 256, 200, polychromatic: true);
                    text = MtfVsFieldTextExport.Export(result, "FFT MTF vs Field", fieldUnit, system.IsAfocal);
                    break;
                }
                case "fftpsf":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = FftPsfCalculator.Compute(system, glassMgr, f, wIdx, 64);
                        sb.Append(FftPsfTextExport.Export(result, $"FFT PSF \u2014 F{f + 1} W{wIdx + 1}"));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "relillum":
                {
                    int riFieldPts = ParseIntFlag(args, "--field-pts", 50);
                    int riPupilRays = ParseIntFlag(args, "--pupil-rays", 36);
                    var result = RelativeIlluminationCalculator.Compute(system, glassMgr,
                        numFieldPoints: riFieldPts, numPupilRays: riPupilRays);
                    text = RelativeIlluminationTextExport.Export(result, "Relative Illumination", fieldUnit);
                    break;
                }
                case "chromaticshift":
                {
                    var result = ChromaticFocalShift.Compute(system, glassMgr);
                    text = ChromaticFocalShiftTextExport.Export(result, "Chromatic Focal Shift");
                    break;
                }
                case "fftmtf-focus":
                {
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = FftMtfCalculator.ComputeThroughFocusPolychromatic(
                            system, glassMgr, f, 30, 0.1, 21, 64);
                        sb.Append(MtfThroughFocusTextExport.Export(result,
                            $"FFT MTF Through Focus \u2014 F{f + 1}"));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "geomtf":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = GeometricMtfKidger.Compute(system, glassMgr, f, wIdx);
                        sb.Append(FftMtfTextExport.Export(result,
                            $"Geometric MTF (Kidger) \u2014 F{f + 1} W{wIdx + 1}",
                            cutoffT: result.CutoffT, cutoffS: result.CutoffS,
                            fieldValue: system.Fields[f].Y,
                            wavelengthUm: system.Wavelengths[wIdx].Value,
                            fieldUnit: fieldUnit, isAfocal: system.IsAfocal));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "geomtf-field":
                {
                    double[] freqs = { 10, 20, 30, 40 };
                    var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(system, glassMgr,
                        freqs, system.PrimaryWavelengthIndex, numRings: 30, numFieldPoints: 20);
                    text = MtfVsFieldTextExport.Export(result, "Geometric MTF vs Field (Kidger)", fieldUnit, system.IsAfocal);
                    break;
                }
                case "geomtf-focus":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = GeometricMtfKidger.ComputeThroughFocus(system, glassMgr,
                            f, 30, wIdx, 0.1, 21);
                        sb.Append(MtfThroughFocusTextExport.Export(result,
                            $"Geometric MTF Through Focus \u2014 F{f + 1}"));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                case "wavefront":
                {
                    int wIdx = system.PrimaryWavelengthIndex;
                    var sb = new System.Text.StringBuilder();
                    for (int f = 0; f < system.Fields.Count; f++)
                    {
                        var result = WavefrontMapCalculator.Compute(system, glassMgr, f, wIdx);
                        sb.Append(WavefrontMapTextExport.Export(result,
                            $"Wavefront Map \u2014 F{f + 1} W{wIdx + 1}"));
                        sb.AppendLine();
                    }
                    text = sb.ToString();
                    break;
                }
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown analysis: {Markup.Escape(analysis)}[/]");
                    AnsiConsole.MarkupLine("Valid: spot, rayfan, opdfan, fftmtf, seidel, lateralcolor, fieldcurvature, distortion, fftmtf-field, fftpsf, relillum, chromaticshift, fftmtf-focus, geomtf-focus, wavefront");
                    return;
            }

            File.WriteAllText(outFile, text);
            AnsiConsole.MarkupLine($"[green]Text exported to: {Markup.Escape(outFile)}[/]");
        }
    }
}
