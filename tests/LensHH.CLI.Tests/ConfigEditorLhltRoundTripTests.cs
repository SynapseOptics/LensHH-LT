using System.IO;
using LensHH.Core.Configuration;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.CLI.Tests
{
    /// <summary>
    /// M3.2a-IO: the .lhlt writer/reader round-trip the configuration editor's system
    /// operands (aperture / field / wavelength) and config-pickups (source/scale/offset).
    /// </summary>
    public class ConfigEditorLhltRoundTripTests
    {
        private static OpticalSystem MakeSystem()
        {
            var s = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0), Title = "mce-roundtrip" };
            s.Surfaces.Add(new Surface());
            s.Surfaces.Add(new Surface { Curvature = 0.02, Thickness = 5.0, Material = "BK7" });
            s.Surfaces.Add(new Surface());
            s.Fields.Add(new Field(0.0, 1.0));
            s.Wavelengths.Add(new Wavelength(0.55, 1.0, true));
            return s;
        }

        [Fact]
        public void ConfigEditor_SystemOperands_And_Pickups_RoundTrip()
        {
            var ed = new ConfigurationEditor();
            ed.AddOperand(new ConfigOperand(ConfigOperandType.Curvature, 1));   // op 0 — surface
            ed.AddOperand(new ConfigOperand(ConfigOperandType.Aperture, 0));    // op 1 — system
            ed.SetNumberOfConfigurations(3);
            ed.SetValue(0, 0, 0.02); ed.SetValue(1, 0, 0.05);                    // curvature (configs 0,1)
            ed.SetValue(0, 1, 10.0); ed.SetValue(1, 1, 20.0); ed.SetValue(2, 1, 15.0); // aperture
            ed.SetPickup(2, 0, sourceConfig: 1, scale: 1.2, offset: 0.001);      // cfg2 curv = cfg1×1.2 + .001
            ed.ActiveConfiguration = 1;

            string path = Path.Combine(Path.GetTempPath(), "mce_roundtrip.lhlt");
            LhltWriter.Write(MakeSystem(), path, null, ed);
            var read = LhltReader.Read(path);
            File.Delete(path);

            var ce = read.ConfigEditor;
            Assert.NotNull(ce);
            Assert.Equal(2, ce!.OperandCount);
            Assert.Equal(3, ce.ConfigurationCount);
            Assert.Equal(1, ce.ActiveConfiguration);

            // Operand types incl. the new system operand.
            Assert.Equal(ConfigOperandType.Curvature, ce.Operands[0].Type);
            Assert.Equal(ConfigOperandType.Aperture, ce.Operands[1].Type);

            // Values (incl. per-config aperture).
            Assert.Equal(0.05, ce.GetValue(1, 0), 12);
            Assert.Equal(15.0, ce.GetValue(2, 1), 12);

            // Config-pickup fully restored: source/scale/offset + resolved effective value.
            Assert.True(ce.IsPickup(2, 0));
            Assert.Equal(1, ce.GetPickupSource(2, 0));
            Assert.Equal(1.2, ce.GetPickupScale(2, 0), 12);
            Assert.Equal(0.001, ce.GetPickupOffset(2, 0), 12);
            Assert.Equal(0.05 * 1.2 + 0.001, ce.GetEffectiveValue(2, 0), 12);
        }
    }
}
