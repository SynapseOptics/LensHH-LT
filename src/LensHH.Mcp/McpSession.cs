using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.Configuration;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;

namespace LensHH.Mcp
{
    /// <summary>
    /// Shared session state for the MCP server.
    /// Manages the currently loaded optical system and glass catalog.
    /// </summary>
    public class McpSession
    {
        private OpticalSystem? _system;
        private GlassCatalogManager? _glassCatalog;
        private string? _currentFilePath;

        public OpticalSystem System
        {
            get => _system ?? throw new InvalidOperationException(
                "No optical system loaded. Use the load_system tool first.");
            set => _system = value;
        }

        public bool HasSystem => _system != null;

        /// <summary>
        /// System accessor that additionally enforces every validation
        /// rule (glass resolution, wavelength range, on-axis field).
        /// Analysis / optimization tools call this so failures surface
        /// to the LLM as a tool error instead of producing a NaN result.
        /// </summary>
        public OpticalSystem ValidSystem
        {
            get
            {
                var s = System;
                LensHH.Core.Validation.SystemValidator.ValidateOrThrow(s, GlassCatalog);
                return s;
            }
        }

        /// <summary>
        /// Returns validation errors without throwing — used by the
        /// system_get_info tool and post-load reporting so the LLM can
        /// surface issues to the user up front.
        /// </summary>
        public IReadOnlyList<LensHH.Core.Validation.SystemValidator.ValidationError> Validate()
        {
            if (_system == null)
                return Array.Empty<LensHH.Core.Validation.SystemValidator.ValidationError>();
            return LensHH.Core.Validation.SystemValidator.Validate(_system, GlassCatalog);
        }

        // ── Pending-revert snapshot for optimize_try ────────────────
        // optimize_try captures the live system + merit + config-editor
        // here, runs the optimization in-place, and asks the LLM to
        // either commit (optimize_keep_result -> clear snapshot) or
        // discard (optimize_revert_result -> restore from snapshot).
        // Only one snapshot is held at a time; calling optimize_try
        // again overwrites the previous one (with a warning to the LLM
        // in the tool response).
        private string? _pendingOptimizeSnapshot;

        public bool HasPendingOptimizeResult => _pendingOptimizeSnapshot != null;

        private static readonly JsonSerializerOptions _snapshotJsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// Snapshot the current system + merit + config-editor and store
        /// it as the pending revert target. Called by optimize_try right
        /// before running the optimizer.
        /// </summary>
        public void CaptureOptimizeSnapshot()
        {
            if (_system == null) return;
            var file = LhltWriter.ToLhltFile(_system, MeritFunction, ConfigEditor);
            _pendingOptimizeSnapshot = JsonSerializer.Serialize(file, _snapshotJsonOptions);
        }

        /// <summary>
        /// Restore the system from the pending snapshot and clear it.
        /// Returns true if a snapshot existed; false if the LLM called
        /// revert without a prior optimize_try.
        /// </summary>
        public bool RevertOptimizeSnapshot()
        {
            if (_pendingOptimizeSnapshot == null) return false;
            var file = JsonSerializer.Deserialize<LhltFile>(_pendingOptimizeSnapshot, _snapshotJsonOptions);
            _pendingOptimizeSnapshot = null;
            if (file == null) return false;
            var result = LhltReader.FromLhltFile(file);
            _system = result.System;
            MeritFunction = result.MeritFunction;
            ConfigEditor = result.ConfigEditor;
            return true;
        }

        /// <summary>
        /// Discard the pending snapshot — i.e. commit the most recent
        /// optimize_try result. Returns true if there was a snapshot to
        /// drop; false if there wasn't.
        /// </summary>
        public bool ClearOptimizeSnapshot()
        {
            bool had = _pendingOptimizeSnapshot != null;
            _pendingOptimizeSnapshot = null;
            return had;
        }

        // ── Long-running optimization jobs ─────────────────────────────
        // The synchronous optimize_* tools can take many minutes to
        // complete, during which the LLM (and user) can't see progress.
        // The job-pattern variants (optimize_*_start) push the work onto
        // a background Task, store a snapshot under a job-id, and let
        // optimize_status / optimize_cancel poll. The engine still
        // mutates _session.System in place, so the result is visible to
        // every other tool call as soon as the job completes.

        private readonly ConcurrentDictionary<string, RunningJob> _jobs = new();

        public string AddJob(RunningJob job)
        {
            _jobs[job.JobId] = job;
            return job.JobId;
        }

        public RunningJob? GetJob(string jobId)
            => _jobs.TryGetValue(jobId, out var j) ? j : null;

        public IReadOnlyCollection<RunningJob> Jobs => _jobs.Values.ToArray();

        public GlassCatalogManager GlassCatalog
        {
            get
            {
                if (_glassCatalog == null)
                {
                    _glassCatalog = new GlassCatalogManager();
                    // Search for glass catalogs in standard locations
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var searchPaths = new[]
                    {
                        Path.Combine(baseDir, "catalogs", "Glass"),
                        Path.Combine(baseDir, "..", "catalogs", "Glass"),
                        Path.Combine(baseDir, "catalogs"),
                        // Dev: bin/Debug/net8.0 → src/LensHH.Mcp → src → LensHH-LT → catalogs/Glass
                        Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "Glass"),
                        Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs"),
                        Path.Combine(Directory.GetCurrentDirectory(), "catalogs", "Glass"),
                        Path.Combine(Directory.GetCurrentDirectory(), "catalogs"),
                    };

                    foreach (var path in searchPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            _glassCatalog.LoadCatalogsFromFolder(path);
                            break;
                        }
                    }
                }
                return _glassCatalog;
            }
        }

        public MeritFunction? MeritFunction { get; set; }
        public ConfigurationEditor? ConfigEditor { get; set; }

        public string? CurrentFilePath
        {
            get => _currentFilePath;
            set => _currentFilePath = value;
        }

        public void LoadFromFile(string filePath)
        {
            var result = LhltReader.Read(filePath);
            _system = result.System;
            MeritFunction = result.MeritFunction;
            ConfigEditor = result.ConfigEditor;
            _currentFilePath = filePath;
            ClearLastRender();
        }

        public void SaveToFile(string filePath)
        {
            if (_system == null)
                throw new InvalidOperationException("No optical system loaded.");
            LhltWriter.Write(_system, filePath, MeritFunction, ConfigEditor);
            _currentFilePath = filePath;
        }

        public void ImportZemax(string filePath)
        {
            _system = ZmxReader.Read(filePath);
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }

        public void ImportCodeV(string filePath)
        {
            _system = CodeVReader.Read(filePath);
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }

        public void ImportOslo(string filePath)
        {
            _system = OsloReader.Read(filePath);
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }

        public void ImportOptalix(string filePath)
        {
            _system = OptalixReader.Read(filePath, GlassCatalog);
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }

        public void ImportOptiland(string filePath)
        {
            _system = OptilandReader.Read(filePath);
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }

        public void ExportZemax(string filePath)
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
            ZmxWriter.Write(_system, filePath);
        }

        public void ExportCodeV(string filePath)
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
            CodeVWriter.Write(_system, filePath);
        }

        public void ExportOslo(string filePath)
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
            OsloWriter.Write(_system, filePath);
        }

        public void ExportOptalix(string filePath)
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
            OptalixWriter.Write(_system, filePath);
        }

        public void ExportOptiland(string filePath)
        {
            if (_system == null) throw new InvalidOperationException("No optical system loaded.");
            OptilandWriter.Write(_system, filePath);
        }

        /// <summary>Last rendered analysis name (e.g. "FftMtf", "SpotDiagram").</summary>
        public string? LastRenderedAnalysis { get; set; }

        /// <summary>Parameters used for the last render (field index, wavelength, etc.).</summary>
        public Dictionary<string, object>? LastRenderedParams { get; set; }

        public void ClearLastRender()
        {
            LastRenderedAnalysis = null;
            LastRenderedParams = null;
        }

        /// <summary>
        /// Run every system validation rule (glass resolution, wavelength
        /// range, on-axis field). Returns a single newline-joined error
        /// message if any rule fails, null otherwise. Kept under the
        /// historical name (ValidateGlass) so the ~30 tool call sites
        /// using it continue to work; the contract is the same — return
        /// non-null and the tool short-circuits with that string as the
        /// LLM-visible error.
        /// </summary>
        public string? ValidateGlass()
        {
            if (_system == null) return null;
            var errors = LensHH.Core.Validation.SystemValidator.Validate(_system, GlassCatalog);
            if (errors.Count == 0) return null;
            return errors.Count == 1
                ? errors[0].Message
                : string.Join("\n", errors.ConvertAll(e => "- " + e.Message));
        }

        public void NewSystem()
        {
            _system = new OpticalSystem();
            MeritFunction = null;
            ConfigEditor = null;
            _currentFilePath = null;
            ClearLastRender();
        }
    }
}
