namespace IniaAi.Application.Documents;

public enum IntakeOutcome
{
    /// <summary>The bytes may be stored; <see cref="IntakeScreening.Bytes"/> is the copy to keep.</summary>
    Admitted,

    /// <summary>The inspector refused the bytes and the refusal record is written; nothing is stored.</summary>
    Refused,

    /// <summary>The file is encrypted and no password opens it; the caller asks for one or skips the file.</summary>
    PasswordRequired,

    /// <summary>The supplied password does not open the file.</summary>
    PasswordIncorrect,

    /// <summary>The file is protected in a way no password removes.</summary>
    UnsupportedProtection,
}
