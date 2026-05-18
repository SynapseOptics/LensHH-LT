using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LensHH.Core.Configuration;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    public static class LhltReader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };

        public static LhltReadResult Read(string path)
        {
            var json = File.ReadAllText(path);
            json = MigrateJson(json);
            var file = JsonSerializer.Deserialize<LhltFile>(json, JsonOptions);
            if (file == null)
                throw new InvalidOperationException("Failed to deserialize LHLT file.");

            return FromLhltFile(file);
        }

        /// <summary>
        /// Migrate JSON before deserialization. Currently only handles the
        /// removed Paraxial ray-aiming mode → Off rewrite. The earlier
        /// operand-name table (EFFL→EFL, MAX_CV→CV, etc.) was dropped on
        /// 2026-05-04 because no in-the-wild .lhlt files use those names.
        /// </summary>
        private static string MigrateJson(string json)
        {
            // Ray aiming: Paraxial removed, map to Off
            json = json.Replace("\"Paraxial\"", "\"Off\"");
            return json;
        }

        public static LhltReadResult FromLhltFile(LhltFile file)
        {
            var system = new OpticalSystem
            {
                Title = file.Title,
                Notes = file.Notes ?? string.Empty,
                Designer = file.Designer ?? string.Empty,
                Aperture = new Aperture(file.Aperture.Type, file.Aperture.Value),
                FieldType = file.FieldType,
                RayAiming = file.RayAiming,
                IsAfocal = file.IsAfocal,
                PenalizeVignetting = file.PenalizeVignetting,
                GlassCatalogs = file.GlassCatalogs ?? new System.Collections.Generic.List<string>()
            };

            // Surfaces
            foreach (var ls in file.Surfaces)
            {
                var s = new Surface
                {
                    Index = ls.Index,
                    Type = ls.Type,
                    Comment = ls.Comment ?? string.Empty,
                    Radius = ls.Radius,
                    // Legacy .lhlt files (and any written before importers
                    // normalized 1e20) store the object-at-infinity sentinel
                    // numerically. Normalize on read so the engine sees
                    // double.PositiveInfinity.
                    Thickness = Math.Abs(ls.Thickness) > 1e18 ? double.PositiveInfinity : ls.Thickness,
                    Material = ls.Material ?? string.Empty,
                    SemiDiameter = ls.SemiDiameter,
                    SemiDiameterMode = ls.SemiDiameterMode,
                    ClearAperturePercent = ls.ClearAperturePercent > 0 ? ls.ClearAperturePercent : 100.0,
                    Conic = ls.Conic,
                    IsStop = ls.IsStop,
                    InnerRadius = ls.InnerRadius,
                    ObscurationRadius = ls.ObscurationRadius,
                    FloatingApertureRadius = ls.FloatingApertureRadius,
                    CurvatureVariable = ls.CurvatureVariable,
                    ThicknessVariable = ls.ThicknessVariable,
                    ConicVariable = ls.ConicVariable,
                    HasMarginalRaySolve = ls.HasMarginalRaySolve,
                    CurvatureMin = ls.CurvatureMin,
                    CurvatureMax = ls.CurvatureMax,
                    ThicknessMin = ls.ThicknessMin,
                    ThicknessMax = ls.ThicknessMax,
                    ConicMin = ls.ConicMin,
                    ConicMax = ls.ConicMax
                };

                if (ls.AsphericCoefficients != null)
                {
                    int len = Math.Min(ls.AsphericCoefficients.Length, s.AsphericCoefficients.Length);
                    Array.Copy(ls.AsphericCoefficients, s.AsphericCoefficients, len);
                }

                if (ls.AsphericVariable != null)
                {
                    int len = Math.Min(ls.AsphericVariable.Length, s.AsphericVariable.Length);
                    Array.Copy(ls.AsphericVariable, s.AsphericVariable, len);
                }

                if (ls.AsphericMin != null)
                {
                    int len = Math.Min(ls.AsphericMin.Length, s.AsphericMin.Length);
                    Array.Copy(ls.AsphericMin, s.AsphericMin, len);
                }

                if (ls.AsphericMax != null)
                {
                    int len = Math.Min(ls.AsphericMax.Length, s.AsphericMax.Length);
                    Array.Copy(ls.AsphericMax, s.AsphericMax, len);
                }

                system.Surfaces.Add(s);
            }

            // Wavelengths
            foreach (var w in file.Wavelengths)
            {
                system.Wavelengths.Add(new Wavelength(w.Value, w.Weight, w.IsPrimary));
            }

            // Fields
            foreach (var f in file.Fields)
            {
                system.Fields.Add(new Field(f.Y, f.Weight)
                {
                    Variable = f.Variable,
                    Min = f.Min,
                    Max = f.Max
                });
            }
            FieldValidation.FilterImportedFields(system);

            // Pickups
            foreach (var p in file.Pickups)
            {
                system.Pickups.Add(new Pickup
                {
                    TargetSurfaceIndex = p.TargetSurfaceIndex,
                    Parameter = p.Parameter,
                    SourceSurfaceIndex = p.SourceSurfaceIndex,
                    SourceConfigurationIndex = p.SourceConfigurationIndex,
                    ScaleFactor = p.ScaleFactor,
                    Offset = p.Offset
                });
            }

            // Glass substitution settings
            if (file.GlassSubstitutions != null)
            {
                foreach (var gs in file.GlassSubstitutions)
                {
                    system.GlassSubstitutions.Add(new GlassSubstitutionSetting
                    {
                        SurfaceIndex = gs.SurfaceIndex,
                        Substitute = gs.Substitute,
                        CatalogName = gs.CatalogName ?? string.Empty
                    });
                }
            }

            // Merit function
            MeritFunction.MeritFunction? meritFunction = null;
            if (file.MeritFunction != null && file.MeritFunction.Operands.Count > 0)
            {
                meritFunction = new MeritFunction.MeritFunction();
                foreach (var lo in file.MeritFunction.Operands)
                {
                    meritFunction.AddOperand(new Operand
                    {
                        Type = lo.Type,
                        Target = lo.Target,
                        Weight = lo.Weight,
                        Minimum = lo.Minimum,
                        Maximum = lo.Maximum,
                        OpCode = lo.OpCode,
                        ConfigurationNo = lo.ConfigurationNo,
                        SurfaceIndex = lo.SurfaceIndex,
                        WaveIndex = lo.WaveIndex,
                        Hx = lo.Hx,
                        Hy = lo.Hy,
                        Px = lo.Px,
                        Py = lo.Py,
                        Rings = lo.Rings,
                        Arms = lo.Arms,
                        GridSize = lo.GridSize,
                        OperandNo = lo.OperandNo,
                        OperandNo2 = lo.OperandNo2,
                        Factor = lo.Factor,
                        Surface1 = lo.Surface1,
                        Surface2 = lo.Surface2
                    });
                }
            }

            // Configuration editor
            ConfigurationEditor? configEditor = null;
            if (file.ConfigurationEditor != null && file.ConfigurationEditor.Operands.Count > 0)
            {
                configEditor = new ConfigurationEditor();

                // Add operands first
                foreach (var co in file.ConfigurationEditor.Operands)
                {
                    configEditor.AddOperand(new ConfigOperand(co.Type, co.SurfaceIndex, co.AsphericTermIndex));
                }

                // Set number of configurations
                int numConfigs = file.ConfigurationEditor.Configurations.Count;
                if (numConfigs > 0)
                {
                    configEditor.SetNumberOfConfigurations(numConfigs);

                    // Populate values
                    for (int c = 0; c < numConfigs; c++)
                    {
                        var cv = file.ConfigurationEditor.Configurations[c];
                        for (int i = 0; i < configEditor.OperandCount && i < cv.Values.Length; i++)
                        {
                            configEditor.SetValue(c, i, cv.Values[i]);
                        }

                        if (cv.GlassValues != null)
                        {
                            for (int i = 0; i < configEditor.OperandCount && i < cv.GlassValues.Length; i++)
                            {
                                if (cv.GlassValues[i] != null &&
                                    configEditor.Operands[i].Type == ConfigOperandType.Glass)
                                {
                                    configEditor.SetGlass(c, i, cv.GlassValues[i]!);
                                }
                            }
                        }

                        if (cv.VariableFlags != null)
                        {
                            for (int i = 0; i < configEditor.OperandCount && i < cv.VariableFlags.Length; i++)
                            {
                                if (cv.VariableFlags[i])
                                {
                                    double? mn = cv.MinValues != null && i < cv.MinValues.Length ? cv.MinValues[i] : null;
                                    double? mx = cv.MaxValues != null && i < cv.MaxValues.Length ? cv.MaxValues[i] : null;
                                    configEditor.SetVariable(c, i, true, mn, mx);
                                }
                            }
                        }
                    }
                }

                configEditor.ActiveConfiguration = file.ConfigurationEditor.ActiveConfiguration;
            }

            return new LhltReadResult
            {
                System = system,
                MeritFunction = meritFunction,
                ConfigEditor = configEditor
            };
        }
    }

    public class LhltReadResult
    {
        public OpticalSystem System { get; set; } = null!;
        public MeritFunction.MeritFunction? MeritFunction { get; set; }
        public ConfigurationEditor? ConfigEditor { get; set; }
    }
}
