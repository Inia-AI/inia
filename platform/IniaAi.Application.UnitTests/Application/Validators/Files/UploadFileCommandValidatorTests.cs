using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Files.UploadFile;
using Microsoft.AspNetCore.Http;

namespace IniaAi.Application.UnitTests.Application.Validators.Files;

/// <summary>
/// The validator checks the caller, the presence of a file and the password length. What the
/// file is, whether it is empty and whether it fits the size limit is the intake gate's call
/// inside the handler, so those rejections write their screening record.
/// </summary>
public class UploadFileCommandValidatorTests
{
    private readonly UploadFileCommandValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_File_Is_Null()
    {
        var command = new UploadFileCommand(File: null!, OrganizationId: "Org", UserId: "User");
        TestValidationResult<UploadFileCommand> result = _validator.TestValidate(command);
        _ = result.ShouldHaveValidationErrorFor(x => x.File);
    }

    [Theory]
    [InlineData("", "User")]
    [InlineData("Org", "")]
    public void Should_Have_Error_When_Caller_Is_Missing(string organizationId, string userId)
    {
        var validFile = new FormFile(new MemoryStream(new byte[1024]), 0, 1024, "file", "file.pdf");
        var command = new UploadFileCommand(validFile, organizationId, userId);
        TestValidationResult<UploadFileCommand> result = _validator.TestValidate(command);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("file.pdf")]
    [InlineData("file.exe")]
    [InlineData("file")]
    [InlineData("empty.pdf")]
    public void Should_Not_Judge_The_File_By_Its_Name_Or_Size(string fileName)
    {
        var file = new FormFile(new MemoryStream(new byte[15 * 1024 * 1024]), 0, fileName == "empty.pdf" ? 0 : 15 * 1024 * 1024, "file", fileName);
        var command = new UploadFileCommand(file, "Org", "User");
        TestValidationResult<UploadFileCommand> result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_FilePassword_Too_Long()
    {
        var validFile = new FormFile(new MemoryStream(new byte[1024]), 0, 1024, "file", "file.pdf");
        var command = new UploadFileCommand(validFile, "Org", "User", FilePassword: new string('p', 257));
        TestValidationResult<UploadFileCommand> result = _validator.TestValidate(command);
        _ = result.ShouldHaveValidationErrorFor(x => x.FilePassword);
    }

    [Fact]
    public void Should_Not_Have_Error_When_FilePassword_Provided()
    {
        var validFile = new FormFile(new MemoryStream(new byte[1024]), 0, 1024, "file", "file.pdf");
        var command = new UploadFileCommand(validFile, "Org", "User", FilePassword: "correct-horse");
        TestValidationResult<UploadFileCommand> result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
