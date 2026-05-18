using System.Collections.Generic;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Pure utilities for splicing one optical system's lens vertices into
    /// another: extract the contiguous refractive chunk between source-stop
    /// and source-IMG, optionally reverse it 180 degrees, and deep-clone
    /// individual Surface instances.
    ///
    /// Lives in LensHH.IO so both the MCP server (StockLensTools,
    /// BatchDesignSearchService) and the LT App (Insert-Lens-from-File
    /// GUI buttons) can share them without duplicating logic.
    ///
    /// No SQLite or catalog plumbing in this file — those live in
    /// LensHH.Mcp.StockLensCatalog.
    /// </summary>
    public static class LensInsertHelpers
    {
        /// <summary>
        /// Return just the optical-vertex surfaces from a loaded .lhlt: skip
        /// OBJ (index 0), the dummy aperture stop our stock-lens import
        /// inserts (the next surface with IsStop=true), and IMG (last surface).
        /// The remaining surfaces are the lens body. Each is a deep clone, so
        /// the caller can safely splice into a different system.
        ///
        /// Returns null on shape mismatch (no stop found, fewer than 4 surfaces, etc.)
        /// with a human-readable reason in <paramref name="error"/>.
        /// </summary>
        public static List<Surface>? ExtractLensVertices(OpticalSystem system, out string? error)
        {
            error = null;
            int n = system.Surfaces.Count;
            if (n < 4)
            {
                error = $"system has only {n} surface(s); need at least 4 (OBJ + stop + lens + IMG)";
                return null;
            }

            int stopIdx = -1;
            for (int i = 1; i < n - 1; i++)
            {
                if (system.Surfaces[i].IsStop) { stopIdx = i; break; }
            }
            if (stopIdx < 0)
            {
                error = "no stop surface; cannot identify the lens range";
                return null;
            }

            var verts = new List<Surface>();
            for (int i = stopIdx + 1; i < n - 1; i++)
                verts.Add(CloneSurface(system.Surfaces[i]));
            // Force IsStop=false on extracted vertices — the host has its own stop.
            foreach (var s in verts) s.IsStop = false;
            return verts;
        }

        /// <summary>
        /// Reverse a lens group: radii negate, surface order mirrors,
        /// internal thicknesses + materials shift one slot. Trailing thickness
        /// on the final surface is preserved (host-system air gap, unchanged
        /// by a 180-deg flip of the lens body).
        /// </summary>
        public static List<Surface> ReverseVertexGroup(List<Surface> vertices)
        {
            int n = vertices.Count;
            var reversed = new List<Surface>(n);
            for (int i = 0; i < n; i++)
            {
                var src = CloneSurface(vertices[n - 1 - i]);
                src.Radius = -vertices[n - 1 - i].Radius;
                if (i < n - 1)
                {
                    src.Thickness = vertices[n - 2 - i].Thickness;
                    src.Material  = vertices[n - 2 - i].Material;
                }
                else
                {
                    src.Thickness = vertices[n - 1].Thickness;
                    src.Material  = vertices[n - 1].Material;
                }
                reversed.Add(src);
            }
            return reversed;
        }

        public static Surface CloneSurface(Surface s)
        {
            return new Surface
            {
                Index                  = s.Index,
                Type                   = s.Type,
                Comment                = s.Comment,
                Radius                 = s.Radius,
                Thickness              = s.Thickness,
                Material               = s.Material,
                SemiDiameter           = s.SemiDiameter,
                SemiDiameterMode       = s.SemiDiameterMode,
                ClearAperturePercent   = s.ClearAperturePercent,
                Conic                  = s.Conic,
                AsphericCoefficients   = s.AsphericCoefficients != null ? (double[])s.AsphericCoefficients.Clone() : new double[8],
                CurvatureVariable      = s.CurvatureVariable,
                ThicknessVariable      = s.ThicknessVariable,
                ConicVariable          = s.ConicVariable,
                AsphericVariable       = s.AsphericVariable != null ? (bool[])s.AsphericVariable.Clone() : new bool[8],
                HasMarginalRaySolve    = s.HasMarginalRaySolve,
                IsStop                 = s.IsStop,
                InnerRadius            = s.InnerRadius,
                ClapOuterRadius        = s.ClapOuterRadius,
                ObscurationRadius      = s.ObscurationRadius,
                FloatingApertureRadius = s.FloatingApertureRadius,
            };
        }
    }
}
