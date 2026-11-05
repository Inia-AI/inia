using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using IniaAi.Application.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.Common.Dispatching;

internal sealed class ValidatingDispatcher<TRequest, TValue> : RequestDispatcher<ErrorOr<TValue>>
    where TRequest : IRequest<ErrorOr<TValue>>
{
    public override async Task<ErrorOr<TValue>> Send(
        IRequest<ErrorOr<TValue>> request,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        TRequest typed = (TRequest)request;
        IValidator<TRequest>? validator = services.GetService<IValidator<TRequest>>();

        if (validator is not null)
        {
            ValidationResult validationResult = await validator.ValidateAsync(typed, cancellationToken);

            if (!validationResult.IsValid)
            {
                return RequestValidationErrorFactory.FromFailures(validationResult);
            }
        }

        return await services.GetRequiredService<IRequestHandler<TRequest, ErrorOr<TValue>>>().Handle(typed, cancellationToken);
    }
}
