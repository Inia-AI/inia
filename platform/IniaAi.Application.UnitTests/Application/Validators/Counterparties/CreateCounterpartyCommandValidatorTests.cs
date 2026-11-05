using FluentValidation.TestHelper;
using IniaAi.Application.Commands.Counterparties;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;

namespace IniaAi.Application.UnitTests.Application.Validators.Counterparties;

/// <summary>
/// Unit tests for <see cref="CreateCounterpartyCommandValidator"/>.
/// </summary>
public class CreateCounterpartyCommandValidatorTests
{
    private readonly CreateCounterpartyCommandValidator _validator;

    public CreateCounterpartyCommandValidatorTests()
    {
        _validator = new CreateCounterpartyCommandValidator();
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_Email_Invalid()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: "not-an-email",
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Have_Error_When_Website_Invalid()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: "notaurl",
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Website);
    }

    [Fact]
    public void Should_Have_Error_When_PhoneNumber_Invalid()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: "InvalidPhone!",
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("W & T Offshore, Inc. (\"WT\")")]
    [InlineData("Genie “Energy” Ltd.")]
    [InlineData("Acme {Corp}")]
    [InlineData("Acme\\Corp")]
    [InlineData("Acme`Corp")]
    [InlineData("Acme|Corp")]
    [InlineData("Acme<Corp>")]
    [InlineData("Acme\0Corp")]
    [InlineData("Acme\nCorp")]
    public void Should_Have_Error_When_Name_Contains_Forbidden_Characters(string name)
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: name,
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_NameVariants_Contains_Forbidden_Characters()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "Alias \"WT\"",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.NameVariants);
    }

    [Fact]
    public void Should_Pass_When_NameVariants_Contains_Pipe_Separator()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "International Business Machines",
            NameVariants: "ibm corp|int.bus.m",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("O'Reilly Automotive, Inc.")]
    [InlineData("McDonald's Corporation")]
    [InlineData("AT&T Inc.")]
    [InlineData("Société Générale S.A.")]
    public void Should_Pass_When_Name_Contains_Legitimate_Punctuation(string name)
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: name,
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_With_Valid_Command()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "Alias, Co.",
            Email: "valid@example.com",
            PhoneNumber: "+123456789",
            Website: "https://example.com",
            Address: "123 Street",
            LastFinancialAnalysisResult: "Good"
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_LastFinancialAnalysisResult_At_Max_Length()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: new string('a', CounterpartyFieldsValidator.LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH)
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_LastFinancialAnalysisResult_Exceeds_Max_Length()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: new string('a', CounterpartyFieldsValidator.LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH + 1)
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.LastFinancialAnalysisResult);
    }

    [Fact]
    public void Should_Have_Error_When_Property_Value_Exceeds_Max_Length()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null,
            Properties: new Dictionary<string, string>
            {
                ["reportingStyle"] = new string('x', CounterpartyPropertiesRules.VALUE_MAX_LENGTH + 1),
            }
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.StartsWith("Properties"));
    }

    [Fact]
    public void Should_Pass_With_Valid_Properties()
    {
        var command = new CreateCounterpartyCommand(
            Id: null,
            OrganizationId: "Org123",
            ParentId: null,
            CounterpartyTypeIds: ["Type001"],
            Name: "Valid Name",
            NameVariants: "",
            Email: null,
            PhoneNumber: null,
            Website: null,
            Address: null,
            LastFinancialAnalysisResult: null,
            Properties: new Dictionary<string, string>
            {
                ["reportingStyle"] = "{\"currency\":\"JPY\",\"units\":\"millions\"}",
                ["notes"] = "NAV is prior-month close",
            }
        );

        TestValidationResult<CreateCounterpartyCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
