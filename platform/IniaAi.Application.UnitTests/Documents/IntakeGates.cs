using IniaAi.Application.Documents;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Documents;

internal static class IntakeGates
{
    public const int MAX_FILE_SIZE_MB = 100;

    public static DocumentIntakeGate Logging(int maxFileSizeMb = MAX_FILE_SIZE_MB)
    {
        return new DocumentIntakeGate(
            new LoggingIntakeRefusalRecorder(NullLogger<LoggingIntakeRefusalRecorder>.Instance),
            FileFormatsUnderTest.Unlocker,
            FileFormatsUnderTest.ReadableFormats,
            Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = maxFileSizeMb } }));
    }
}
