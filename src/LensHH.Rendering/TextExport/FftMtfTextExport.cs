using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class FftMtfTextExport
    {
        /// <summary>
        /// Export FFT MTF with direction-dependent diffraction limit columns.
        /// cutoffT/cutoffS account for canonical pupil scaling at off-axis fields.
        /// If cutoffT=0, DL columns are omitted.
        /// </summary>
        public static string Export(MtfResult result, string title = "",
            double cutoffT = 0, double cutoffS = 0,
            double fieldValue = double.NaN, double wavelengthUm = double.NaN, string fieldUnit = "deg",
            bool isAfocal = false)
        {
            string freqUnit = isAfocal ? "cy/arcmin" : "cy/mm";
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Field\t{0}", result.FieldIndex + 1));
            if (!double.IsNaN(fieldValue))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Field Value ({0})\t{1:F4}", fieldUnit, fieldValue));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Wavelength\t{0}", result.WavelengthIndex + 1));
            if (!double.IsNaN(wavelengthUm))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Wavelength (µm)\t{0:F6}", wavelengthUm));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Frequency ({0})\t{1:F2}", freqUnit, result.MaxFrequency));

            bool hasDL = cutoffT > 0 && cutoffS > 0;
            if (hasDL)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Cutoff T ({0})\t{1:F2}", freqUnit, cutoffT));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Cutoff S ({0})\t{1:F2}", freqUnit, cutoffS));
            }
            sb.AppendLine();

            if (hasDL)
                sb.AppendLine($"Spatial Frequency ({freqUnit})\tTangential\tSagittal\tDL Tangential\tDL Sagittal");
            else
                sb.AppendLine($"Spatial Frequency ({freqUnit})\tTangential\tSagittal");

            bool hasPrecomputedDL = result.DiffractionLimit != null
                && result.DiffractionLimit.Count == result.Points.Count;

            for (int i = 0; i < result.Points.Count; i++)
            {
                var pt = result.Points[i];
                if (hasDL)
                {
                    double dlT, dlS;
                    if (hasPrecomputedDL)
                    {
                        dlT = result.DiffractionLimit[i].Tangential;
                        dlS = result.DiffractionLimit[i].Sagittal;
                    }
                    else
                    {
                        dlT = DiffractionLimit(pt.SpatialFrequency, cutoffT);
                        dlS = DiffractionLimit(pt.SpatialFrequency, cutoffS);
                    }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F4}\t{1:F6}\t{2:F6}\t{3:F6}\t{4:F6}",
                        pt.SpatialFrequency, pt.Tangential, pt.Sagittal, dlT, dlS));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F4}\t{1:F6}\t{2:F6}",
                        pt.SpatialFrequency, pt.Tangential, pt.Sagittal));
                }
            }

            return sb.ToString();
        }

        private static double DiffractionLimit(double freq, double cutoff)
        {
            if (cutoff <= 0 || freq >= cutoff) return 0;
            if (freq <= 0) return 1;
            double rho = freq / cutoff;
            return (2.0 / Math.PI) * (Math.Acos(rho) - rho * Math.Sqrt(1.0 - rho * rho));
        }
    }
}
