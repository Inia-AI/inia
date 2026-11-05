namespace IniaAi.Common.Tests;

public class DateTimeExtensionsTest
{
    [Theory]
    [InlineData(2025, 1, 1, 31)]
    [InlineData(2025, 1, 15, 31)]
    [InlineData(2025, 2, 1, 28)]
    [InlineData(2024, 2, 1, 29)]
    [InlineData(2025, 4, 30, 30)]
    [InlineData(2025, 12, 31, 31)]
    public void ToMonthEnd_ReturnsLastDayOfMonth(int year, int month, int day, int expectedDay)
    {
        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        DateTime result = date.ToMonthEnd();

        Assert.Equal(new DateTime(year, month, expectedDay, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToMonthEnd_ZeroesTimeOfDay()
    {
        var date = new DateTime(2025, 3, 10, 14, 30, 45, DateTimeKind.Utc);

        DateTime result = date.ToMonthEnd();

        Assert.Equal(new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToMonthEnd_PreservesDateTimeKind(DateTimeKind kind)
    {
        var date = new DateTime(2025, 6, 5, 0, 0, 0, kind);

        DateTime result = date.ToMonthEnd();

        Assert.Equal(kind, result.Kind);
    }
}
