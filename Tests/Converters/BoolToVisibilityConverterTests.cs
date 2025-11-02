using System.Windows;
using TestCaseEditorApp.Converters;
using Xunit;

namespace TestCaseEditorApp.Tests.Converters
{
    public class BoolToVisibilityConverterTests
    {
        [Theory]
        [InlineData(true, false, Visibility.Visible)]
        [InlineData(false, false, Visibility.Collapsed)]
        [InlineData(true, true, Visibility.Collapsed)]  // inverted
        [InlineData(false, true, Visibility.Visible)]    // inverted
        public void Convert_ReturnsExpectedVisibility(bool input, bool invert, Visibility expected)
        {
            var conv = new BoolToVisibilityConverter { Invert = invert };
            var result = conv.Convert(input, typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsType<Visibility>(result);
            Assert.Equal(expected, (Visibility)result);
        }
    }
}