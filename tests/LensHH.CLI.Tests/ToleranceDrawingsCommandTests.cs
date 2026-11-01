using System;
using System.IO;
using LensHH.CLI.Commands;
using Xunit;

namespace LensHH.CLI.Tests
{
    // ─── tolerance drawings ────────────────────────────────────────

    public class ToleranceDrawingsCommandTests
    {
        [Fact]
        public void Drawings_WritesThePdfAndEachElementsFilesAndReportsThem()
        {
            string folder = Path.Combine(Path.GetTempPath(), "lenshh-cli-drawings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var cmd = new ToleranceCommand();
                var session = TestHelper.CreateSessionWithSystem();
                string tol = Path.Combine(folder, "lens.tol");
                TestHelper.CaptureOutput(cmd, session, new[] { "defaults", "precision", tol });
                Assert.True(File.Exists(tol));

                string settings = Path.Combine(folder, "drawing-settings.json");
                File.WriteAllText(settings, "{ \"date\": \"2026-10-31\", \"elements\": { \"1\": { \"number\": \"100-7\", \"glass\": \"N-BK7 or equivalent\" } } }");
                string output = TestHelper.CaptureOutput(cmd, session, new[]
                {
                    "drawings", tol, $"out={Path.Combine(folder, "out")}", $"settings={settings}", "format=all", "standard=asme",
                });

                Assert.Contains("1 element drawing (ASME Y14.18)", output);
                Assert.Contains("100-7", output);
                Assert.Contains("N-BK7 or equivalent", output);
                foreach (var file in new[] { "drawings.pdf", "100-7.svg", "100-7.dxf" })
                    Assert.True(File.Exists(Path.Combine(folder, "out", file)), file);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Drawings_WritesTheDefaultSettingsAndRefusesAMissingFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "lenshh-cli-drawings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var cmd = new ToleranceCommand();
                var session = TestHelper.CreateSessionWithSystem();
                string json = Path.Combine(folder, "settings.json");
                TestHelper.CaptureOutput(cmd, session, new[] { "drawings", "settings", json });
                Assert.Contains("\"maximumPowerFringes\"", File.ReadAllText(json));

                string tol = Path.Combine(folder, "lens.tol");
                File.WriteAllText(tol, "TFRN 1 3\n");
                string output = TestHelper.CaptureOutput(cmd, session, new[] { "drawings", tol });
                Assert.Contains("needs out=", output);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
