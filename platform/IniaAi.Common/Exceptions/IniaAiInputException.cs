namespace IniaAi.Common.Exceptions;
/// <summary>
/// INIA.AI exception related to file or text inputs
/// </summary>
public class IniaAiInputException : Exception
{
    /// <summary>
    /// Create a new exception with the specified message
    /// </summary>
    /// <param name="Message">Exception message</param>
    public IniaAiInputException(string Message) : base(Message)
    {

    }
}
