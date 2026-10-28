using System;
using System.ComponentModel;
using System.IO;
using LensHH.Tolerancing;
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
            "Either pass the text, or a path to a .tol file.";

        private const string Options =
            " criterion: 'rms' (RMS wavefront, waves) or 'mtf:field:frequency:t|s'. compensate: refocus each lens by the back " +
            "focus; travel limits it (mm). polychromatic=false uses the primary wavelength. mirrorFields mirrors each field to -y.";

        private static string Read(string tolerances) =>
            !tolerances.Contains('\n') && File.Exists(tolerances) ? File.ReadAllText(tolerances) : tolerances;

        private static ToleranceSettings Settings(string criterion, bool compensate, double? travel, bool polychromatic,
                                                  bool mirrorFields) => new()
        {
            Criterion = string.IsNullOrWhiteSpace(criterion) ? "rms" : criterion,
            Compensate = compensate,
            Travel = travel,
            Polychromatic = polychromatic,
            Fields = mirrorFields ? FieldSymmetry.Y : FieldSymmetry.File,
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
                                           bool polychromatic = true, bool mirrorFields = true, int show = 0)
        {
            try
            {
                var settings = Settings(criterion, compensate, travel, polychromatic, mirrorFields);
                var table = ToleranceRun.Sensitivity(_session.System, _session.GlassCatalog, Read(tolerances), settings);
                return ToleranceRun.Report(table, show);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or NotSupportedException or IOException)
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
                                          bool polychromatic = true, bool mirrorFields = true,
                                          double mtfMaxFrequency = 0, double mtfStep = 5, string? saveDirectory = null, int saveTrials = 20)
        {
            try
            {
                var settings = Settings(criterion, compensate, travel, polychromatic, mirrorFields) with
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
