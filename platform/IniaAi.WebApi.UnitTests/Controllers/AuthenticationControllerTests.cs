using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Controllers;

/// <summary>
/// OpenIddict validates every token request before the action runs, and the form model only feeds
/// Swagger. Model validation on it would refuse a valid grant with a 400 before OpenIddict sees it.
/// </summary>
public class AuthenticationControllerTests
{
    [Fact]
    public void TheTokenEndpointNeverValidatesItsFormModel()
    {
        ParameterInfo formModel = typeof(AuthenticationController)
            .GetMethod(nameof(AuthenticationController.Exchange))!
            .GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(TokenRequest));

        Assert.NotNull(formModel.ParameterType.GetCustomAttribute<ValidateNeverAttribute>());
    }
}
