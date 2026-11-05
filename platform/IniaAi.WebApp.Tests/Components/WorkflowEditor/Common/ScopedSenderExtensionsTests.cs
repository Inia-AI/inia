using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class ScopedSenderExtensionsTests
{
    [Fact]
    public async Task SendScopedAsync_ValidatesAndHandlesTheRequest()
    {
        ServiceCollection services = new();
        _ = services.AddRequestDispatching(typeof(ScopedSenderExtensionsTests).Assembly);
        _ = services.AddSingleton<IValidator<EchoQuery>>(new EchoQueryValidator());
        using ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

        ErrorOr<string> refused = await scopes.SendScopedAsync(new EchoQuery(string.Empty), TestContext.Current.CancellationToken);
        ErrorOr<string> answered = await scopes.SendScopedAsync(new EchoQuery("hello"), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Validation, refused.FirstError.Type);
        Assert.Equal("hello", answered.Value);
    }

    public sealed record EchoQuery(string Text) : IRequest<ErrorOr<string>>;

    public sealed class EchoQueryHandler : IRequestHandler<EchoQuery, ErrorOr<string>>
    {
        public Task<ErrorOr<string>> Handle(EchoQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult<ErrorOr<string>>(request.Text);
        }
    }

    public sealed class EchoQueryValidator : AbstractValidator<EchoQuery>
    {
        public EchoQueryValidator()
        {
            _ = RuleFor(query => query.Text).NotEmpty();
        }
    }
}
