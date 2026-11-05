using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Common.Dispatching;

public sealed class SenderTests : IDisposable
{
    private const string BLANK_NAME_MESSAGE = "Name is required.";

    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly ISender _sender;

    public SenderTests()
    {
        ServiceCollection services = new();
        _ = services.AddSingleton<HandledRequests>();
        _ = services.AddScoped<RequestScope>();
        _ = services.AddSingleton<IValidator<Greet>, GreetValidator>();
        _ = services.AddSingleton<IValidator<MeasureName>, MeasureNameValidator>();
        _ = services.AddRequestDispatching([typeof(GreetHandler), typeof(GreetThroughInnerHandler), typeof(MeasureNameHandler)]);

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _services.CreateScope();
        _sender = _scope.ServiceProvider.GetRequiredService<ISender>();
    }

    private HandledRequests Handled => _services.GetRequiredService<HandledRequests>();

    [Fact]
    public async Task Send_ValidatorRejectsRequest_ReturnsValidationErrorsAndSkipsHandler()
    {
        ErrorOr<string> result = await _sender.Send(new Greet(""), TestContext.Current.CancellationToken);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(Greet.Name), error.Code);
        Assert.Equal(BLANK_NAME_MESSAGE, error.Description);
        Assert.Empty(Handled.Names);
    }

    [Fact]
    public async Task Send_ValidRequest_ReturnsTheHandlerResponse()
    {
        ErrorOr<string> result = await _sender.Send(new Greet("Ada"), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Hello, Ada", result.Value);
        Assert.Equal(["Ada"], Handled.Names);
    }

    [Fact]
    public async Task Send_FromInsideHandler_RunsInnerRequestValidator()
    {
        ErrorOr<string> result = await _sender.Send(new GreetThroughInner("outer", InnerName: ""), TestContext.Current.CancellationToken);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(Greet.Name), error.Code);
        Assert.Equal(BLANK_NAME_MESSAGE, error.Description);
        Assert.Equal(["outer"], Handled.Names);
    }

    [Fact]
    public async Task Send_FromInsideHandler_ResolvesTheInnerHandlerInTheOuterScope()
    {
        ErrorOr<string> result = await _sender.Send(new GreetThroughInner("outer", InnerName: "inner"), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Guid outerScope = _scope.ServiceProvider.GetRequiredService<RequestScope>().Id;
        Assert.Equal([outerScope, outerScope], Handled.ScopeIds);
    }

    [Fact]
    public async Task Send_ResponseIsNotErrorOr_RunsTheHandlerWithoutValidating()
    {
        int length = await _sender.Send(new MeasureName(""), TestContext.Current.CancellationToken);

        Assert.Equal(0, length);
        Assert.Equal([""], Handled.Names);
    }

    [Fact]
    public async Task Send_RequestWithNoHandler_ThrowsNamingTheRequest()
    {
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sender.Send(new Unhandled(), TestContext.Current.CancellationToken));

        Assert.Contains(typeof(Unhandled).FullName!, exception.Message);
    }

    [Fact]
    public void AddRequestDispatching_TwoHandlersForOneRequest_ThrowsNamingBoth()
    {
        ServiceCollection services = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddRequestDispatching([typeof(FirstDuplicatedHandler), typeof(SecondDuplicatedHandler)]));

        Assert.Contains(nameof(FirstDuplicatedHandler), exception.Message);
        Assert.Contains(nameof(SecondDuplicatedHandler), exception.Message);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    public sealed class HandledRequests
    {
        public List<string> Names { get; } = [];

        public List<Guid> ScopeIds { get; } = [];
    }

    public sealed class RequestScope
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed record Greet(string Name) : IRequest<ErrorOr<string>>;

    public sealed class GreetValidator : AbstractValidator<Greet>
    {
        public GreetValidator()
        {
            _ = RuleFor(request => request.Name).NotEmpty().WithMessage(BLANK_NAME_MESSAGE);
        }
    }

    public sealed class GreetHandler(HandledRequests handled, RequestScope scope) : IRequestHandler<Greet, ErrorOr<string>>
    {
        private readonly HandledRequests _handled = handled;
        private readonly RequestScope _scope = scope;

        public Task<ErrorOr<string>> Handle(Greet request, CancellationToken cancellationToken)
        {
            _handled.Names.Add(request.Name);
            _handled.ScopeIds.Add(_scope.Id);

            return Task.FromResult<ErrorOr<string>>($"Hello, {request.Name}");
        }
    }

    public sealed record GreetThroughInner(string Name, string InnerName) : IRequest<ErrorOr<string>>;

    public sealed class GreetThroughInnerHandler(HandledRequests handled, RequestScope scope, ISender sender)
        : IRequestHandler<GreetThroughInner, ErrorOr<string>>
    {
        private readonly HandledRequests _handled = handled;
        private readonly RequestScope _scope = scope;
        private readonly ISender _sender = sender;

        public Task<ErrorOr<string>> Handle(GreetThroughInner request, CancellationToken cancellationToken)
        {
            _handled.Names.Add(request.Name);
            _handled.ScopeIds.Add(_scope.Id);

            return _sender.Send(new Greet(request.InnerName), cancellationToken);
        }
    }

    public sealed record MeasureName(string Name) : IRequest<int>;

    public sealed class MeasureNameValidator : AbstractValidator<MeasureName>
    {
        public MeasureNameValidator()
        {
            _ = RuleFor(request => request.Name).NotEmpty();
        }
    }

    public sealed class MeasureNameHandler(HandledRequests handled) : IRequestHandler<MeasureName, int>
    {
        private readonly HandledRequests _handled = handled;

        public Task<int> Handle(MeasureName request, CancellationToken cancellationToken)
        {
            _handled.Names.Add(request.Name);

            return Task.FromResult(request.Name.Length);
        }
    }

    public sealed record Unhandled : IRequest<ErrorOr<string>>;

    public sealed record Duplicated : IRequest<ErrorOr<string>>;

    public sealed class FirstDuplicatedHandler : IRequestHandler<Duplicated, ErrorOr<string>>
    {
        public Task<ErrorOr<string>> Handle(Duplicated request, CancellationToken cancellationToken)
        {
            return Task.FromResult<ErrorOr<string>>("first");
        }
    }

    public sealed class SecondDuplicatedHandler : IRequestHandler<Duplicated, ErrorOr<string>>
    {
        public Task<ErrorOr<string>> Handle(Duplicated request, CancellationToken cancellationToken)
        {
            return Task.FromResult<ErrorOr<string>>("second");
        }
    }
}
