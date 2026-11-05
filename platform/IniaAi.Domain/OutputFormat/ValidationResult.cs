namespace IniaAi.Domain.OutputFormat;

public record ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Valid()
    {
        return new ValidationResult(true, null);
    }

    public static ValidationResult Invalid(string error)
    {
        return new ValidationResult(false, error);
    }
}
