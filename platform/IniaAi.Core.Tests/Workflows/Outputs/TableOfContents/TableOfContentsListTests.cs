using IniaAi.Core.Workflows.Outputs.TableOfContents;

namespace IniaAi.Core.Tests.Workflows.Outputs.TableOfContents;

public class TableOfContentsListTests
{
    [Fact]
    public void ToJson_WhenCalled_ReturnsJson()
    {
        // Arrange
        var tableOfContents = new TableOfContentsList
        {
            Sections =
            [
                new TableOfContentsEntry
                {
                    Title = "Title",
                    StartPage = 1,
                    EndPage = 2
                }
            ]
        };
        string expectedResult =
        """
        {
          "Sections": [
            {
              "Title": "Title",
              "StartPage": 1,
              "EndPage": 2
            }
          ]
        }
        """;

        // Act
        string json = tableOfContents.ToJson(pretty: true);

        // Assert
        Assert.NotNull(json);
        Assert.Equal(expectedResult, json, ignoreCase: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FromJson_WhenCalled_ReturnsTableOfContents()
    {
        // Arrange
        string json =
        """
        {
          "Sections": [
            {
              "Title": "Title",
              "StartPage": 1,
              "EndPage": 2
            }
          ]
        }
        """;
        var expectedResult = new TableOfContentsList
        {
            Sections =
            [
                new TableOfContentsEntry
                {
                    Title = "Title",
                    StartPage = 1,
                    EndPage = 2
                }
            ]
        };

        // Act
        var tableOfContents = TableOfContentsList.FromJson(json);

        // Assert
        Assert.NotNull(tableOfContents);
        Assert.Equal(expectedResult.Sections.Count, tableOfContents.Sections.Count);
        Assert.Equal(expectedResult.Sections[0].Title, tableOfContents.Sections[0].Title);
        Assert.Equal(expectedResult.Sections[0].StartPage, tableOfContents.Sections[0].StartPage);
        Assert.Equal(expectedResult.Sections[0].EndPage, tableOfContents.Sections[0].EndPage);
    }

    [Fact]
    public void FromJson_WhenCalledWithInvalidJson_ReturnsNull()
    {
        // Arrange
        string json = "invalid json";

        // Act
        var tableOfContents = TableOfContentsList.FromJson(json);

        // Assert
        Assert.Null(tableOfContents);
    }
}
