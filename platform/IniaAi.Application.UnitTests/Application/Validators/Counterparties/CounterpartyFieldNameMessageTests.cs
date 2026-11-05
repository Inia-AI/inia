using IniaAi.Application.Commands.Counterparties.ImportCounterparties;
using IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.UnitTests.Application.Validators.Counterparties;

/// <summary>
/// A default validation message names the field. A field whose property says counterparty reads
/// as company, written out here rather than built from the term constant so a wrong term fails.
/// </summary>
public class CounterpartyFieldNameMessageTests
{
    [Fact]
    public void WorkflowTypeWithoutCompanyTypes_SaysCompanyTypes()
    {
        var command = new CreateWorkflowTypeCommand("org-test", "user-test", "qa-se01", "QA SE01", null, null, null!);

        string message = Assert.Single(new CreateWorkflowTypeCommandValidator().Validate(command).Errors).ErrorMessage;

        Assert.Equal("'Company types' must not be empty.", message);
    }

    [Fact]
    public void BundleWithoutLists_SaysCompaniesAndCompanyTypes()
    {
        var bundle = new CounterpartyBundleDto { Counterparties = null!, CounterpartyTypes = null! };

        List<string> messages = [.. new ImportCounterpartiesCommandValidator()
            .Validate(new ImportCounterpartiesCommand("org-test", "user-test", bundle, DryRun: true))
            .Errors.Select(error => error.ErrorMessage)];

        Assert.Equal(["'Companies' must not be empty.", "'Company types' must not be empty."], messages);
    }

    [Fact]
    public void BundledTypeNameTooLong_SaysCompanyTypeName()
    {
        var entry = new CounterpartyBundleEntryDto { Id = "cp-1", Name = "Acme", CounterpartyTypeNames = [new string('x', 251)] };

        string message = Assert.Single(new CounterpartyBundleEntryValidator().Validate(entry).Errors).ErrorMessage;

        Assert.StartsWith("The length of 'Company type name' must be 250 characters or fewer.", message);
    }
}
