using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class PickupTools
    {
        private readonly McpSession _session;
        public PickupTools(McpSession session) => _session = session;

        [McpServerTool, Description("List all pickup solves in the system. A pickup ties a target surface parameter to a source surface: target = source * scale + offset.")]
        public string ListPickups()
        {
            var sys = _session.System;
            if (sys.Pickups.Count == 0)
                return "No pickup solves defined.";

            var sb = new StringBuilder();
            sb.AppendLine($"{"#",3} {"Parameter",-12} {"Target",7} {"Source",7} {"Scale",10} {"Offset",10}");

            for (int i = 0; i < sys.Pickups.Count; i++)
            {
                var p = sys.Pickups[i];
                sb.AppendLine($"{i,3} {p.Parameter,-12} {p.TargetSurfaceIndex,7} {p.SourceSurfaceIndex,7} {p.ScaleFactor,10:G4} {p.Offset,10:G4}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Add a pickup solve. parameter: 'radius', 'thickness', 'glass', 'semi_diameter', or 'conic'. The target surface value = source surface value * scale + offset. scale defaults to 1.0, offset defaults to 0.0.")]
        public string AddPickup(int targetSurface, string parameter, int sourceSurface, double scale = 1.0, double offset = 0.0)
        {
            var sys = _session.System;
            if (targetSurface < 0 || targetSurface >= sys.Surfaces.Count)
                return $"Target surface index {targetSurface} out of range.";
            if (sourceSurface < 0 || sourceSurface >= sys.Surfaces.Count)
                return $"Source surface index {sourceSurface} out of range.";
            if (targetSurface == sourceSurface)
                return "Target and source surface cannot be the same.";

            if (!Enum.TryParse<PickupParameter>(parameter, true, out var param))
            {
                // Try common aliases
                param = parameter.ToLowerInvariant() switch
                {
                    "radius" => PickupParameter.Radius,
                    "thickness" => PickupParameter.Thickness,
                    "glass" => PickupParameter.Glass,
                    "semi_diameter" => PickupParameter.SemiDiameter,
                    "conic" => PickupParameter.Conic,
                    _ => (PickupParameter)(-1)
                };
                if ((int)param == -1)
                    return $"Unknown parameter '{parameter}'. Use: radius, thickness, glass, semi_diameter, conic.";
            }

            var pickup = new Pickup
            {
                TargetSurfaceIndex = targetSurface,
                SourceSurfaceIndex = sourceSurface,
                Parameter = param,
                ScaleFactor = scale,
                Offset = offset
            };

            sys.Pickups.Add(pickup);

            // Apply immediately
            try { PickupSolver.Solve(sys); } catch { }

            return $"Pickup added: surface {targetSurface} {param} = surface {sourceSurface} * {scale} + {offset}. Total pickups: {sys.Pickups.Count}.";
        }

        [McpServerTool, Description("Remove a pickup solve by index (0-based).")]
        public string RemovePickup(int index)
        {
            var sys = _session.System;
            if (index < 0 || index >= sys.Pickups.Count)
                return $"Pickup index {index} out of range (0-{sys.Pickups.Count - 1}).";

            var p = sys.Pickups[index];
            sys.Pickups.RemoveAt(index);
            return $"Removed pickup {index} ({p.Parameter} surface {p.TargetSurfaceIndex} from {p.SourceSurfaceIndex}). Remaining: {sys.Pickups.Count}.";
        }

        [McpServerTool, Description("Apply all pickup solves to update target surface values from their source surfaces.")]
        public string ApplyPickups()
        {
            var sys = _session.System;
            if (sys.Pickups.Count == 0)
                return "No pickup solves to apply.";

            PickupSolver.Solve(sys);
            return $"Applied {sys.Pickups.Count} pickup solve(s).";
        }
    }
}
