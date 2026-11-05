using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.HttpHandling;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

/// <summary>
/// Unit tests for <see cref="BedrockResiliencePipelineFactory"/>.
/// </summary>
public class BedrockResiliencePipelineFactoryTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger _logger;

    public BedrockResiliencePipelineFactoryTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = TestsLogger.CreateLogger<BedrockResiliencePipelineFactoryTests>(_output);
    }

    #region Pipeline Creation Tests

    [Fact]
    public void Create_WithDefaultPolicy_ReturnsPipeline()
    {
        // Arrange
        var policy = new RetryPolicy();

        // Act
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        // Assert
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Create_WithCustomRetryAttempts_ReturnsPipeline()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRateLimitRetries = 3,
            MaxUnavailableRetries = 5
        };

        // Act
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        // Assert
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Create_WithCustomDelays_ReturnsPipeline()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            DefaultUnavailableRetryDelaySeconds = 2,
            MaxRetryDelaySeconds = 30
        };

        // Act
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        // Assert
        Assert.NotNull(pipeline);
    }

    #endregion

    #region Retry Behavior Tests - Transient Exceptions

    [Fact]
    public async Task ExecuteAsync_ThrottlingException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRateLimitRetries = 3,
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0, // No delay for faster tests
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 3 ? throw new ThrottlingException("Rate limited") : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(3, callCount); // Called 3 times (2 failures + 1 success)
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceUnavailableException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2 ? throw new ServiceUnavailableException("Service unavailable") : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_InternalServerException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2 ? throw new InternalServerException("Internal server error") : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_ModelTimeoutException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2 ? throw new ModelTimeoutException("Model timeout") : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2 ? throw new HttpRequestException("Network error") : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutException_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2 ? throw new TaskCanceledException("Timeout", new TimeoutException("Request timed out")) : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    #endregion

    #region Non-Retryable Exception Tests

    [Fact]
    public async Task ExecuteAsync_AccessDeniedException_DoesNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<AccessDeniedException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new AccessDeniedException("Access denied");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        Assert.Equal(1, callCount); // Should only be called once, no retry
    }

    [Fact]
    public async Task ExecuteAsync_ResourceNotFoundException_DoesNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new ResourceNotFoundException("Model not found");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationException_DoesNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new ValidationException("Invalid request");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        Assert.Equal(1, callCount);
    }

    #endregion

    #region Max Retry Exhaustion Tests

    [Fact]
    public async Task ExecuteAsync_ExceedsMaxRetries_ThrowsException()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRateLimitRetries = 2,
            MaxUnavailableRetries = 2,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<ThrottlingException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new ThrottlingException("Rate limited");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        // Should be called maxRetries + 1 times (initial call + retries)
        Assert.Equal(3, callCount); // 1 initial + 2 retries
    }

    [Fact]
    public async Task ExecuteAsync_ServiceUnavailable_ExceedsMaxRetries_ThrowsException()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRateLimitRetries = 2,  // Factory uses Max() of both retry types
            MaxUnavailableRetries = 2,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<ServiceUnavailableException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new ServiceUnavailableException("Service unavailable");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        Assert.Equal(3, callCount); // 1 initial + 2 retries
    }

    #endregion

    #region Transient Status Code Tests

    [Fact]
    public async Task ExecuteAsync_BedrockExceptionWith503_Retries()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new AmazonBedrockRuntimeException("Service unavailable")
                {
                    StatusCode = HttpStatusCode.ServiceUnavailable
                }
                : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_BedrockExceptionWith429_Retries()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new AmazonBedrockRuntimeException("Too many requests")
                {
                    StatusCode = HttpStatusCode.TooManyRequests
                }
                : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_BedrockExceptionWith500_Retries()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new AmazonBedrockRuntimeException("Internal server error")
                {
                    StatusCode = HttpStatusCode.InternalServerError
                }
                : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task ExecuteAsync_BedrockExceptionWith400_DoesNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAsync<AmazonBedrockRuntimeException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                throw new AmazonBedrockRuntimeException("Bad request")
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, CancellationToken.None);
        });

        Assert.Equal(1, callCount); // Should not retry on 400
    }

    #endregion

    #region Signature Expiration Test

    [Fact]
    public async Task ExecuteAsync_SignatureExpiredException_Retries()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 3,
            DefaultUnavailableRetryDelaySeconds = 0,
            MaxRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        int callCount = 0;
        InvokeModelResponse expectedResponse = CreateMockResponse();

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return callCount < 2
                ? throw new AmazonBedrockRuntimeException("Signature expired: 20251215T083954Z is now earlier than 20251215T084108Z")
                : expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(expectedResponse, result);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxUnavailableRetries = 5,
            DefaultUnavailableRetryDelaySeconds = 1
        };
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        using var cts = new CancellationTokenSource();
        int callCount = 0;

        // Act & Assert
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await pipeline.ExecuteAsync(async ct =>
            {
                callCount++;
                if (callCount == 1)
                {
                    cts.Cancel(); // Cancel after first attempt
                }

                ct.ThrowIfCancellationRequested();
                throw new ThrottlingException("Rate limited");
#pragma warning disable CS0162 // Unreachable code - needed for type inference
                return CreateMockResponse();
#pragma warning restore CS0162
            }, cts.Token);
        });
    }

    #endregion

    #region Success Path Tests

    [Fact]
    public async Task ExecuteAsync_NoExceptions_ReturnsResponseDirectly()
    {
        // Arrange
        var policy = new RetryPolicy();
        ResiliencePipeline<InvokeModelResponse> pipeline = BedrockResiliencePipelineFactory.Create(_logger, policy);

        InvokeModelResponse expectedResponse = CreateMockResponse();
        int callCount = 0;

        // Act
        InvokeModelResponse result = await pipeline.ExecuteAsync(async ct =>
        {
            callCount++;
            return expectedResponse;
        }, CancellationToken.None);

        // Assert
        Assert.Equal(1, callCount);
        Assert.Equal(expectedResponse, result);
    }

    #endregion

    #region Helper Methods

    private static InvokeModelResponse CreateMockResponse()
    {
        return new InvokeModelResponse
        {
            HttpStatusCode = HttpStatusCode.OK,
            ContentType = "application/json"
        };
    }

    #endregion
}
