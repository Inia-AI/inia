using System.Globalization;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class DurationFormatterTests
{
    public DurationFormatterTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0, "0ms")]
    [InlineData(500, "500ms")]
    [InlineData(999, "999ms")]
    [InlineData(1000, "1.0s")]
    [InlineData(1500, "1.5s")]
    [InlineData(59000, "59.0s")]
    [InlineData(60000, "1.0m")]
    [InlineData(90000, "1.5m")]
    public void Format_PicksUnitAndPrecisionByMagnitude(double milliseconds, string expected)
    {
        string result = DurationFormatter.Format(TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(expected, result);
    }
}
