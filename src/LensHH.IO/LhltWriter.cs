using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LensHH.Core.Configuration;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    public static class LhltWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Skip only true nulls (e.g. nullable Minimum/Maximum), NOT
            // type-default values. WhenWritingDefault was previously used
            // here and dropped any property equal to its CLR default — so
            // an operand explicitly set to Weight=0 would round-trip back
            // to Weight=1 (the property's init value) on reload, silently
            // re-enabling operands the user had disabled.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() }
        };

        public static void Write(OpticalSystem system, string path,
            MeritFunction.MeritFunction? meritFunction = null,
            ConfigurationEditor? configEditor = null)
        {
            var file = ToLhltFile(system, meritFunction, configEditor);
            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static LhltFile ToLhltFile(OpticalSystem system,
            MeritFunction.MeritFunction? meritFunction = null,
            ConfigurationEditor? configEditor = null)
        {
            var file = new LhltFile
            {
                Title = system.Title,
                Notes = system.Notes ?? string.Empty,
                Designer = system.Designer ?? string.Empty,
                Aperture = new LhltAperture
                {
                    Type = system.Aperture.Type,
                    Value = system.Aperture.Value
                },
                FieldType = system.FieldType,
                RayAiming = system.RayAiming,
                IsAfocal = system.IsAfocal,
                PenalizeVignetting = system.PenalizeVignetting,
                GlassCatalogs = system.GlassCatalogs
            };

            // Surfaces
            foreach (var s in system.Surfaces)
            {
                var ls = new LhltSurface
                {
                    Index = s.Index,
                    Type = s.Type,
                    Comment = s.Comment,
                    Radius = s.Radius,
                    Thickness = s.Thickness,
                    Material = s.Material,
                    SemiDiameter = s.SemiDiameter,
                    SemiDiameterMode = s.SemiDiameterMode,
                    ClearAperturePercent = s.ClearAperturePercent,
                    Conic = s.Conic,
                    IsStop = s.IsStop,
                    InnerRadius = s.InnerRadius,
                    ObscurationRadius = s.ObscurationRadius,
                    FloatingApertureRadius = s.FloatingApertureRadius,
                    CurvatureVariable = s.CurvatureVariable,
                    ThicknessVariable = s.ThicknessVariable,
                    ConicVariable = s.ConicVariable,
                    HasMarginalRaySolve = s.HasMarginalRaySolve,
                    CurvatureMin = s.CurvatureMin,
                    CurvatureMax = s.CurvatureMax,
                    ThicknessMin = s.ThicknessMin,
                    ThicknessMax = s.ThicknessMax,
                    ConicMin = s.ConicMin,
                    ConicMax = s.ConicMax
                };

                // Only write aspheric data if non-trivial
                if (s.AsphericCoefficients != null && s.AsphericCoefficients.Any(c => c != 0))
                    ls.AsphericCoefficients = (double[])s.AsphericCoefficients.Clone();

                if (s.AsphericVariable != null && s.AsphericVariable.Any(v => v))
                    ls.AsphericVariable = (bool[])s.AsphericVariable.Clone();

                if (s.AsphericMin != null && s.AsphericMin.Any(v => v.HasValue))
                    ls.AsphericMin = (double?[])s.AsphericMin.Clone();

                if (s.AsphericMax != null && s.AsphericMax.Any(v => v.HasValue))
                    ls.AsphericMax = (double?[])s.AsphericMax.Clone();

                file.Surfaces.Add(ls);
            }

            // Wavelengths
            foreach (var w in system.Wavelengths)
            {
                file.Wavelengths.Add(new LhltWavelength
                {
                    Value = w.Value,
                    Weight = w.Weight,
                    IsPrimary = w.IsPrimary
                });
            }

            // Fields
            foreach (var f in system.Fields)
            {
                file.Fields.Add(new LhltField
                {
                    Y = f.Y,
                    Weight = f.Weight,
                    Variable = f.Variable,
                    Min = f.Min,
                    Max = f.Max
                });
            }

            // Pickups
            foreach (var p in system.Pickups)
            {
                file.Pickups.Add(new LhltPickup
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
            if (system.GlassSubstitutions.Count > 0)
            {
                file.GlassSubstitutions = new List<LhltGlassSubstitution>();
                foreach (var gs in system.GlassSubstitutions)
                {
                    file.GlassSubstitutions.Add(new LhltGlassSubstitution
                    {
                        SurfaceIndex = gs.SurfaceIndex,
                        Substitute = gs.Substitute,
                        CatalogName = gs.CatalogName
                    });
                }
            }

            // Merit function
            if (meritFunction != null && meritFunction.Operands.Count > 0)
            {
                file.MeritFunction = new LhltMeritFunction();
                foreach (var op in meritFunction.Operands)
                {
                    file.MeritFunction.Operands.Add(new LhltOperand
                    {
                        Type = op.Type,
                        Target = op.Target,
                        Weight = op.Weight,
                        Minimum = op.Minimum,
                        Maximum = op.Maximum,
                        OpCode = op.OpCode,
                        ConfigurationNo = op.ConfigurationNo,
                        SurfaceIndex = op.SurfaceIndex,
                        WaveIndex = op.WaveIndex,
                        Hx = op.Hx,
                        Hy = op.Hy,
                        Px = op.Px,
                        Py = op.Py,
                        Rings = op.Rings,
                        Arms = op.Arms,
                        GridSize = op.GridSize,
                        OperandNo = op.OperandNo,
                        OperandNo2 = op.OperandNo2,
                        Factor = op.Factor,
                        Surface1 = op.Surface1,
                        Surface2 = op.Surface2
                    });
                }
            }

            // Configuration editor
            if (configEditor != null && configEditor.OperandCount > 0)
            {
                var ce = new LhltConfigurationEditor
                {
                    ActiveConfiguration = configEditor.ActiveConfiguration
                };

                for (int i = 0; i < configEditor.OperandCount; i++)
                {
                    var op = configEditor.Operands[i];
                    ce.Operands.Add(new LhltConfigOperand
                    {
                        Type = op.Type,
                        SurfaceIndex = op.SurfaceIndex,
                        AsphericTermIndex = op.AsphericTermIndex
                    });
                }

                for (int c = 0; c < configEditor.ConfigurationCount; c++)
                {
                    var cv = new LhltConfigValues
                    {
                        Values = new double[configEditor.OperandCount],
                        GlassValues = new string?[configEditor.OperandCount]
                    };

                    bool hasVariables = false;
                    var varFlags = new bool[configEditor.OperandCount];
                    var minVals = new double?[configEditor.OperandCount];
                    var maxVals = new double?[configEditor.OperandCount];

                    for (int i = 0; i < configEditor.OperandCount; i++)
                    {
                        cv.Values[i] = configEditor.GetValue(c, i);
                        cv.GlassValues[i] = configEditor.GetGlass(c, i);
                        varFlags[i] = configEditor.IsVariable(c, i);
                        minVals[i] = configEditor.GetMin(c, i);
                        maxVals[i] = configEditor.GetMax(c, i);
                        if (varFlags[i]) hasVariables = true;
                    }

                    if (hasVariables)
                    {
                        cv.VariableFlags = varFlags;
                        cv.MinValues = minVals;
                        cv.MaxValues = maxVals;
                    }

                    ce.Configurations.Add(cv);
                }

                file.ConfigurationEditor = ce;
            }

            return file;
        }
    }
}
