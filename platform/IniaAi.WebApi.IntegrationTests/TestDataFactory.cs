using Bogus;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Models.Dto;
using System.Text.Json;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// Provides reusable factory methods to build valid test data for commands, DTOs, or entities.
/// Uses Bogus for realistic random data.
/// </summary>
public static class TestDataFactory
{
    private static readonly Faker Faker = new();

    /// <summary>
    /// Builds a valid CreateCounterpartyCommand with unique realistic values.
    /// </summary>
    /// <param name="id">Optional ID to override the generated one.</param>
    /// <param name="organizationId">Optional Organization ID to override default.</param>
    /// <param name="counterpartyTypeId">Counterparty Type ID to override default.</param>
    /// <returns>A valid CreateCounterpartyCommand ready for testing.</returns>
    public static CreateCounterpartyCommand BuildValidCreateCounterparty(
        string? id = null,
        string? organizationId = null,
        string? counterpartyTypeId = null)
    {
        return new CreateCounterpartyCommand(
            Id: id ?? Guid.NewGuid().ToString(),
            OrganizationId: organizationId ?? "IniaAI",
            ParentId: null,
            CounterpartyTypeIds: counterpartyTypeId is not null ? [counterpartyTypeId] : null,
            Name: Faker.Company.CompanyName(),
            NameVariants: Faker.Company.CompanySuffix(),
            Email: Faker.Internet.Email(),
            PhoneNumber: Faker.Phone.PhoneNumber("+############"),
            Website: Faker.Internet.Url(),
            Address: Faker.Address.FullAddress(),
            LastFinancialAnalysisResult: null
        );
    }
    public static CounterpartyType BuildValidCounterpartyType(string? id = null)
    {
        return new CounterpartyType
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = $"Type-{Faker.Random.Number(100, 999)}",
            Description = "Test Counterparty Type"
        };
    }

    public static StartWorkflowExecutionCommand BuildValidStartWorkflowExecution(
        WorkflowDto workflowDto,
        string? counterpartyId,
        string userId,
        string organizationId,
        List<string>? peerCompanyIds = null,
        List<string>? fileIds = null,
        List<PostDocumentInfoRequest>? files = null)
    {
        var variablesDict = new Dictionary<string, JsonElement>();

        foreach (WorkflowVariableDto v in workflowDto.Variables)
        {
            JsonElement value;

            switch (v.Type)
            {
                case "String":
                    value = JsonDocument.Parse($"\"{v.DefaultValue ?? "SampleStringValue"}\"").RootElement;
                    break;

                case "List":
                case "MultiSelect":
                    string[] listItems = v.DefaultValue?.Split('|') ?? ["Item 1", "Item 2"];
                    string listJson = $"[{string.Join(",", listItems.Select(i => $"\"{i}\""))}]";
                    value = JsonDocument.Parse(listJson).RootElement;
                    break;

                case "Number":
                    int number = int.TryParse(v.DefaultValue, out int n) ? n : 123;
                    value = JsonDocument.Parse($"{number}").RootElement;
                    break;

                case "Dropdown":
                    value = JsonDocument.Parse($"\"{v.DefaultValue ?? v.Options.FirstOrDefault().Key ?? "Option1"}\"").RootElement;
                    break;

                case "DateTime":
                    string date = v.DefaultValue ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
                    value = JsonDocument.Parse($"\"{date}T00:00\"").RootElement;
                    break;

                default:
                    // Fallback to string
                    value = JsonDocument.Parse($"\"{v.DefaultValue ?? "Default"}\"").RootElement;
                    break;
            }

            variablesDict.Add(v.ReplacementTag, value);
        }

        // Use the provided files list, or build from fileIds if provided
        List<PostDocumentInfoRequest>? requestFiles = files ?? fileIds?.Select(fileId => new PostDocumentInfoRequest
        {
            FileId = fileId,
            ShouldUseOcr = false
        }).ToList();

        var request = new StartWorkflowExecutionRequest
        {
            WorkflowId = workflowDto.Id,
            Files = requestFiles,
            CounterpartyId = counterpartyId,
            PeerCompanyIds = peerCompanyIds,
            Variables = variablesDict,
            IsConfidential = false
        };

        return new StartWorkflowExecutionCommand(request, organizationId, userId, username: null, email: null);
    }
}
