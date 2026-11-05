namespace IniaAi.FileFormats.Email;

public sealed class EmailConversionException : Exception
{
    public const string DAMAGED_EMAIL = "The email is damaged and cannot be opened. Save it again from the mail program, then upload the new copy.";

    public EmailConversionException(string message)
        : base(message)
    {
    }

    public EmailConversionException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
