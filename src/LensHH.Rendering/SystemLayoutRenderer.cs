using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class SystemLayoutRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // Shared 15-entry qualitative palette so layouts with up to 15
        // fields keep distinct ray colors. Slightly muted relative to the
        // shared palette historically — we now use the unmuted shared
        // palette for visual consistency with the chart legends.
        private static readonly string[] FieldColors = RenderingOptions.DefaultPalette15;

        /// <summary>
        /// Render system layout as an SVG string.
        /// </summary>
        public static string Render(SystemLayoutResult layout, string title = "",
            int width = 800, int height = 400, RenderingOptions? options = null,
            IReadOnlyList<double>? fieldYs = null, string? fieldUnit = null)
        {
            var opt = options ?? new RenderingOptions();
            int marginX = 60, marginY = 65;
            int plotW = width - 2 * marginX;
            int plotH = height - 2 * marginY;

            // Auto-scale to fit
            double totalLen = layout.TotalLength;
            if (totalLen < 1e-6) totalLen = 10;
            double maxSD = layout.MaxSemiDiameter;
            if (maxSD < 1e-6) maxSD = 5;

            // Include traced-ray extents in the viewport so off-axis rays for
            // wide-angle systems aren't clipped at the top/bottom edges. On a
            // 100°-FOV lens the chief-ray image height (focal_length·tan40°
            // ≈ 55 mm) easily exceeds the front element's 16 mm semi-diameter
            // — without this expansion the rays disappear off-screen and the
            // user sees no convergence at the image plane.
            double maxAbsRayY = 0;
            foreach (var rp in layout.RayPaths)
            {
                foreach (var (_, y) in rp.Points)
                {
                    double a = Math.Abs(y);
                    if (a > maxAbsRayY) maxAbsRayY = a;
                }
            }
            if (maxAbsRayY > maxSD) maxSD = maxAbsRayY;

            // Add padding for incoming rays lead-in and sag
            double leadIn = layout.StartsFromSurface1 ? Math.Max(totalLen * 0.15, 5) : 0;
            double zPadLeft = leadIn + totalLen * 0.05;
            double zPadRight = totalLen * 0.05;
            double totalZRange = totalLen + zPadLeft + zPadRight;
            double scaleZ = plotW / totalZRange;
            double scaleY = plotH / (2.2 * maxSD);
            double scale = Math.Min(scaleZ, scaleY);

            // Center the content if scaleY is the limiting factor
            double drawnWidth = totalZRange * scale;
            double xOffset = marginX + (plotW - drawnWidth) / 2.0;

            double minZ = -zPadLeft;
            double centerY = height / 2.0;

            double SvgX(double z) => xOffset + (z - minZ) * scale;
            double SvgY(double y) => centerY - y * scale;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{width}\" height=\"{height}\" fill=\"white\"/>"));

            // Title
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{width / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Optical axis
            double axisX1 = SvgX(minZ);
            double axisX2 = SvgX(totalLen + zPadRight);
            sb.AppendLine(F($"<line x1=\"{axisX1:F1}\" y1=\"{centerY:F1}\" x2=\"{axisX2:F1}\" y2=\"{centerY:F1}\" stroke=\"#999\" stroke-width=\"0.5\" stroke-dasharray=\"4,2\"/>"));

            // Glass elements (filled polygons)
            foreach (var elem in layout.Elements)
            {
                RenderElement(sb, elem, scale, minZ, centerY, xOffset);
            }

            // Aperture stop
            foreach (var surf in layout.Surfaces)
            {
                if (surf.IsStop)
                {
                    double sx = SvgX(surf.VertexZ);
                    double stopY = surf.SemiDiameter * scale;
                    double barLen = 6;
                    // Top bar
                    sb.AppendLine(F($"<line x1=\"{sx:F1}\" y1=\"{centerY - stopY - barLen:F1}\" x2=\"{sx:F1}\" y2=\"{centerY - stopY:F1}\" stroke=\"black\" stroke-width=\"1.5\"/>"));
                    // Bottom bar
                    sb.AppendLine(F($"<line x1=\"{sx:F1}\" y1=\"{centerY + stopY:F1}\" x2=\"{sx:F1}\" y2=\"{centerY + stopY + barLen:F1}\" stroke=\"black\" stroke-width=\"1.5\"/>"));
                }

                // Object surface — curved arc or vertical line
                if (surf.Index == 0)
                {
                    double sd = surf.SemiDiameter;
                    if (sd < 1e-6) sd = maxSD * 0.5;

                    if (Math.Abs(surf.Curvature) > 1e-12)
                    {
                        int nPts = 41;
                        var objPath = new StringBuilder();
                        for (int ip = 0; ip <= nPts; ip++)
                        {
                            double y = sd * (1.0 - 2.0 * ip / nPts);
                            double sag = SystemLayout.ComputeSag(y, surf.Curvature, surf.Conic, surf.AsphericCoeffs);
                            double px = SvgX(surf.VertexZ + sag);
                            double py = SvgY(y);
                            if (ip == 0)
                                objPath.Append(F($"M {px:F2},{py:F2}"));
                            else
                                objPath.Append(F($" L {px:F2},{py:F2}"));
                        }
                        sb.AppendLine(F($"<path d=\"{objPath}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>"));
                    }
                    else
                    {
                        double ox = SvgX(surf.VertexZ);
                        double oy = sd * scale;
                        sb.AppendLine(F($"<line x1=\"{ox:F1}\" y1=\"{centerY - oy:F1}\" x2=\"{ox:F1}\" y2=\"{centerY + oy:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                    }
                }

                // Image surface — curved arc or vertical line
                if (surf.IsImage)
                {
                    double sd = surf.SemiDiameter;
                    // Surface.SemiDiameter at the image is often computed
                    // from ray heights at the image vertex and can collapse
                    // to the on-axis spot radius (e.g. <0.1 mm) when the
                    // off-axis chief-ray height was missed. Drawing a sub-
                    // millimeter line on a 60 mm canvas leaves the image
                    // plane invisible. Fall back to the largest |y| any
                    // traced ray reached at the image Z (≈ chief-ray height
                    // of the highest field) so the line scales with the
                    // ray fan you can actually see on the layout.
                    double imgZ = surf.VertexZ;
                    double rayMaxAbsY = 0;
                    foreach (var rp in layout.RayPaths)
                    {
                        if (rp.Points.Count == 0) continue;
                        var (lastZ, lastY) = rp.Points[rp.Points.Count - 1];
                        if (Math.Abs(lastZ - imgZ) < 1e-3)
                        {
                            double a = Math.Abs(lastY);
                            if (a > rayMaxAbsY) rayMaxAbsY = a;
                        }
                    }
                    if (rayMaxAbsY > sd) sd = rayMaxAbsY;
                    if (sd < 1e-6) sd = maxSD * 0.5;

                    if (Math.Abs(surf.Curvature) > 1e-12)
                    {
                        // Draw curved image surface
                        int nPts = 41;
                        var imgPath = new StringBuilder();
                        for (int ip = 0; ip <= nPts; ip++)
                        {
                            double y = sd * (1.0 - 2.0 * ip / nPts);
                            double sag = SystemLayout.ComputeSag(y, surf.Curvature, surf.Conic, surf.AsphericCoeffs);
                            double px = SvgX(surf.VertexZ + sag);
                            double py = SvgY(y);
                            if (ip == 0)
                                imgPath.Append(F($"M {px:F2},{py:F2}"));
                            else
                                imgPath.Append(F($" L {px:F2},{py:F2}"));
                        }
                        sb.AppendLine(F($"<path d=\"{imgPath}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>"));
                    }
                    else
                    {
                        // Flat image surface — vertical line
                        double ix = SvgX(surf.VertexZ);
                        double iy = sd * scale;
                        sb.AppendLine(F($"<line x1=\"{ix:F1}\" y1=\"{centerY - iy:F1}\" x2=\"{ix:F1}\" y2=\"{centerY + iy:F1}\" stroke=\"black\" stroke-width=\"1\"/>"));
                    }
                }
            }

            // Mirror surfaces (drawn as thick curved lines, not filled).
            // If InnerRadius > 0 (central hole, e.g. Cassegrain primary), draw
            // two arcs with a gap in the center.
            foreach (var surf in layout.Surfaces)
            {
                if (!surf.IsMirror) continue;
                double sd = surf.SemiDiameter;
                double inner = surf.InnerRadius;
                int nPts = 51;

                if (inner > 0 && inner < sd)
                {
                    // Upper arc: +sd to +inner
                    var upperPath = new StringBuilder();
                    for (int ip = 0; ip <= nPts; ip++)
                    {
                        double y = sd - (sd - inner) * ip / nPts;
                        double sag = SystemLayout.ComputeSag(y, surf.Curvature, surf.Conic, surf.AsphericCoeffs);
                        double px = SvgX(surf.VertexZ + sag);
                        double py = SvgY(y);
                        upperPath.Append(ip == 0 ? F($"M {px:F2},{py:F2}") : F($" L {px:F2},{py:F2}"));
                    }
                    sb.AppendLine(F($"<path d=\"{upperPath}\" fill=\"none\" stroke=\"#444\" stroke-width=\"2.5\"/>"));

                    // Lower arc: -inner to -sd
                    var lowerPath = new StringBuilder();
                    for (int ip = 0; ip <= nPts; ip++)
                    {
                        double y = -inner - (sd - inner) * ip / nPts;
                        double sag = SystemLayout.ComputeSag(y, surf.Curvature, surf.Conic, surf.AsphericCoeffs);
                        double px = SvgX(surf.VertexZ + sag);
                        double py = SvgY(y);
                        lowerPath.Append(ip == 0 ? F($"M {px:F2},{py:F2}") : F($" L {px:F2},{py:F2}"));
                    }
                    sb.AppendLine(F($"<path d=\"{lowerPath}\" fill=\"none\" stroke=\"#444\" stroke-width=\"2.5\"/>"));
                }
                else
                {
                    // Solid mirror (no central hole)
                    var mirrorPath = new StringBuilder();
                    for (int ip = 0; ip <= nPts; ip++)
                    {
                        double y = sd * (1.0 - 2.0 * ip / nPts);
                        double sag = SystemLayout.ComputeSag(y, surf.Curvature, surf.Conic, surf.AsphericCoeffs);
                        double px = SvgX(surf.VertexZ + sag);
                        double py = SvgY(y);
                        mirrorPath.Append(ip == 0 ? F($"M {px:F2},{py:F2}") : F($" L {px:F2},{py:F2}"));
                    }
                    sb.AppendLine(F($"<path d=\"{mirrorPath}\" fill=\"none\" stroke=\"#444\" stroke-width=\"2.5\"/>"));
                }
            }

            // Rays — count traced rays per field for the summary panel.
            // A "traced" ray is one with >= 2 points (renderable as a
            // polyline). A 1-point ray represents a trace that died at
            // surface 0 (typically failed aperture aiming or first-
            // surface refraction); we still draw a small marker for it
            // so the user can see the entrance point and the colour for
            // that field, instead of silently losing the ray.
            var rayCountByField = new Dictionary<int, int>();
            var stubCountByField = new Dictionary<int, int>();
            foreach (var ray in layout.RayPaths)
            {
                int fIdx = ray.FieldIndex;
                string color = FieldColors[fIdx % FieldColors.Length];

                if (ray.Points.Count >= 2)
                {
                    var pts = new StringBuilder();
                    foreach (var (z, y) in ray.Points)
                    {
                        double rx = SvgX(z);
                        double ry = SvgY(y);
                        if (pts.Length > 0) pts.Append(" ");
                        pts.Append(F($"{rx:F2},{ry:F2}"));
                    }
                    sb.AppendLine(F($"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"0.7\" opacity=\"0.8\"/>"));
                    rayCountByField[fIdx] = (rayCountByField.TryGetValue(fIdx, out var rc) ? rc : 0) + 1;
                }
                else if (ray.Points.Count == 1)
                {
                    var (z0, y0) = ray.Points[0];
                    double mx = SvgX(z0);
                    double my = SvgY(y0);
                    // Outline-only X-marker for failed-at-entry rays. Using
                    // a 4-px X (two crossed lines) instead of a filled dot
                    // so it's still visible against busy ray bundles.
                    sb.AppendLine(F($"<line x1=\"{mx - 3:F1}\" y1=\"{my - 3:F1}\" x2=\"{mx + 3:F1}\" y2=\"{my + 3:F1}\" stroke=\"{color}\" stroke-width=\"1.2\" opacity=\"0.9\"/>"));
                    sb.AppendLine(F($"<line x1=\"{mx - 3:F1}\" y1=\"{my + 3:F1}\" x2=\"{mx + 3:F1}\" y2=\"{my - 3:F1}\" stroke=\"{color}\" stroke-width=\"1.2\" opacity=\"0.9\"/>"));
                    stubCountByField[fIdx] = (stubCountByField.TryGetValue(fIdx, out var sc) ? sc : 0) + 1;
                }
                // Count == 0: nothing meaningful to draw, skip.
            }

            // Per-field summary panel — top-right. Lists every field
            // (when fieldYs is provided) with its traced/expected count
            // and the ray colour swatch. Fields with zero traced rays
            // are highlighted in red so the user can see exactly where
            // the layout's blank fields are coming from.
            RenderFieldSummary(sb, fieldYs, fieldUnit, rayCountByField, stubCountByField, width, marginX);

            // Scale bar
            double totalTrack = layout.FullTotalTrack > 0 ? layout.FullTotalTrack : layout.TotalLength;
            RenderScaleBar(sb, scale, width, height, marginX, marginY, totalTrack, layout.TraceWavelengthMicrons);

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render as a complete HTML page with embedded SVG.
        /// </summary>
        public static string RenderPage(SystemLayoutResult layout, string title = "",
            int width = 900, int height = 450, RenderingOptions? options = null,
            IReadOnlyList<double>? fieldYs = null, string? fieldUnit = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<style>body{font-family:sans-serif;margin:20px;} svg{border:1px solid #ccc;}</style>");
            sb.AppendLine("</head><body>");
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine($"<h2>{Esc(title)}</h2>");
            sb.AppendLine(Render(layout, title, width, height, options, fieldYs, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void RenderElement(StringBuilder sb, LayoutElement elem,
            double scale, double minZ, double centerY, double xOffset)
        {
            double SvgX(double z) => xOffset + (z - minZ) * scale;
            double SvgY(double y) => centerY - y * scale;
            double FrontZ(double y) => elem.FrontVertexZ +
                SystemLayout.ComputeSag(y, elem.FrontCurvature, elem.FrontConic, elem.FrontAsphericCoeffs);
            double BackZ(double y) => elem.BackVertexZ +
                SystemLayout.ComputeSag(y, elem.BackCurvature, elem.BackConic, elem.BackAsphericCoeffs);

            int nPts = 51;
            var pathPoints = new StringBuilder();
            double frontSD = elem.FrontSemiDiameter;
            double backSD = elem.BackSemiDiameter;

            // RIM Method (general — works whether front or back surface
            // has the larger CA):
            {
                //   1) Front Surface
                //   2) Back Surface
                //   3) Top Edge    (horizontal at y = +max(SD))
                //   4) Bottom Edge (horizontal at y = -max(SD))
                //
                // The LARGER-CA surface is drawn with its full natural sag.
                // The SMALLER-CA surface is drawn with natural sag up to its
                // own CA, then VERTICAL LINES at x = (smaller surface's z at
                // ±smallerSD) extend to y = ±largerSD where the horizontal
                // top/bottom edges close the polygon.
                //
                // No hard-coded "front is larger" assumption.

                bool frontIsLarger = frontSD >= backSD;

                if (frontIsLarger)
                {
                    // Larger = FRONT. Vertical extensions on the BACK side.
                    // Path: front curve → bottom edge (y=-frontSD horizontal) →
                    //       bottom vertical (x=BackZ(-backSD)) → back curve →
                    //       top vertical (x=BackZ(+backSD)) → top edge (Z close).

                    // Front curve top→bottom (own SD, full natural sag)
                    for (int i = 0; i <= nPts; i++)
                    {
                        double y = frontSD * (1.0 - 2.0 * i / nPts);
                        double z = FrontZ(y);
                        double sx = SvgX(z), sy = SvgY(y);
                        if (i == 0) pathPoints.Append(F($"M {sx:F2},{sy:F2}"));
                        else pathPoints.Append(F($" L {sx:F2},{sy:F2}"));
                    }

                    // Bottom edge: horizontal at y=-frontSD across to back's outer z
                    pathPoints.Append(F($" L {SvgX(BackZ(-backSD)):F2},{SvgY(-frontSD):F2}"));
                    // Bottom vertical: at x=BackZ(-backSD) up to y=-backSD
                    pathPoints.Append(F($" L {SvgX(BackZ(-backSD)):F2},{SvgY(-backSD):F2}"));

                    // Back curve bottom→top (own SD)
                    for (int i = 0; i <= nPts; i++)
                    {
                        double y = backSD * (-1.0 + 2.0 * i / nPts);
                        double z = BackZ(y);
                        pathPoints.Append(F($" L {SvgX(z):F2},{SvgY(y):F2}"));
                    }

                    // Top vertical: at x=BackZ(+backSD) up to y=+frontSD
                    pathPoints.Append(F($" L {SvgX(BackZ(backSD)):F2},{SvgY(frontSD):F2}"));
                    // Top edge: Z closes horizontally at y=+frontSD to start.
                }
                else
                {
                    // Larger = BACK. Vertical extensions on the FRONT side.
                    // Path: front curve → bottom vertical (x=FrontZ(-frontSD)) →
                    //       bottom edge (y=-backSD horizontal) → back curve →
                    //       top edge (y=+backSD horizontal) → top vertical
                    //       (x=FrontZ(+frontSD)) → Z close.

                    // Front curve top→bottom (own SD, full natural sag)
                    for (int i = 0; i <= nPts; i++)
                    {
                        double y = frontSD * (1.0 - 2.0 * i / nPts);
                        double z = FrontZ(y);
                        double sx = SvgX(z), sy = SvgY(y);
                        if (i == 0) pathPoints.Append(F($"M {sx:F2},{sy:F2}"));
                        else pathPoints.Append(F($" L {sx:F2},{sy:F2}"));
                    }

                    // Bottom vertical: at x=FrontZ(-frontSD) from y=-frontSD down to y=-backSD
                    pathPoints.Append(F($" L {SvgX(FrontZ(-frontSD)):F2},{SvgY(-backSD):F2}"));
                    // Bottom edge: horizontal at y=-backSD across to back's outer z
                    pathPoints.Append(F($" L {SvgX(BackZ(-backSD)):F2},{SvgY(-backSD):F2}"));

                    // Back curve bottom→top (own SD)
                    for (int i = 0; i <= nPts; i++)
                    {
                        double y = backSD * (-1.0 + 2.0 * i / nPts);
                        double z = BackZ(y);
                        pathPoints.Append(F($" L {SvgX(z):F2},{SvgY(y):F2}"));
                    }

                    // Top edge: horizontal at y=+backSD across to front's outer z
                    pathPoints.Append(F($" L {SvgX(FrontZ(frontSD)):F2},{SvgY(backSD):F2}"));
                    // Top vertical: Z closes from (FrontZ(+frontSD), +backSD) to start
                    // (FrontZ(+frontSD), +frontSD) — that's the vertical segment.
                }
            }

            pathPoints.Append(" Z");
            sb.AppendLine(F($"<path d=\"{pathPoints}\" fill=\"#B8D4F0\" stroke=\"#4477AA\" stroke-width=\"0.8\" opacity=\"0.7\"/>"));
        }

        private static void RenderScaleBar(StringBuilder sb, double scale,
            int width, int height, int marginX, int marginY, double totalTrack = 0,
            double traceWavelengthMicrons = 0)
        {
            // Find a nice round scale bar length — target 25% of canvas width
            double targetPixels = width * 0.25;
            double targetMm = targetPixels / scale;

            // Round to nice value: 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100...
            double mag = Math.Pow(10, Math.Floor(Math.Log10(targetMm)));
            double norm = targetMm / mag;
            double nice;
            if (norm < 1.5) nice = 1;
            else if (norm < 3.5) nice = 2;
            else if (norm < 7.5) nice = 5;
            else nice = 10;
            double barMm = nice * mag;
            double barPx = barMm * scale;

            // Center the scale bar horizontally
            double bx = (width - barPx) / 2.0;
            double by = height - marginY + 25;
            double tickH = 6;

            sb.AppendLine(F($"<line x1=\"{bx:F1}\" y1=\"{by:F1}\" x2=\"{bx + barPx:F1}\" y2=\"{by:F1}\" stroke=\"black\" stroke-width=\"2\"/>"));
            sb.AppendLine(F($"<line x1=\"{bx:F1}\" y1=\"{by - tickH:F1}\" x2=\"{bx:F1}\" y2=\"{by + tickH:F1}\" stroke=\"black\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<line x1=\"{bx + barPx:F1}\" y1=\"{by - tickH:F1}\" x2=\"{bx + barPx:F1}\" y2=\"{by + tickH:F1}\" stroke=\"black\" stroke-width=\"1.5\"/>"));

            string label = LabelFormat.WithUnit(barMm, "mm");
            sb.AppendLine(F($"<text x=\"{bx + barPx / 2:F1}\" y=\"{by + 16:F1}\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\" fill=\"#333\">{label}</text>"));

            // Total track label
            if (totalTrack > 0)
            {
                string ttLabel = $"Total Track: {LabelFormat.Auto(totalTrack)} mm";
                if (traceWavelengthMicrons > 0)
                    ttLabel += $"   \u03BB = {traceWavelengthMicrons.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} \u00B5m";
                sb.AppendLine(F($"<text x=\"{width / 2.0:F1}\" y=\"{by + 32:F1}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#555\">{ttLabel}</text>"));
            }
            else if (traceWavelengthMicrons > 0)
            {
                string wlLabel = $"\u03BB = {traceWavelengthMicrons.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} \u00B5m";
                sb.AppendLine(F($"<text x=\"{width / 2.0:F1}\" y=\"{by + 32:F1}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#555\">{wlLabel}</text>"));
            }
        }

        /// <summary>
        /// Top-right summary panel. Lists every field with a colour
        /// swatch and the traced ray count. Fields with 0 rays are
        /// highlighted (red text) so the user can see "field 2 had 0
        /// rays and field 3 had only the chief ray reach the image."
        /// Skipped silently when no <paramref name="fieldYs"/> is
        /// supplied (preserves the older layouts in formats that don't
        /// have access to the system's field list).
        /// </summary>
        private static void RenderFieldSummary(
            StringBuilder sb,
            IReadOnlyList<double>? fieldYs,
            string? fieldUnit,
            Dictionary<int, int> rayCountByField,
            Dictionary<int, int> stubCountByField,
            int width,
            int marginX)
        {
            if (fieldYs == null || fieldYs.Count == 0) return;

            string unit = string.IsNullOrEmpty(fieldUnit) ? "" : " " + fieldUnit;
            double rowH = 14;
            double padX = 8;
            double padY = 8;
            int rows = fieldYs.Count;
            double panelW = 150;
            double panelH = padY * 2 + rowH * rows + 4;
            double panelX = width - marginX - panelW;
            double panelY = 38;

            sb.AppendLine(F($"<rect x=\"{panelX:F1}\" y=\"{panelY:F1}\" width=\"{panelW:F1}\" height=\"{panelH:F1}\" fill=\"white\" fill-opacity=\"0.92\" stroke=\"#bbb\" stroke-width=\"0.5\" rx=\"3\"/>"));
            sb.AppendLine(F($"<text x=\"{panelX + padX:F1}\" y=\"{panelY + padY + 9:F1}\" font-size=\"10\" font-weight=\"bold\" fill=\"#333\">Fields</text>"));

            for (int i = 0; i < fieldYs.Count; i++)
            {
                double y = panelY + padY + 4 + (i + 1) * rowH;
                int traced = rayCountByField.TryGetValue(i, out var t) ? t : 0;
                int stubs  = stubCountByField.TryGetValue(i, out var s) ? s : 0;
                bool failed = traced == 0;

                string colour = FieldColors[i % FieldColors.Length];

                // Colour swatch
                sb.AppendLine(F($"<rect x=\"{panelX + padX:F1}\" y=\"{y - 8:F1}\" width=\"10\" height=\"3\" fill=\"{colour}\"/>"));

                string label = $"F{i + 1}: {fieldYs[i].ToString("0.###", CultureInfo.InvariantCulture)}{unit}";
                string countText = stubs > 0
                    ? $"{traced} rays, {stubs} stub{(stubs == 1 ? "" : "s")}"
                    : $"{traced} ray{(traced == 1 ? "" : "s")}";

                string textColour = failed ? "#c00" : "#333";
                sb.AppendLine(F($"<text x=\"{panelX + padX + 14:F1}\" y=\"{y - 1:F1}\" font-size=\"10\" fill=\"{textColour}\">{Esc(label)}</text>"));
                sb.AppendLine(F($"<text x=\"{panelX + panelW - padX:F1}\" y=\"{y - 1:F1}\" font-size=\"10\" fill=\"{textColour}\" text-anchor=\"end\">{Esc(countText)}</text>"));
            }
        }

        private static string Esc(string s) => s
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
