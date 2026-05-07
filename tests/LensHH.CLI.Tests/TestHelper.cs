using System;
using System.IO;
using LensHH.CLI;
using LensHH.CLI.Commands;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.CLI.Tests
{
    /// <summary>
    /// Helper for CLI tests. Captures Console.Out and provides
    /// a pre-configured Session for testing commands.
    /// </summary>
    public static class TestHelper
    {
        /// <summary>
        /// Locate the repository root by walking up from the test assembly
        /// until we find <c>LensHH-LT.sln</c>. Resolves on any machine and
        /// any clone path, replacing earlier hardcoded <c>C:\GIT\...</c>
        /// references that broke tests on every machine but the original
        /// developer's. Returns <c>null</c> if no marker is found
        /// (e.g. the test exe was copied somewhere outside the repo).
        /// </summary>
        public static string? RepoRoot => _repoRoot.Value;

        private static readonly Lazy<string?> _repoRoot = new(() =>
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LensHH-LT.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        });


        /// <summary>
        /// Execute a CLI command and capture its console output.
        /// Replaces Spectre.Console's static console with a plain-text writer.
        /// </summary>
        public static string CaptureOutput(ICommand command, Session session, string[] args)
        {
            var writer = new StringWriter();

            // Replace Spectre's global console with a plain-text one writing to our StringWriter
            var testConsole = Spectre.Console.AnsiConsole.Create(
                new Spectre.Console.AnsiConsoleSettings
                {
                    Out = new Spectre.Console.AnsiConsoleOutput(writer),
                    Interactive = Spectre.Console.InteractionSupport.No,
                    Ansi = Spectre.Console.AnsiSupport.No,
                    ColorSystem = Spectre.Console.ColorSystemSupport.NoColors
                });
            Spectre.Console.AnsiConsole.Console = testConsole;

            // Also redirect Console.Out for any direct Console.WriteLine calls
            var originalOut = Console.Out;
            Console.SetOut(writer);

            try
            {
                command.Execute(session, args);
            }
            catch (InvalidOperationException ex)
            {
                writer.WriteLine($"ERROR: {ex.Message}");
            }
            finally
            {
                Console.SetOut(originalOut);
                // Restore default Spectre console
                Spectre.Console.AnsiConsole.Console = Spectre.Console.AnsiConsole.Create(
                    new Spectre.Console.AnsiConsoleSettings());
            }

            return writer.ToString();
        }

        /// <summary>
        /// Create a session with a simple test system loaded (singlet lens).
        /// </summary>
        public static Session CreateSessionWithSystem()
        {
            var session = new Session();
            var system = new OpticalSystem
            {
                Title = "Test Singlet",
                Aperture = new Aperture(ApertureType.EPD, 20.0),
                FieldType = FieldType.ObjectAngle
            };

            system.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            system.Surfaces.Add(new Surface
            {
                Index = 1, Radius = 50.0, Thickness = 5.0, Material = "BK7",
                IsStop = true, SemiDiameter = 12, SemiDiameterMode = SemiDiameterMode.Fixed
            });
            system.Surfaces.Add(new Surface
            {
                Index = 2, Radius = -200.0, Thickness = 95.0,
                SemiDiameter = 12, SemiDiameterMode = SemiDiameterMode.Fixed
            });
            system.Surfaces.Add(new Surface { Index = 3 });

            system.Wavelengths.Add(new Wavelength(0.486, 1.0, false));
            system.Wavelengths.Add(new Wavelength(0.587, 1.0, true));
            system.Wavelengths.Add(new Wavelength(0.656, 1.0, false));

            system.Fields.Add(new Field(0, 1.0));
            system.Fields.Add(new Field(10, 1.0));

            system.GlassCatalogs.Add("SCHOTT");
            session.CurrentSystem = system;

            // Load glass catalogs from the repo's catalogs/ folder.
            var glassMgr = new LensHH.Core.Glass.GlassCatalogManager();
            if (RepoRoot != null)
            {
                var catalogsDir = Path.Combine(RepoRoot, "catalogs");
                if (Directory.Exists(catalogsDir))
                    glassMgr.LoadCatalogsFromFolder(catalogsDir);
            }
            session.GlassCatalog = glassMgr;

            return session;
        }

        /// <summary>Create an empty session (no system loaded).</summary>
        public static Session CreateEmptySession()
        {
            return new Session();
        }
    }
}
