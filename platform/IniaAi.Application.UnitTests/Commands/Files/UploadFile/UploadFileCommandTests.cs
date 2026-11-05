using IniaAi.Application.Commands.Files.UploadFile;
using Microsoft.AspNetCore.Http;

namespace IniaAi.Application.UnitTests.Commands.Files.UploadFile;

public class UploadFileCommandTests
{
    private const string SECRET_PASSWORD = "secret-pw-do-not-log";

    [Fact]
    public void ToString_DoesNotLeakFilePassword()
    {
        var file = new FormFile(new MemoryStream(new byte[16]), 0, 16, "file", "locked.pdf");

        var command = new UploadFileCommand(file, "Org", "User", null, SECRET_PASSWORD);

        Assert.Equal(SECRET_PASSWORD, command.FilePassword);
        Assert.DoesNotContain(SECRET_PASSWORD, command.ToString());
    }
}
