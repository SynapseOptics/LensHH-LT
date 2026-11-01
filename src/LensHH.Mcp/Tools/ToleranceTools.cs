using System;
using System.ComponentModel;
using System.IO;
using LensHH.Tolerancing;
using LensHH.Tolerancing.Drawings;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class ToleranceTools
    {
        private readonly McpSession _session;

        public ToleranceTools(McpSession session) => _session = session;

        private const string Format =
            "Tolerances are text, one per line: CODE surfaces ±t, or CODE surfaces min max, then optionally " +
            "uniform | normal[:k] | endpoint, and pivot:d on an element tilt (# starts a comment). Codes: TRAD radius (mm), " +
            "TFRN power (fringes), TIRR irregularity (fringes), TRSI symmetric irregularity (fringes), TTHI thickness (mm), " +
            "TIND index, TABB Abbe number, TCON conic, TSDX/TSDY surface decentre (mm), TSTX/TSTY surface tilt (deg), and " +
            "for an element given as first-last surfaces TEDX/TEDY decentre (mm), TETX/TETY tilt (deg). " +
            "Compensators go on lines of their own: COMP surface [±travel], the thickness after it, and CEDX/CEDY/CETX/CETY " +
            "first-last [±travel], an element decentred (mm) or tilted (deg) in its cell; without them each lens is refocused " +
            "by its back focus. The lens's pickups hold through every error and compensator. " +
            "Either pass the text, or a path to a .tol file.";

        private const string Options =
            " criterion: 'rms' (RMS wavefront, waves) or 'mtf:field:frequency:t|s'. compensate: refocus each lens, by the back " +
            "focus or the compensators the set names; travel limits the back focus (mm). polychromatic=false uses the primary wavelength. fields: the directions across the field each lens is judged in, " +
            "'xy' (+y, +x, -y, -x; default: the lens turned about its axis), 'y' (+y, -y) or 'file' (+y, the lens's fields as given).";

        private static string Read(string tolerances) =>
            !tolerances.Contains('\n') && File.Exists(tolerances) ? File.ReadAllText(tolerances) : tolerances;

        private static ToleranceSettings Settings(string criterion, bool compensate, double? travel, bool polychromatic,
                                                  string fields) => new()
        {
            Criterion = string.IsNullOrWhiteSpace(criterion) ? "rms" : criterion,
            Compensate = compensate,
            Travel = travel,
            Polychromatic = polychromatic,
            Fields = ToleranceRun.ParseFields(string.IsNullOrWhiteSpace(fields) ? "xy" : fields),
        };

        [McpServerTool, Description("A starting tolerance set for the current lens at a shop grade: 'lowcost', 'commercial' (default), " +
            "'precision' or 'extraprecise'. Returns the set as .tol text; with savePath also writes it there. " + Format)]
        public string ToleranceDefaults(string grade = "commercial", string? savePath = null)
        {
            try
            {
                var g = Enum.Parse<ShopGrade>(grade.Replace("-", ""), ignoreCase: true);
                string text = ToleranceRun.Defaults(_session.System, _session.GlassCatalog, g, new ToleranceSettings());
                if (!string.IsNullOrWhiteSpace(savePath)) File.WriteAllText(savePath, text);
                return text;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
            {
                return $"Error: {e.Message}";
            }
        }

        [McpServerTool, Description("Sensitivity of the current lens to each tolerance at both ends of its range, worst first, " +
            "with the root-sum-square estimate of the lens as built. " + Format + Options + " show limits the rows (0 = all).")]
        public string ToleranceSensitivity(string tolerances, string criterion = "rms", bool compensate = true, double? travel = null,
                                           bool polychromatic = true, string fields = "xy", int show = 0)
        {
            try
            {
                var settings = Settings(criterion, compensate, travel, polychromatic, fields);
                var table = ToleranceRun.Sensitivity(_session.System, _session.GlassCatalog, Read(tolerances), settings);
                return ToleranceRun.Report(table, show);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or NotSupportedException or IOException)
            {
                return $"Error: {e.Message}";
            }
        }

        [McpServerTool, Description("Element drawings of the current lens for an optical shop, from its tolerance set: one sheet per " +
            "piece of glass (cemented pieces each on their own) with its section, centre thickness and edge diameter with their " +
            "tolerances, the radii, clear apertures, power and irregularity in fringes (or a radius tolerance past the settings' " +
            "maximumPowerFringes), tilt, glass with its nd and vd tolerances, coatings, an asphere's sag table, mount tolerances, and " +
            "a title block. The set's CEDX/CEDY/CETX/CETY compensators are drawn as alignment at assembly. standard: 'iso' (ISO 10110, " +
            "A4) or 'asme' (ASME Y14.18 notes, MIL-PRF-13830B scratch-dig, ANSI A); empty keeps the settings' own. format: 'pdf' " +
            "(drawings.pdf with every sheet; default), 'svg', 'dxf' (one per element, named by drawing number), 'all', or several as " +
            "'pdf,dxf'. settingsPath: a drawing settings JSON file - the shop's classes, coatings, title block, and per-element " +
            "overrides under 'elements', keyed by first surface ('3') or drawing number ('OPT-02'), of any value on the drawing " +
            "(title, number, description, notes, glass, ndTolerance, vdTolerancePercent, thicknessMinus/Plus, diameter, " +
            "diameterMinus/Plus, and front/back: powerFringes, radiusTolerance, irregularityFringes, symmetricIrregularityFringes, " +
            "tiltArcmin, decentreMm, coating, clearAperture). Returns each drawing with its warnings and the files written. " + Format)]
        public string ToleranceDrawings(string tolerances, string outputDirectory, string? settingsPath = null, string format = "pdf",
                                        string standard = "", double testWavelength = PowerFringes.HeNe)
        {
            try
            {
                var settings = DrawingRun.ReadSettings(settingsPath);
                if (!string.IsNullOrWhiteSpace(standard)) settings = settings with { Standard = DrawingRun.ParseStandard(standard) };
                var result = DrawingRun.Write(_session.System, _session.GlassCatalog, Read(tolerances), settings, outputDirectory,
                                              DrawingRun.ParseFormat(format), testWavelength);
                return DrawingRun.Report(result);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or NotSupportedException
                                       or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                return $"Error: {e.Message}";
            }
        }

        [McpServerTool, Description("Monte Carlo of the current lens: trials random lenses drawn within the tolerances, each refocused, " +
            "judged by the criterion. Reports the spread (what 50/80/90/98 % of lenses reach) and the back focus travel used. " +
            "mtfMaxFrequency > 0 also keeps each lens's polychromatic MTF up to that frequency (cycles/mm, step mtfStep) and reports " +
            "the MTF the nominal lens, half the lenses and 90 % of them reach. saveDirectory saves the first saveTrials lenses as " +
            ".lhlt files (their decentres and tilts as Coordinate Breaks, irregularity as Zernike departures, glass errors as " +
            "index and Abbe offsets, refocused) that load and analyse like any lens, with trials.csv and an MTF plot per field (SVG). " +
            "statistics: 'uniform' (default), 'normal', 'normal:k' or 'endpoint', '+disc' or '+ring' to draw x and y pairs as one error. " +
            Format + Options)]
        public string ToleranceMonteCarlo(string tolerances, int trials = 200, int seed = 1, string statistics = "uniform",
                                          string criterion = "rms", bool compensate = true, double? travel = null,
                                          bool polychromatic = true, string fields = "xy",
                                          double mtfMaxFrequency = 0, double mtfStep = 5, string? saveDirectory = null, int saveTrials = 20)
        {
            try
            {
                var settings = Settings(criterion, compensate, travel, polychromatic, fields) with
                {
                    Trials = trials,
                    Seed = seed,
                    Statistics = statistics,
                    MtfMaxFrequency = mtfMaxFrequency,
                    MtfStep = mtfStep,
                    SaveDirectory = string.IsNullOrWhiteSpace(saveDirectory) ? null : saveDirectory,
                    SaveTrials = saveTrials,
                };
                var result = ToleranceRun.MonteCarlo(_session.System, _session.GlassCatalog, Read(tolerances), settings);
                return ToleranceRun.Report(result);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or NotSupportedException or IOException)
            {
                return $"Error: {e.Message}";
            }
        }
    }
}
