using CsvHelper;
using IniaAi.Common.Text;
using System.Globalization;

namespace IniaAi.Common.Tests.Text;

public class SafeCsvTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public void AFieldThatStartsLikeAFormula_IsWrittenAsText(string field)
    {
        Assert.Equal("'" + field, RoundTrip(field));
    }

    [Theory]
    [InlineData("-12.5")]
    [InlineData("+3")]
    public void ANumberWithASign_StaysANumber(string field)
    {
        Assert.Equal(field, RoundTrip(field));
    }

    private static string RoundTrip(string field)
    {
        using var text = new StringWriter();
        using (CsvWriter writer = SafeCsv.CreateWriter(text, CultureInfo.InvariantCulture))
        {
            writer.WriteField(field);
            writer.NextRecord();
        }

        using var reader = new CsvReader(new StringReader(text.ToString()), CultureInfo.InvariantCulture);
        _ = reader.Read();
        return reader.GetField(0)!;
    }
}
