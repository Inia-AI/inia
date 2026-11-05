using IniaAi.Common.Resources;
using System.Reflection;

namespace IniaAi.Common.Tests;

public class EmbeddedResourceManagerTests
{
    private readonly Assembly _assembly = Assembly.GetAssembly(typeof(EmbeddedResourceManagerTests))!;

    [Fact]
    public async Task ReadResourceAsync_WithValidResourceName_ReturnsContentAsync()
    {
        // Arrange
        string resourceName = $"Test.EmbeddedResource.txt";
        string expectedContent = "This is a test content\n";

        // Act
        string content = await _assembly.ReadResourceAsync(resourceName, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(expectedContent.Replace("\r\n", "\n"), content.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ReadResourceAsync_WithInvalidResourceName_ThrowsInvalidOperationExceptionAsync()
    {
        // Arrange
        string resourceName = "Test.EmbeddedResource.Invalid.txt";

        // Act
        async Task Act()
        {
            _ = await _assembly.ReadResourceAsync(resourceName);
        }

        // Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(Act);
    }

    [Fact]
    public void GetResourcesList_WithValidAssemblyName_ReturnsResourcesList()
    {
        // Arrange & Act
        List<string> resources = _assembly.GetResourcesList();

        // Assert
        Assert.NotNull(resources);
        Assert.NotEmpty(resources);
        Assert.Contains("IniaAi.Common.Tests.Test.EmbeddedResource.txt", resources);
    }

    [Fact]
    public void GetResourcesList_WithValidSearchPpattern_ReturnsResourcesList()
    {
        // Arrange
        string searchPattern = "EmbeddedResource";

        // Act
        List<string> resources = _assembly.GetResourcesList(searchPattern);

        // Assert
        Assert.NotNull(resources);
        Assert.NotEmpty(resources);
        Assert.Contains("IniaAi.Common.Tests.Test.EmbeddedResource.txt", resources);
    }

    [Fact]
    public void GetResourcesList_WithInvalidSearchPpattern_ReturnsEmptyList()
    {
        // Arrange
        string searchPattern = "Invalid";

        // Act
        List<string> resources = _assembly.GetResourcesList(searchPattern);

        // Assert
        Assert.NotNull(resources);
        Assert.Empty(resources);
    }
}
