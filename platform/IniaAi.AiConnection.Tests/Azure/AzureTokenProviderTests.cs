using IniaAi.AiConnection.Azure;

namespace IniaAi.AiConnection.Tests.Azure;

public class AzureTokenProviderTests
{
    [Fact]
    public async Task NullAzureTokenProvider_GetTokenAsync_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = new NullAzureTokenProvider();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NullAzureTokenProvider_GetTokenAsync_WithCancellationToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = new NullAzureTokenProvider();
        using var cts = new CancellationTokenSource();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(cts.Token));
    }
}
