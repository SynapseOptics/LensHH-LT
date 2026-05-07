using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace LensHH.RenderApp;

public static class SvgBitmapHelper
{
    public static Bitmap SvgToBitmap(string svg, int scale = 2,
        int defaultWidth = 800, int defaultHeight = 600)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);

        if (skSvg.Picture == null)
            throw new InvalidOperationException("Failed to parse SVG.");

        var bounds = skSvg.Picture.CullRect;
        int w = (int)bounds.Width;
        int h = (int)bounds.Height;
        if (w < 100) w = defaultWidth;
        if (h < 100) h = defaultHeight;

        using var bitmap = new SKBitmap(w * scale, h * scale);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(skSvg.Picture);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    /// <summary>
    /// Convert an HTML page containing one or more SVGs into a bitmap.
    /// When multiple SVGs are present, they are composited into rows
    /// matching the HTML layout (inline-flex groups are placed side-by-side,
    /// separate field-row divs are stacked vertically).
    /// </summary>
    public static Bitmap HtmlPageToBitmap(string html, int scale = 2)
    {
        // Collect all <svg>...</svg> elements
        var svgs = new System.Collections.Generic.List<string>();
        int searchFrom = 0;
        while (true)
        {
            int start = html.IndexOf("<svg", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int end = html.IndexOf("</svg>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            svgs.Add(html.Substring(start, end - start + 6));
            searchFrom = end + 6;
        }

        if (svgs.Count == 0)
            throw new InvalidOperationException("No SVG found in HTML content.");

        if (svgs.Count == 1)
            return SvgToBitmap(svgs[0], scale);

        // Determine how many SVGs per row from the HTML structure.
        // Renderers use inline-flex divs to group SVGs side-by-side per field.
        // Count SVGs inside the first inline-flex group to get columns.
        int cols = svgs.Count; // default: all in one row
        int flexIdx = html.IndexOf("inline-flex", StringComparison.OrdinalIgnoreCase);
        if (flexIdx >= 0)
        {
            // Find the closing </div> of this flex container and count <svg> tags inside
            int divEnd = html.IndexOf("</div>", flexIdx, StringComparison.OrdinalIgnoreCase);
            if (divEnd > flexIdx)
            {
                string group = html.Substring(flexIdx, divEnd - flexIdx);
                int count = 0;
                int idx = 0;
                while ((idx = group.IndexOf("<svg", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                { count++; idx += 4; }
                if (count > 0) cols = count;
            }
        }

        int rows = (svgs.Count + cols - 1) / cols;

        // Render each SVG individually to get dimensions and pictures
        var pictures = new System.Collections.Generic.List<(SKSvg svg, int w, int h)>();
        foreach (var s in svgs)
        {
            var skSvg = new SKSvg();
            skSvg.FromSvg(s);
            if (skSvg.Picture == null)
                throw new InvalidOperationException("Failed to parse SVG.");
            var b = skSvg.Picture.CullRect;
            int w = Math.Max((int)b.Width, 100);
            int h = Math.Max((int)b.Height, 100);
            pictures.Add((skSvg, w, h));
        }

        // Compute cell sizes per column and row
        int[] colWidths = new int[cols];
        int[] rowHeights = new int[rows];
        for (int i = 0; i < pictures.Count; i++)
        {
            int c = i % cols, r = i / cols;
            if (pictures[i].w > colWidths[c]) colWidths[c] = pictures[i].w;
            if (pictures[i].h > rowHeights[r]) rowHeights[r] = pictures[i].h;
        }

        int totalW = 0, totalH = 0;
        foreach (int cw in colWidths) totalW += cw;
        foreach (int rh in rowHeights) totalH += rh;

        using var bitmap = new SKBitmap(totalW * scale, totalH * scale);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);

        for (int i = 0; i < pictures.Count; i++)
        {
            int c = i % cols, r = i / cols;
            int x = 0, y = 0;
            for (int cc = 0; cc < c; cc++) x += colWidths[cc];
            for (int rr = 0; rr < r; rr++) y += rowHeights[rr];

            canvas.Save();
            canvas.Translate(x, y);
            canvas.DrawPicture(pictures[i].svg.Picture!);
            canvas.Restore();
        }

        // Dispose SKSvg objects
        foreach (var p in pictures) p.svg.Dispose();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
