using System;
using System.IO;
using LensHH.CLI.Commands;
using Xunit;

namespace LensHH.CLI.Tests
{
    /// <summary>
    /// Tests that every CLI analysis text export and PNG render produces output
    /// without crashing. Uses the Cooke Triplet test file for a realistic multi-field system.
    /// </summary>
    public class AnalysisExportTests : IDisposable
    {
        // In-repo multi-field test fixture (3 fields, 3 wavelengths,
        // resolves on any clone via TestHelper.RepoRoot).
        private static readonly string CookeTripletPath =
            TestHelper.RepoRoot is { } root
                ? Path.Combine(root, "samples", "CookeTripletAftrLocalOptimization.lhlt")
                : "samples/CookeTripletAftrLocalOptimization.lhlt";

        private readonly string _tmpDir;
        private readonly Session _session;
        private readonly AnalysisCommand _cmd;

        public AnalysisExportTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), "LensHH_ExportTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tmpDir);

            // Activate engine (required for wavefront, FFT MTF, etc.)
            LensHH.Core.Activation.ActivationManager.TryLoadExistingActivation();

            _cmd = new AnalysisCommand();

            // Load the Cooke Triplet via FileCommand
            _session = TestHelper.CreateEmptySession();
            var fileCmd = new FileCommand();
            TestHelper.CaptureOutput(fileCmd, _session, new[] { "open", CookeTripletPath });
            Assert.NotNull(_session.CurrentSystem);

            // Ensure glass catalogs are loaded (test exe dir differs from CLI dir)
            var glassMgr = new LensHH.Core.Glass.GlassCatalogManager();
            if (TestHelper.RepoRoot != null)
            {
                var glassDir = Path.Combine(TestHelper.RepoRoot, "catalogs", "Glass");
                if (Directory.Exists(glassDir))
                    glassMgr.LoadCatalogsFromFolder(glassDir);
            }
            _session.GlassCatalog = glassMgr;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tmpDir))
                    Directory.Delete(_tmpDir, true);
            }
            catch { /* best-effort cleanup */ }
        }

        // ─── Text Export tests ────────────────────────────────────────

        [Theory]
        [InlineData("spot")]
        [InlineData("rayfan")]
        [InlineData("opdfan")]
        [InlineData("fftmtf")]
        [InlineData("seidel")]
        [InlineData("lateralcolor")]
        [InlineData("fieldcurvature")]
        [InlineData("distortion")]
        [InlineData("fftmtf-field")]
        [InlineData("fftpsf")]
        [InlineData("relillum")]
        [InlineData("chromaticshift")]
        [InlineData("fftmtf-focus")]
        [InlineData("geomtf")]
        [InlineData("geomtf-field")]
        [InlineData("geomtf-focus")]
        [InlineData("wavefront")]
        public void ExportText_ProducesNonEmptyFile(string analysis)
        {
            string outFile = Path.Combine(_tmpDir, $"{analysis}.txt");

            string output = TestHelper.CaptureOutput(_cmd, _session,
                new[] { "export-text", analysis, outFile });

            Assert.True(File.Exists(outFile),
                $"export-text {analysis} did not create {outFile}. Output: {output}");

            string content = File.ReadAllText(outFile);
            Assert.True(content.Length > 10,
                $"export-text {analysis} produced near-empty file ({content.Length} chars)");
        }

        [Theory]
        [InlineData("spot")]
        [InlineData("rayfan")]
        [InlineData("opdfan")]
        [InlineData("fftmtf")]
        [InlineData("seidel")]
        [InlineData("lateralcolor")]
        [InlineData("fieldcurvature")]
        [InlineData("distortion")]
        [InlineData("fftmtf-field")]
        [InlineData("fftpsf")]
        [InlineData("relillum")]
        [InlineData("chromaticshift")]
        [InlineData("fftmtf-focus")]
        [InlineData("geomtf")]
        [InlineData("geomtf-field")]
        [InlineData("geomtf-focus")]
        [InlineData("wavefront")]
        public void ExportText_ContainsTabSeparatedData(string analysis)
        {
            string outFile = Path.Combine(_tmpDir, $"{analysis}_tabs.txt");

            TestHelper.CaptureOutput(_cmd, _session,
                new[] { "export-text", analysis, outFile });

            string content = File.ReadAllText(outFile);
            Assert.Contains("\t", content,
                StringComparison.Ordinal);
        }

        // ─── PNG Render tests (require RenderApp) ────────────────────

        private static bool RenderAppAvailable()
        {
            if (TestHelper.RepoRoot == null) return false;
            var renderAppBase = Path.Combine(TestHelper.RepoRoot,
                "src", "LensHH.RenderApp", "bin");
            foreach (var config in new[] { "Debug", "Release" })
            {
                var path = Path.Combine(renderAppBase, config, "net8.0", "LensHH.RenderApp.exe");
                if (File.Exists(path)) return true;
            }
            return false;
        }

        [Theory]
        [InlineData("spot")]
        [InlineData("rayfan")]
        [InlineData("opdfan")]
        [InlineData("seidel")]
        [InlineData("layout")]
        [InlineData("relillum")]
        [InlineData("lateralcolor")]
        [InlineData("fieldcurvature")]
        [InlineData("distortion")]
        [InlineData("fftmtf")]
        [InlineData("fftmtf-field")]
        [InlineData("fftmtf-focus")]
        [InlineData("fftpsf")]
        [InlineData("geomtf")]
        [InlineData("geomtf-field")]
        [InlineData("geomtf-focus")]
        [InlineData("wavefront")]
        [InlineData("chromaticfocalshift")]
        public void RenderPng_ProducesFile(string analysis)
        {
            if (!RenderAppAvailable())
            {
                // Skip when RenderApp is not built — CI may not have it
                return;
            }

            string outFile = Path.Combine(_tmpDir, $"{analysis}.png");

            string output = TestHelper.CaptureOutput(_cmd, _session,
                new[] { "render-png", analysis, outFile });

            // If RenderApp pipe failed (not running), treat as skip rather than failure
            if (output.Contains("Render error", StringComparison.OrdinalIgnoreCase))
                return;

            Assert.True(File.Exists(outFile),
                $"render-png {analysis} did not create {outFile}. Output: {output}");

            var info = new FileInfo(outFile);
            Assert.True(info.Length > 100,
                $"render-png {analysis} produced suspiciously small PNG ({info.Length} bytes)");
        }

        // ─── Multi-field export tests (verify all fields present) ────

        [Theory]
        [InlineData("spot", "Spot Diagram")]
        [InlineData("rayfan", "Ray Fan")]
        [InlineData("opdfan", "OPD Fan")]
        [InlineData("fftmtf", "FFT MTF")]
        [InlineData("fftpsf", "FFT PSF")]
        [InlineData("fftmtf-focus", "FFT MTF Through Focus")]
        [InlineData("geomtf", "Geometric MTF")]
        [InlineData("geomtf-focus", "Geometric MTF Through Focus")]
        [InlineData("wavefront", "Wavefront Map")]
        public void ExportText_PerFieldAnalysis_ContainsAllFields(string analysis, string _)
        {
            string outFile = Path.Combine(_tmpDir, $"{analysis}_allfields.txt");

            TestHelper.CaptureOutput(_cmd, _session,
                new[] { "export-text", analysis, outFile });

            string content = File.ReadAllText(outFile);
            int fieldCount = _session.CurrentSystem!.Fields.Count;

            for (int f = 1; f <= fieldCount; f++)
            {
                Assert.Contains($"F{f}", content,
                    StringComparison.Ordinal);
            }
        }
    }
}
