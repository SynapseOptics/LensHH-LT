using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    /// <summary>
    /// MCP tools for the batch design-search workflow: agent supplies a host
    /// .lhlt plus a JSON list of candidates (each a list of stock-lens inserts);
    /// tool runs LM (or multistart) per candidate and returns ranked results.
    /// See docs/agent-stock-lens-workflow.md for the end-to-end recipe.
    /// </summary>
    [McpServerToolType]
    public class BatchDesignSearchTools
    {
        private readonly McpSession _session;
        public BatchDesignSearchTools(McpSession session) => _session = session;

        // ── Tool: start ───────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Start a batch design search. Clones the host .lhlt per candidate, applies the candidate's "
            + "stock-lens inserts, runs the inner optimizer on the resulting system, and records the merit. "
            + "Returns a jobId; poll batch_design_search_status to monitor and call batch_design_search_keep "
            + "to load the winner into the session. "
            + "\n\n"
            + "candidatesJson is a JSON array. Each element is an object: "
            + "{ label?, entrance pupil?, inserts: [{ partNumber, vendor?, reversed?, air thickness, after_surface? }] }. "
            + "Insert ops without after_surface go sequentially after the prior insert; with after_surface they "
            + "start a new group at HOST-numbered surface after_surface. The host's air gap at any insert's "
            + "after_surface (the 'leading gap') is automatically marked variable; so is every insert's trailing "
            + "'air thickness'. For build-from-scratch hosts with a floating stop, entrance pupil seeds S1.Thickness. "
            + "\n\n"
            + "innerOptimizer: 'lm' (default, damped least squares — fast) or 'multistart' (heavier, escapes local "
            + "minima). parallelism: number of candidates to optimize concurrently (default 1; bump only after the "
            + "engine thread-safety has been audited). "
            + "\n\n"
            + "hostLhltPath must point to a .lhlt that already has OBJ, aperture, fields, wavelengths, "
            + "merit-function operands, and the appropriate stop convention for the task case (a / b / c / d / e — "
            + "see docs/agent-stock-lens-workflow.md).")]
        public string BatchDesignSearchStart(string hostLhltPath, string candidatesJson,
            string innerOptimizer = "lm", int parallelism = 1)
        {
            List<CandidateDescriptor> candidates;
            try
            {
                candidates = ParseCandidates(candidatesJson);
            }
            catch (Exception ex)
            {
                return $"Failed to parse candidatesJson: {ex.Message}";
            }
            if (candidates.Count == 0) return "candidatesJson contained no candidates.";

            try
            {
                var job = _session.BatchDesignSearch.Start(
                    _session, hostLhltPath, candidates, innerOptimizer, parallelism);
                return $"Started batch_design_search. jobId={job.JobId}; "
                     + $"{candidates.Count} candidate(s), innerOptimizer={innerOptimizer}, "
                     + $"parallelism={parallelism}. Poll batch_design_search_status({job.JobId}).";
            }
            catch (Exception ex)
            {
                return $"Failed to start: {ex.Message}";
            }
        }

        // ── Tool: status ──────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Get the current status of a batch_design_search job. Returns state (Running/Completed/Cancelled/"
            + "Faulted), progress (done/total), and partial ranked results. Useful for monitoring + early "
            + "cancellation when an early winner is good enough.")]
        public string BatchDesignSearchStatus(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            var data = _session.BatchDesignSearch.GetData(jobId);
            if (data == null) return $"Job '{jobId}' has no batch data (not a batch_design_search job?).";

            var sb = new StringBuilder();
            sb.AppendLine($"jobId:   {job.JobId}");
            sb.AppendLine($"state:   {job.Status}");
            sb.AppendLine($"phase:   {job.Phase}");
            sb.AppendLine($"elapsed: {job.Elapsed.TotalSeconds:F1} s");
            sb.AppendLine($"progress: {data.Done}/{data.Candidates.Count}");
            sb.AppendLine();
            sb.AppendLine("ranked results (so far):");
            sb.AppendLine($"  {"idx",-4} {"label",-40} {"status",-10} {"merit",-15} {"iters",-7} {"EFL",-10}");
            // Rank by FinalMerit ascending, only "ok" first
            var ordered = new List<CandidateResult>(data.Results);
            ordered.Sort((a, b) =>
            {
                int sa = a.Status == "ok" ? 0 : 1, sb2 = b.Status == "ok" ? 0 : 1;
                if (sa != sb2) return sa - sb2;
                return a.FinalMerit.CompareTo(b.FinalMerit);
            });
            int shown = 0;
            foreach (var r in ordered)
            {
                if (r.Status == "pending") continue;
                string merit = double.IsNaN(r.FinalMerit) ? "—" : r.FinalMerit.ToString("E6", CultureInfo.InvariantCulture);
                string efl   = double.IsNaN(r.FinalEfl) ? "—" : r.FinalEfl.ToString("0.###", CultureInfo.InvariantCulture);
                string lab   = (r.Label ?? "").Length > 40 ? r.Label!.Substring(0, 37) + "..." : (r.Label ?? "");
                sb.AppendLine($"  {r.CandidateIndex,-4} {lab,-40} {r.Status,-10} {merit,-15} {r.Iterations,-7} {efl,-10}");
                if (r.Status == "error" && !string.IsNullOrEmpty(r.Error))
                    sb.AppendLine($"        error: {r.Error}");
                if (++shown >= 25) { sb.AppendLine($"  ... ({ordered.Count - shown} more not shown)"); break; }
            }
            if (job.Status == JobStatus.Completed && !string.IsNullOrEmpty(job.Result))
            {
                sb.AppendLine();
                sb.AppendLine(job.Result);
            }
            if (job.Status == JobStatus.Faulted && !string.IsNullOrEmpty(job.Error))
            {
                sb.AppendLine();
                sb.AppendLine($"Error: {job.Error}");
            }
            return sb.ToString();
        }

        // ── Tool: cancel ──────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Cancel a running batch_design_search job. Any candidate already finished keeps its result; "
            + "candidates not yet started are skipped. Use this when an early winner is good enough.")]
        public string BatchDesignSearchCancel(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            if (job.Status != JobStatus.Running)
                return $"Job {jobId} is already {job.Status}; nothing to cancel.";
            try { job.Cts.Cancel(); } catch { }
            return $"Cancellation requested for job {jobId}. Poll batch_design_search_status to confirm transition to Cancelled.";
        }

        // ── Tool: keep ────────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Load a specific candidate's optimized system into the current session. The candidate must have "
            + "status 'ok'. After this returns, the session.System holds the optimized design — call "
            + "save_as_system to persist, or run analyses/spot_diagram/etc. directly.")]
        public string BatchDesignSearchKeep(string jobId, int candidateIndex)
        {
            try
            {
                _session.BatchDesignSearch.ApplyToSession(_session, jobId, candidateIndex);
                var data = _session.BatchDesignSearch.GetData(jobId)!;
                var r = data.Results[candidateIndex];
                return $"Loaded candidate #{candidateIndex} ('{r.Label ?? "(unlabeled)"}') into the session. "
                     + $"finalMerit={r.FinalMerit:E6}, EFL={r.FinalEfl:0.###} mm. "
                     + $"System now holds {_session.System.Surfaces.Count} surfaces.";
            }
            catch (Exception ex)
            {
                return $"Failed to keep candidate {candidateIndex}: {ex.Message}";
            }
        }

        // ── Tool: discard ─────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Drop a finished or cancelled batch_design_search job's data from memory. The RunningJob entry "
            + "stays for history (optimize_jobs still lists it) but per-candidate result arrays and inputs "
            + "are released. Use when you're done inspecting a batch run.")]
        public string BatchDesignSearchDiscard(string jobId)
        {
            _session.BatchDesignSearch.Discard(jobId);
            return $"Discarded batch_design_search data for job {jobId}.";
        }

        // ── Internal: JSON parser ─────────────────────────────────────────────

        /// <summary>
        /// Parses the candidatesJson argument into typed CandidateDescriptors.
        /// Tolerates both 'air thickness' (with space) and 'air_thickness' /
        /// 'airThickness' (camelCase); same for 'entrance pupil'. The keys with
        /// spaces match the design notation the user/agent settled on.
        /// </summary>
        private static List<CandidateDescriptor> ParseCandidates(string json)
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("candidatesJson must be a JSON array.");
            var list = new List<CandidateDescriptor>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var c = new CandidateDescriptor();
                if (elem.TryGetProperty("label", out var lab) && lab.ValueKind == JsonValueKind.String)
                    c.Label = lab.GetString();
                if (TryGetDouble(elem, "entrance pupil", out double ep) ||
                    TryGetDouble(elem, "entrance_pupil", out ep) ||
                    TryGetDouble(elem, "entrancePupil",  out ep))
                    c.EntrancePupil = ep;
                if (elem.TryGetProperty("inserts", out var ins) && ins.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ie in ins.EnumerateArray())
                    {
                        var spec = new InsertSpec();
                        spec.PartNumber = ie.GetProperty("partNumber").GetString()
                            ?? throw new ArgumentException("each insert needs partNumber.");
                        if (ie.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.String)
                            spec.Vendor = v.GetString();
                        if (ie.TryGetProperty("reversed", out var rv))
                            spec.Reversed = rv.GetBoolean();
                        if (TryGetDouble(ie, "air thickness", out double at) ||
                            TryGetDouble(ie, "air_thickness", out at) ||
                            TryGetDouble(ie, "airThickness",  out at))
                            spec.AirThickness = at;
                        else
                            throw new ArgumentException($"insert {spec.PartNumber} missing 'air thickness'.");
                        if (TryGetInt(ie, "after_surface", out int afs) ||
                            TryGetInt(ie, "afterSurface",  out afs))
                            spec.AfterSurface = afs;
                        c.Inserts.Add(spec);
                    }
                }
                if (c.Inserts.Count == 0) throw new ArgumentException("each candidate needs at least one insert.");
                list.Add(c);
            }
            return list;
        }

        private static bool TryGetDouble(JsonElement obj, string name, out double value)
        {
            value = 0;
            if (obj.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number) { value = p.GetDouble(); return true; }
                if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            }
            return false;
        }
        private static bool TryGetInt(JsonElement obj, string name, out int value)
        {
            value = 0;
            if (obj.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number) { value = p.GetInt32(); return true; }
                if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value)) return true;
            }
            return false;
        }
    }
}
