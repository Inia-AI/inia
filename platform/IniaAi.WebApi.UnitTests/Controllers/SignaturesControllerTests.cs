using IniaAi.Application;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Requests.Signatures;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ContractsEnums = IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.UnitTests.Controllers;

public sealed class SignaturesControllerTests
{
    private const ContractsEnums.SignatureVoteKind UNKNOWN_VOTE_KIND = (ContractsEnums.SignatureVoteKind)99;

    [Fact]
    public async Task CastVote_WithAnUnknownVoteKind_IsRefusedAsInvalid()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddMvcCore();
        _ = services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider();

        RequestContext requestContext = new() { OrganizationId = "org-1", UserId = "user-1" };
        using IServiceScope scope = provider.CreateScope();
        SignaturesController controller = new(requestContext, scope.ServiceProvider.GetRequiredService<ISender>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider } },
        };

        ActionResult result = await controller.CastVote("workflow-type", "pending-1", new CastSignatureVoteRequest(UNKNOWN_VOTE_KIND, null, null));

        ObjectResult problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        ValidationProblemDetails details = Assert.IsType<ValidationProblemDetails>(problem.Value);
        Assert.Contains("VoteKind must be Approve or Reject.", details.Errors.SelectMany(error => error.Value));
    }
}
