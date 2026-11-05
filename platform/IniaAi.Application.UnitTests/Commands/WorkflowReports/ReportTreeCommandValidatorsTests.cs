using IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;
using IniaAi.WebApi.Contracts.Requests.WorkflowReports;

namespace IniaAi.Application.UnitTests.Commands.WorkflowReports;

public class ReportTreeCommandValidatorsTests
{
    private const string ORG = "IniaAi";
    private const string USER = "User123";
    private const string EXEC = "exec-1";
    private const string ELEMENT = "element-1";

    [Fact]
    public void UpdateReportSection_Valid_Passes()
    {
        var command = new UpdateReportSectionCommand(ORG, USER, EXEC, ELEMENT, new UpdateReportSectionRequest());
        Assert.True(new UpdateReportSectionCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("", USER, EXEC, ELEMENT)]
    [InlineData(ORG, "", EXEC, ELEMENT)]
    [InlineData(ORG, USER, "", ELEMENT)]
    [InlineData(ORG, USER, EXEC, "")]
    public void UpdateReportSection_MissingRequiredField_Fails(string org, string user, string exec, string sectionId)
    {
        var command = new UpdateReportSectionCommand(org, user, exec, sectionId, new UpdateReportSectionRequest());
        Assert.False(new UpdateReportSectionCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void UpdateReportParagraph_Valid_Passes()
    {
        var command = new UpdateReportParagraphCommand(ORG, USER, EXEC, ELEMENT, new UpdateReportParagraphRequest());
        Assert.True(new UpdateReportParagraphCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("", USER, EXEC, ELEMENT)]
    [InlineData(ORG, "", EXEC, ELEMENT)]
    [InlineData(ORG, USER, "", ELEMENT)]
    [InlineData(ORG, USER, EXEC, "")]
    public void UpdateReportParagraph_MissingRequiredField_Fails(string org, string user, string exec, string paragraphId)
    {
        var command = new UpdateReportParagraphCommand(org, user, exec, paragraphId, new UpdateReportParagraphRequest());
        Assert.False(new UpdateReportParagraphCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void DeleteReportSection_Valid_Passes()
    {
        var command = new DeleteReportSectionCommand(ORG, USER, EXEC, ELEMENT);
        Assert.True(new DeleteReportSectionCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("", USER, EXEC, ELEMENT)]
    [InlineData(ORG, "", EXEC, ELEMENT)]
    [InlineData(ORG, USER, "", ELEMENT)]
    [InlineData(ORG, USER, EXEC, "")]
    public void DeleteReportSection_MissingRequiredField_Fails(string org, string user, string exec, string sectionId)
    {
        var command = new DeleteReportSectionCommand(org, user, exec, sectionId);
        Assert.False(new DeleteReportSectionCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void DeleteReportParagraph_Valid_Passes()
    {
        var command = new DeleteReportParagraphCommand(ORG, USER, EXEC, ELEMENT);
        Assert.True(new DeleteReportParagraphCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("", USER, EXEC, ELEMENT)]
    [InlineData(ORG, "", EXEC, ELEMENT)]
    [InlineData(ORG, USER, "", ELEMENT)]
    [InlineData(ORG, USER, EXEC, "")]
    public void DeleteReportParagraph_MissingRequiredField_Fails(string org, string user, string exec, string paragraphId)
    {
        var command = new DeleteReportParagraphCommand(org, user, exec, paragraphId);
        Assert.False(new DeleteReportParagraphCommandValidator().Validate(command).IsValid);
    }
}
