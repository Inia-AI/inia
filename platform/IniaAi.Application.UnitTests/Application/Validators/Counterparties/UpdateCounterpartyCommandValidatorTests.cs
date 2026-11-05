using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Counterparties;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;

namespace IniaAi.Application.UnitTests.Application.Validators.Counterparties;

public class UpdateCounterpartyCommandValidatorTests
{
    private readonly UpdateCounterpartyCommandValidator _validator;

    public UpdateCounterpartyCommandValidatorTests()
    {
        _validator = new UpdateCounterpartyCommandValidator();
    }

    private static UpdateCounterpartyCommand CreateValidCommand(
        string? id = "cp-001",
        string? organizationId = "Org123",
        List<string>? counterpartyTypeIds = null,
        string name = "Valid Name",
        string nameVariants = "",
        string? email = null,
        string? phoneNumber = null,
        string? website = null,
        string? address = null,
        string? lastFinancialAnalysisResult = null,
        Dictionary<string, string>? properties = null)
    {
        return new(
            Id: id!,
            OrganizationId: organizationId,
            ParentId: null,
            CounterpartyTypeIds: counterpartyTypeIds ?? ["Type001"],
            Name: name,
            NameVariants: nameVariants,
            Email: email,
            PhoneNumber: phoneNumber,
            Website: website,
            Address: address,
            LastFinancialAnalysisResult: lastFinancialAnalysisResult,
            Properties: properties
        );
    }

    [Fact]
    public void Should_Have_Error_When_Property_Value_Exceeds_Max_Length()
    {
        UpdateCounterpartyCommand command = CreateValidCommand(
            properties: new Dictionary<string, string>
            {
                ["reportingStyle"] = new string('x', CounterpartyPropertiesRules.VALUE_MAX_LENGTH + 1),
            });

        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.StartsWith("Properties"));
    }

    [Fact]
    public void Should_Pass_With_Valid_Properties()
    {
        UpdateCounterpartyCommand command = CreateValidCommand(
            properties: new Dictionary<string, string>
            {
                ["reportingStyle"] = "{\"currency\":\"JPY\",\"units\":\"millions\"}",
                ["notes"] = "quarterly filing",
            });

        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_With_Valid_Command()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_Id_Is_Empty()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(id: ""));
        _ = result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Is_Empty()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(organizationId: ""));
        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(name: ""));
        _ = result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_CounterpartyTypeIds_Is_Empty()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(counterpartyTypeIds: []));
        _ = result.ShouldHaveValidationErrorFor(x => x.CounterpartyTypeIds);
    }

    [Fact]
    public void Should_Have_Error_When_CounterpartyTypeIds_Contains_Empty_String()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(counterpartyTypeIds: ["Type001", ""]));
        _ = result.ShouldHaveValidationErrorFor("CounterpartyTypeIds[1]");
    }

    [Fact]
    public void Should_Pass_With_Multiple_CounterpartyTypeIds()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(counterpartyTypeIds: ["Type001", "Type002"]));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_Email_Invalid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(email: "not-an-email"));
        _ = result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Pass_When_Email_Is_Valid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(email: "valid@example.com"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_Website_Invalid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(website: "notaurl"));
        _ = result.ShouldHaveValidationErrorFor(x => x.Website);
    }

    [Fact]
    public void Should_Pass_When_Website_Is_Valid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(website: "https://example.com"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_PhoneNumber_Invalid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(phoneNumber: "InvalidPhone!"));
        _ = result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void Should_Pass_When_PhoneNumber_Is_Valid()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(phoneNumber: "+123456789"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("W & T Offshore, Inc. (\"WT\")")]
    [InlineData("Acme {Corp}")]
    [InlineData("Acme\\Corp")]
    [InlineData("Acme|Corp")]
    [InlineData("Acme\0Corp")]
    public void Should_Have_Error_When_Name_Contains_Forbidden_Characters(string name)
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(name: name));
        _ = result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_NameVariants_Contains_Forbidden_Characters()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(nameVariants: "Alias \"WT\""));
        _ = result.ShouldHaveValidationErrorFor(x => x.NameVariants);
    }

    [Fact]
    public void Should_Pass_When_Name_Contains_Apostrophe()
    {
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(name: "O'Reilly Automotive, Inc."));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_LastFinancialAnalysisResult_At_Max_Length()
    {
        string atMax = new('a', CounterpartyFieldsValidator.LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH);
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(lastFinancialAnalysisResult: atMax));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_LastFinancialAnalysisResult_Exceeds_Max_Length()
    {
        string overMax = new('a', CounterpartyFieldsValidator.LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH + 1);
        TestValidationResult<UpdateCounterpartyCommand> result = _validator.TestValidate(CreateValidCommand(lastFinancialAnalysisResult: overMax));
        _ = result.ShouldHaveValidationErrorFor(x => x.LastFinancialAnalysisResult);
    }
}
