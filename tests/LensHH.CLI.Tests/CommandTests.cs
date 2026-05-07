using System;
using System.IO;
using LensHH.CLI.Commands;
using Xunit;

// Serialize all CLI tests — Spectre.Console is static and not thread-safe
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LensHH.CLI.Tests
{
    // ─── Help / metadata tests (no output capture) ─────────────────

    public class HelpTests
    {
        [Theory]
        [InlineData(typeof(FileCommand))]
        [InlineData(typeof(SurfaceCommand))]
        [InlineData(typeof(AnalysisCommand))]
        [InlineData(typeof(MeritCommand))]
        [InlineData(typeof(GlassCommand))]
        public void AllCommands_HaveNonEmptyHelp(Type commandType)
        {
            var command = (ICommand)Activator.CreateInstance(commandType)!;
            Assert.NotEmpty(command.Help);
            Assert.NotEmpty(command.Name);
            Assert.NotEmpty(command.Description);
        }
    }

    // ─── File command tests ────────────────────────────────────────

    public class FileCommandTests
    {
        [Fact]
        public void FileNew_CreatesSystem()
        {
            var cmd = new FileCommand();
            var session = TestHelper.CreateEmptySession();

            TestHelper.CaptureOutput(cmd, session, new[] { "new" });

            Assert.NotNull(session.CurrentSystem);
            Assert.Equal(3, session.CurrentSystem.Surfaces.Count);
        }

        [Fact]
        public void FileSaveAs_And_Open_RoundTrip()
        {
            string tmpPath = Path.GetTempFileName() + ".lhlt";
            try
            {
                var cmd = new FileCommand();

                // Save
                var session1 = TestHelper.CreateSessionWithSystem();
                TestHelper.CaptureOutput(cmd, session1, new[] { "save", tmpPath });
                Assert.True(File.Exists(tmpPath));

                // Open
                var session2 = TestHelper.CreateEmptySession();
                TestHelper.CaptureOutput(cmd, session2, new[] { "open", tmpPath });
                Assert.NotNull(session2.CurrentSystem);
                Assert.Equal("Test Singlet", session2.CurrentSystem.Title);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void FileImport_MissingArgs_DoesNotCrash()
        {
            var cmd = new FileCommand();
            var session = TestHelper.CreateEmptySession();
            // Should not throw
            TestHelper.CaptureOutput(cmd, session, new[] { "import" });
        }

        [Fact]
        public void FileExport_MissingArgs_DoesNotCrash()
        {
            var cmd = new FileCommand();
            var session = TestHelper.CreateSessionWithSystem();
            TestHelper.CaptureOutput(cmd, session, new[] { "export" });
        }
    }

    // ─── System command tests ──────────────────────────────────────

    public class SystemCommandTests
    {
        [Fact]
        public void System_Info_DoesNotCrash()
        {
            var cmd = new SystemCommand();
            var session = TestHelper.CreateSessionWithSystem();
            // Should not throw
            TestHelper.CaptureOutput(cmd, session, new[] { "info" });
        }

        [Fact]
        public void System_Info_NoSystem_DoesNotCrash()
        {
            var cmd = new SystemCommand();
            var session = TestHelper.CreateEmptySession();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "info" });
            Assert.Contains("No optical system", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─── Surface command tests ─────────────────────────────────────

    public class SurfaceCommandTests
    {
        [Fact]
        public void Surface_Add_IncreasesCount()
        {
            var cmd = new SurfaceCommand();
            var session = TestHelper.CreateSessionWithSystem();
            int before = session.CurrentSystem!.Surfaces.Count;

            TestHelper.CaptureOutput(cmd, session, new[] { "add" });

            Assert.Equal(before + 1, session.CurrentSystem.Surfaces.Count);
        }

        [Fact]
        public void Surface_Remove_DecreasesCount()
        {
            var cmd = new SurfaceCommand();
            var session = TestHelper.CreateSessionWithSystem();
            int before = session.CurrentSystem!.Surfaces.Count;

            TestHelper.CaptureOutput(cmd, session, new[] { "remove", "2" });

            Assert.Equal(before - 1, session.CurrentSystem.Surfaces.Count);
        }

        [Fact]
        public void Surface_NoSystem_DoesNotCrash()
        {
            var cmd = new SurfaceCommand();
            var session = TestHelper.CreateEmptySession();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "list" });
            Assert.Contains("No optical system", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─── Analysis command tests ────────────────────────────────────

    public class AnalysisCommandTests
    {
        [Fact]
        public void Analysis_Paraxial_DoesNotCrash()
        {
            var cmd = new AnalysisCommand();
            var session = TestHelper.CreateSessionWithSystem();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "paraxial" });
            // Output is a Spectre table — just verify non-empty
            Assert.NotEmpty(output);
        }

        [Fact]
        public void Analysis_Spot_DoesNotCrash()
        {
            var cmd = new AnalysisCommand();
            var session = TestHelper.CreateSessionWithSystem();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "spot" });
            Assert.Contains("RMS", output);
        }

        [Fact]
        public void Analysis_Seidel_DoesNotCrash()
        {
            var cmd = new AnalysisCommand();
            var session = TestHelper.CreateSessionWithSystem();
            TestHelper.CaptureOutput(cmd, session, new[] { "seidel" });
        }

        [Fact]
        public void Analysis_NoSystem_DoesNotCrash()
        {
            var cmd = new AnalysisCommand();
            var session = TestHelper.CreateEmptySession();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "spot" });
            Assert.Contains("No optical system", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─── Merit command tests ───────────────────────────────────────

    public class MeritCommandTests
    {
        [Fact]
        public void Merit_Add_CreatesOperand()
        {
            var cmd = new MeritCommand();
            var session = TestHelper.CreateSessionWithSystem();

            TestHelper.CaptureOutput(cmd, session, new[] { "add", "EFL", "100", "1" });

            Assert.NotNull(session.CurrentMeritFunction);
            Assert.True(session.CurrentMeritFunction.Operands.Count >= 1);
        }

        [Fact]
        public void Merit_Clear_EmptiesOperands()
        {
            var cmd = new MeritCommand();
            var session = TestHelper.CreateSessionWithSystem();

            TestHelper.CaptureOutput(cmd, session, new[] { "add", "EFL", "100", "1" });
            int countBefore = session.CurrentMeritFunction!.Operands.Count;
            Assert.True(countBefore > 0);

            TestHelper.CaptureOutput(cmd, session, new[] { "clear" });

            // Clear may null or empty the operands — either is acceptable
            Assert.True(session.CurrentMeritFunction == null ||
                         session.CurrentMeritFunction.Operands.Count == 0);
        }

        [Fact]
        public void Merit_Types_DoesNotCrash()
        {
            var cmd = new MeritCommand();
            var session = TestHelper.CreateSessionWithSystem();
            TestHelper.CaptureOutput(cmd, session, new[] { "types" });
        }
    }

    // ─── Glass command tests ───────────────────────────────────────

    public class GlassCommandTests
    {
        [Fact]
        public void Glass_Catalogs_DoesNotCrash()
        {
            var cmd = new GlassCommand();
            var session = TestHelper.CreateSessionWithSystem();
            TestHelper.CaptureOutput(cmd, session, new[] { "catalogs" });
        }

        [Fact]
        public void Glass_Search_FindsBK7()
        {
            var cmd = new GlassCommand();
            var session = TestHelper.CreateSessionWithSystem();
            string output = TestHelper.CaptureOutput(cmd, session, new[] { "search", "BK7" });
            Assert.Contains("BK7", output);
        }
    }

    // ─── Logging tests ─────────────────────────────────────────────

    public class LoggingTests
    {
        [Fact]
        public void Log_StartAndStop()
        {
            var session = TestHelper.CreateSessionWithSystem();
            string tmpLog = Path.GetTempFileName();

            try
            {
                session.StartLogging(tmpLog);
                Assert.True(session.IsLogging);

                Console.WriteLine("Test log entry");

                session.StopLogging();
                Assert.False(session.IsLogging);

                string content = File.ReadAllText(tmpLog);
                Assert.Contains("Test log entry", content);
                Assert.Contains("LensHH-LT Log", content);
            }
            finally
            {
                session.StopLogging();
                if (File.Exists(tmpLog)) File.Delete(tmpLog);
            }
        }
    }
}
