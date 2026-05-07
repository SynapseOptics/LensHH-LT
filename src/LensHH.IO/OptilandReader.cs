using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Reads Optiland .json lens files.
    /// Optiland uses JSON with non-standard Infinity/-Infinity literals.
    /// Surface positions are cumulative Z coordinates (thicknesses must be computed as deltas).
    /// </summary>
    public static class OptilandReader
    {
        public static OpticalSystem Read(string filePath)
        {
            // Optiland JSON uses literal Infinity/-Infinity which is not valid JSON.
            // Replace with numeric placeholders before parsing.
            string rawJson = File.ReadAllText(filePath);
            rawJson = rawJson.Replace("-Infinity", "-1e308").Replace("Infinity", "1e308");

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var system = new OpticalSystem();

            // Aperture
            if (root.TryGetProperty("aperture", out var apertureProp) && apertureProp.ValueKind == JsonValueKind.Object)
            {
                string apertureType = GetString(apertureProp, "type", "EPD");
                double apertureValue = GetDouble(apertureProp, "value", 10.0);

                if (apertureType.Equals("EPD", StringComparison.OrdinalIgnoreCase))
                    system.Aperture = new Aperture(ApertureType.EPD, apertureValue);
                else if (apertureType.Equals("imageFNO", StringComparison.OrdinalIgnoreCase))
                    system.Aperture = new Aperture(ApertureType.FNumber, apertureValue);
                else
                    system.Aperture = new Aperture(ApertureType.EPD, apertureValue);
            }

            // Fields
            if (root.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Object)
            {
                // field_type can be at top level or inside field_definition
                string fieldType = GetString(fieldsProp, "field_type", "");
                if (string.IsNullOrEmpty(fieldType) && fieldsProp.TryGetProperty("field_definition", out var fdProp) && fdProp.ValueKind == JsonValueKind.Object)
                    fieldType = GetString(fdProp, "field_type", "angle");
                if (string.IsNullOrEmpty(fieldType)) fieldType = "angle";

                system.FieldType = (fieldType.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0
                    || fieldType.Equals("ObjectHeight", StringComparison.OrdinalIgnoreCase))
                    ? FieldType.ObjectHeight
                    : FieldType.ObjectAngle;

                if (fieldsProp.TryGetProperty("fields", out var fieldArray) && fieldArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fieldArray.EnumerateArray())
                    {
                        double y = GetDouble(f, "y", 0);
                        system.Fields.Add(new Field(y, 1.0));
                    }
                }
            }
            if (system.Fields.Count == 0)
                system.Fields.Add(new Field(0, 1.0));

            FieldValidation.FilterImportedFields(system);

            // Wavelengths
            if (root.TryGetProperty("wavelengths", out var wlProp) && wlProp.ValueKind == JsonValueKind.Object)
            {
                if (wlProp.TryGetProperty("wavelengths", out var wlArray) && wlArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in wlArray.EnumerateArray())
                    {
                        double value = GetDouble(w, "value", 0.55);
                        bool isPrimary = GetBool(w, "is_primary", false);
                        double weight = GetDouble(w, "weight", 1.0);
                        string unit = GetString(w, "unit", "um");

                        // Convert to micrometers
                        if (unit.Equals("nm", StringComparison.OrdinalIgnoreCase))
                            value /= 1000.0;
                        else if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase))
                            value *= 1000.0;

                        system.Wavelengths.Add(new Wavelength(value, weight, isPrimary));
                    }
                }
            }
            if (system.Wavelengths.Count == 0)
                system.Wavelengths.Add(new Wavelength(0.55, 1.0, true));

            // Surfaces
            if (root.TryGetProperty("surface_group", out var sgProp) && sgProp.ValueKind == JsonValueKind.Object)
            {
                if (sgProp.TryGetProperty("surfaces", out var surfArray) && surfArray.ValueKind == JsonValueKind.Array)
                {
                    var surfElements = new List<JsonElement>();
                    foreach (var s in surfArray.EnumerateArray())
                        surfElements.Add(s);

                    // First pass: extract Z coordinates for thickness computation
                    var zCoords = new List<double>();
                    foreach (var s in surfElements)
                    {
                        double z = 0;
                        if (s.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Object)
                        {
                            if (geom.TryGetProperty("cs", out var cs) && cs.ValueKind == JsonValueKind.Object)
                                z = GetDouble(cs, "z", 0);
                        }
                        zCoords.Add(z);
                    }

                    // Second pass: build surfaces
                    for (int i = 0; i < surfElements.Count; i++)
                    {
                        var s = surfElements[i];
                        var surface = new Surface { Index = i };

                        // Geometry
                        if (s.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Object)
                        {
                            string geomType = GetString(geom, "type", "Plane");
                            double radius = GetDouble(geom, "radius", 1e308);

                            // Convert any infinity sentinel back to real
                            // PositiveInfinity (writer now uses 1e30; older
                            // exports use 1e308 or string-replaced Infinity).
                            if (Math.Abs(radius) >= 1e10)
                                surface.Radius = double.PositiveInfinity;
                            else
                                surface.Radius = radius;

                            if (geomType.Equals("EvenAsphere", StringComparison.OrdinalIgnoreCase))
                            {
                                surface.Type = SurfaceType.EvenAsphere;
                                double conic = GetDouble(geom, "conic", 0);
                                surface.Conic = conic;

                                if (geom.TryGetProperty("coefficients", out var coeffs) && coeffs.ValueKind == JsonValueKind.Array)
                                {
                                    int idx = 0;
                                    foreach (var c in coeffs.EnumerateArray())
                                    {
                                        if (idx < surface.AsphericCoefficients.Length)
                                            surface.AsphericCoefficients[idx] = c.GetDouble();
                                        idx++;
                                    }
                                }
                            }
                            else if (geomType.Equals("StandardGeometry", StringComparison.OrdinalIgnoreCase))
                            {
                                double conic = GetDouble(geom, "conic", 0);
                                surface.Conic = conic;
                                if (conic != 0)
                                    surface.Type = SurfaceType.EvenAsphere;
                            }
                            // "Plane" → default Standard with infinite radius (already set)
                        }

                        // Thickness: prefer explicit "thickness" field, fall back to Z deltas
                        double explicitThickness = GetDouble(s, "thickness", double.NaN);
                        if (!double.IsNaN(explicitThickness))
                        {
                            surface.Thickness = explicitThickness;
                        }
                        else if (i < surfElements.Count - 1)
                        {
                            double zCurrent = zCoords[i];
                            double zNext = zCoords[i + 1];

                            // Catch any sentinel ≥ 1e10. We write 1e30 for
                            // infinity now; OpTaliX-derived files use 1e20;
                            // older tools use 1e10 / 1e11. All bigger than
                            // any real track distance.
                            if (Math.Abs(zCurrent) >= 1e10) // object at infinity
                                surface.Thickness = double.PositiveInfinity;
                            else
                                surface.Thickness = zNext - zCurrent;
                        }
                        else
                        {
                            surface.Thickness = 0; // image surface
                        }

                        // Object surface: always infinite thickness
                        if (i == 0)
                            surface.Thickness = double.PositiveInfinity;

                        // Material (from material_post)
                        if (s.TryGetProperty("material_post", out var matPost) && matPost.ValueKind == JsonValueKind.Object)
                        {
                            string matType = GetString(matPost, "type", "IdealMaterial");
                            if (matType.Equals("Material", StringComparison.OrdinalIgnoreCase) ||
                                matType.Equals("AbbeMaterial", StringComparison.OrdinalIgnoreCase))
                            {
                                string name = GetString(matPost, "name", "");
                                if (!string.IsNullOrEmpty(name))
                                    surface.Material = name;
                            }
                        }

                        // Mirror: check top-level is_reflective and interaction_model.is_reflective
                        bool isReflective = GetBool(s, "is_reflective", false);
                        if (!isReflective && s.TryGetProperty("interaction_model", out var imProp) && imProp.ValueKind == JsonValueKind.Object)
                            isReflective = GetBool(imProp, "is_reflective", false);
                        if (isReflective)
                            surface.Material = "MIRROR";

                        // Stop
                        surface.IsStop = GetBool(s, "is_stop", false);

                        // Semi-diameter (if aperture specified)
                        if (s.TryGetProperty("aperture", out var ap) && ap.ValueKind == JsonValueKind.Object)
                        {
                            double sd = GetDouble(ap, "semi_diameter", 0);
                            if (sd > 0)
                            {
                                surface.SemiDiameter = sd;
                                surface.SemiDiameterMode = SemiDiameterMode.Fixed;
                            }
                        }

                        system.Surfaces.Add(surface);
                    }
                }
            }

            // Ensure at least object + image
            if (system.Surfaces.Count < 2)
            {
                system.Surfaces.Clear();
                system.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
                system.Surfaces.Add(new Surface { Index = 1 });
            }

            return system;
        }

        private static string GetString(JsonElement el, string prop, string defaultValue)
        {
            if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString() ?? defaultValue;
            return defaultValue;
        }

        private static double GetDouble(JsonElement el, string prop, double defaultValue)
        {
            if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number)
                return val.GetDouble();
            return defaultValue;
        }

        private static bool GetBool(JsonElement el, string prop, bool defaultValue)
        {
            if (el.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
            }
            return defaultValue;
        }
    }
}
