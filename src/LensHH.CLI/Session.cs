using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LensHH.Core.Configuration;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;

namespace LensHH.CLI
{
    public class Session
    {
        public OpticalSystem? CurrentSystem { get; set; }
        public string? CurrentFilePath { get; set; }
        public MeritFunction? CurrentMeritFunction { get; set; }
        public ConfigurationEditor? ConfigEditor { get; set; }
        public GlassCatalogManager? GlassCatalog { get; set; }

        // Logging
        private StreamWriter? _logWriter;
        private TeeTextWriter? _teeWriter;
        private TextWriter? _originalOut;

        public bool IsLogging => _logWriter != null;
        public string? LogFilePath { get; private set; }

        public void StartLogging(string path)
        {
            StopLogging();

            _logWriter = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
            _logWriter.WriteLine($"# LensHH-LT Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logWriter.WriteLine();

            _originalOut = Console.Out;
            _teeWriter = new TeeTextWriter(_originalOut, _logWriter);
            Console.SetOut(_teeWriter);

            LogFilePath = path;
        }

        public void StopLogging()
        {
            if (_teeWriter != null && _originalOut != null)
            {
                Console.SetOut(_originalOut);
                _teeWriter = null;
                _originalOut = null;
            }

            if (_logWriter != null)
            {
                _logWriter.WriteLine();
                _logWriter.WriteLine($"# Log ended - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _logWriter.Dispose();
                _logWriter = null;
            }

            LogFilePath = null;
        }

        public void LogInput(string input)
        {
            _logWriter?.WriteLine($"> {input}");
        }

        public OpticalSystem EnsureSystem()
        {
            if (CurrentSystem == null)
                throw new InvalidOperationException("No optical system loaded. Use 'file open <path>' or 'file new' first.");
            return CurrentSystem;
        }

        /// <summary>
        /// Like EnsureSystem, but additionally enforces every system
        /// validation rule (glass resolution, wavelength range, on-axis
        /// field). Use from analysis / optimization commands so the CLI
        /// fails fast with the same gate the GUI shows on its banner.
        /// </summary>
        public OpticalSystem EnsureValidSystem()
        {
            var system = EnsureSystem();
            if (GlassCatalog != null)
                LensHH.Core.Validation.SystemValidator.ValidateOrThrow(system, GlassCatalog);
            return system;
        }

        /// <summary>
        /// Returns validation errors for the current system without
        /// throwing. Used by file-open/new to print warnings before the
        /// user runs an analysis.
        /// </summary>
        public IReadOnlyList<LensHH.Core.Validation.SystemValidator.ValidationError> Validate()
        {
            if (CurrentSystem == null || GlassCatalog == null)
                return Array.Empty<LensHH.Core.Validation.SystemValidator.ValidationError>();
            return LensHH.Core.Validation.SystemValidator.Validate(CurrentSystem, GlassCatalog);
        }

        // Snapshot helpers — full system + merit + config-editor JSON
        // round-trip. Used by 'optimize try' to capture the design before
        // optimization so the user can revert if the result isn't to
        // their liking. Mirrors GuiSession.SnapshotSystem so the GUI and
        // CLI agree on the snapshot format.
        private static readonly JsonSerializerOptions _snapshotJsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
        };

        public string SnapshotSystem()
        {
            var sys = EnsureSystem();
            var file = LhltWriter.ToLhltFile(sys, CurrentMeritFunction, ConfigEditor);
            return JsonSerializer.Serialize(file, _snapshotJsonOptions);
        }

        public void RestoreSystemSnapshot(string json)
        {
            var file = JsonSerializer.Deserialize<LhltFile>(json, _snapshotJsonOptions);
            if (file == null) return;
            var result = LhltReader.FromLhltFile(file);
            CurrentSystem = result.System;
            CurrentMeritFunction = result.MeritFunction;
            ConfigEditor = result.ConfigEditor;
        }

        public MeritFunction EnsureMeritFunction()
        {
            if (CurrentMeritFunction == null)
            {
                CurrentMeritFunction = new MeritFunction();
            }
            return CurrentMeritFunction;
        }

        public ConfigurationEditor EnsureConfigEditor()
        {
            if (ConfigEditor == null)
            {
                ConfigEditor = new ConfigurationEditor();
            }
            return ConfigEditor;
        }

        public void ValidateGlass()
        {
            var sys = CurrentSystem;
            var mgr = GlassCatalog;
            if (sys == null || mgr == null) return;

            var missing = new List<string>();
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var mat = sys.Surfaces[i].Material;
                if (string.IsNullOrEmpty(mat) || mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (mgr.GetGlass(mat, sys.GlassCatalogs.Count > 0 ? sys.GlassCatalogs : null) == null)
                    missing.Add($"Surface {i}: {mat}");
            }
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    "Index of refraction is not defined for: " + string.Join(", ", missing));
        }

        public GlassCatalogManager EnsureGlassCatalog()
        {
            if (GlassCatalog == null)
            {
                GlassCatalog = new GlassCatalogManager();

                // Try to load catalogs from standard locations
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                var searchPaths = new List<string>
                {
                    Path.Combine(exeDir, "catalogs"),
                    Path.Combine(Directory.GetCurrentDirectory(), "catalogs"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs"),
                };

                // Walk up from exe dir to find catalogs (handles bin/Debug/net8.0 layout)
                var dir = exeDir;
                for (int i = 0; i < 5 && dir != null; i++)
                {
                    dir = Path.GetDirectoryName(dir);
                    if (dir != null)
                        searchPaths.Add(Path.Combine(dir, "catalogs"));
                }

                foreach (var path in searchPaths)
                {
                    // Check direct path and Glass/ subdirectory. Don't pre-filter
                    // with a "*.agf" glob: Linux filesystems are case-sensitive, so
                    // it silently misses shipped-uppercase "SCHOTT.AGF" / "HOYA.AGF".
                    // LoadCatalogsFromFolder already filters by extension
                    // case-insensitively and no-ops on a missing/empty folder, so
                    // just hand it the directory and check whether anything loaded.
                    var glassPaths = new[] { path, Path.Combine(path, "Glass") };
                    foreach (var gp in glassPaths)
                    {
                        if (Directory.Exists(gp))
                        {
                            GlassCatalog.LoadCatalogsFromFolder(gp);
                            if (GlassCatalog.LoadedCatalogs.Count > 0) break;
                        }
                    }
                    if (GlassCatalog.LoadedCatalogs.Count > 0) break;
                }
            }
            return GlassCatalog;
        }
    }

    /// <summary>
    /// TextWriter that writes to two underlying writers simultaneously.
    /// Used to tee console output to both the terminal and a log file.
    /// </summary>
    internal class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _primary;
        private readonly TextWriter _secondary;

        public TeeTextWriter(TextWriter primary, TextWriter secondary)
        {
            _primary = primary;
            _secondary = secondary;
        }

        public override Encoding Encoding => _primary.Encoding;

        public override void Write(char value)
        {
            _primary.Write(value);
            _secondary.Write(value);
        }

        public override void Write(string? value)
        {
            _primary.Write(value);
            _secondary.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _primary.WriteLine(value);
            _secondary.WriteLine(value);
        }

        public override void WriteLine()
        {
            _primary.WriteLine();
            _secondary.WriteLine();
        }

        public override void Flush()
        {
            _primary.Flush();
            _secondary.Flush();
        }
    }
}
