using System.Windows;
using TestCaseEditorApp.Converters;
using Xunit;

namespace TestCaseEditorApp.Tests.Converters
{
    public class BadgeToVisibilityConverterTests
    {
        [Theory]
        [InlineData(null, Visibility.Collapsed)]
        [InlineData("0", Visibility.Collapsed)]
        [InlineData("", Visibility.Collapsed)]
        [InlineData("  ", Visibility.Collapsed)]
        [InlineData("5", Visibility.Visible)]
        [InlineData(0, Visibility.Collapsed)]
        [InlineData(3, Visibility.Visible)]
        public void Convert_BadgeValues_ReturnsExpected(object input, Visibility expected)
        {
            var conv = new BadgeToVisibilityConverter();
            var result = conv.Convert(input, typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected, (Visibility)result);
        }
    }
}