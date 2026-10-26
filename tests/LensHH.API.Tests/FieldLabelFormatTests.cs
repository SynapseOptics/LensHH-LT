using System.Linq;
using LensHH.Rendering;
using Xunit;

namespace LensHH.API.Tests
{
    public class FieldLabelFormatTests
    {
        private static string[] Labels(params double[] fields)
        {
            int d = LabelFormat.FieldDigits(fields);
            return fields.Select(f => f.ToString("F" + d, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        [Fact]
        public void TypedWholeFieldsKeepOneDecimal()
        {
            Assert.Equal(new[] { "0.0", "7.0", "10.0", "14.0" }, Labels(0, 7, 10, 14));
        }

        [Fact]
        public void TypedFieldsKeepTheirDigits()
        {
            Assert.Equal(new[] { "0.00", "12.25", "17.50" }, Labels(0, 12.25, 17.5));
        }

        [Fact]
        public void ComputedSmallFieldsAreNotRoundedToOneDecimal()
        {
            // C14 EdgeHD-like angles: these all printed as "0.1 deg" / "0.2 deg" before.
            Assert.Equal(new[] { "0.0000", "0.0733", "0.1464", "0.2193", "0.2918" },
                Labels(0, 0.0732555171413312, 0.14638733647447183, 0.21926782117349763, 0.29176162628522534));
        }

        [Fact]
        public void CloseFieldsGetEnoughDigitsToDiffer()
        {
            var labels = Labels(10, 10.00002, 20);
            Assert.Equal(3, labels.Distinct().Count());
        }
    }
}
