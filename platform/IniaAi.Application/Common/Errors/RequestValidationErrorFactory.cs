using ErrorOr;
using FluentValidation.Results;

namespace IniaAi.Application.Errors;

public static class RequestValidationErrorFactory
{
    public static List<Error> FromFailures(ValidationResult validationResult)
    {
        return validationResult.Errors.ConvertAll(error => Error.Validation(
            code: error.PropertyName,
            description: error.ErrorMessage));
    }
}
