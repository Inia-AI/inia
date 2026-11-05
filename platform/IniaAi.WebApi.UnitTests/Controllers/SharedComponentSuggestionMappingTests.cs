using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApi.Controllers.v1;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;

namespace IniaAi.WebApi.UnitTests.Controllers;

public class SharedComponentSuggestionMappingTests
{
    private static SharedComponentBootstrapNode Node(
        string workflowTypeId = "TypeA",
        string? blockName = "KYC Check",
        int? blockOrder = 2,
        string? provenanceId = null,
        string? provenanceName = null,
        string? stableId = "stable-1")
    {
        return new SharedComponentBootstrapNode(
            workflowTypeId,
            $"{workflowTypeId} Name",
            "Sections[1].Blocks[2]",
            stableId,
            provenanceId,
            provenanceName,
            "Diligence",
            1,
            blockName,
            blockOrder,
            "Credit");
    }

    [Fact]
    public void ToResponse_CarriesEverySuggestionAndNodeField()
    {
        var result = new SharedComponentBootstrapScanResult(
            [
                new SharedComponentBootstrapSuggestion(
                    DomainKind.Block,
                    "abc123",
                    "KYC Check",
                    CrossType: true,
                    ExistingComponentId: "comp-7",
                    ExistingComponentName: "KYC",
                    [Node(provenanceId: "prov-3", provenanceName: "Old KYC")]),
            ],
            ScannedWorkflowTypeCount: 12,
            ElapsedMilliseconds: 345);

        SharedComponentSuggestionsResponse response = SharedComponentResponseMapper.ToResponse(result);

        Assert.Equal(12, response.ScannedWorkflowTypeCount);
        Assert.Equal(345, response.ElapsedMilliseconds);

        SharedComponentSuggestionDto suggestion = Assert.Single(response.Suggestions);
        Assert.Equal(ContractsKind.Block, suggestion.Kind);
        Assert.Equal("abc123", suggestion.ContentHash);
        Assert.Equal("KYC Check", suggestion.SuggestedName);
        Assert.True(suggestion.CrossType);
        Assert.Equal("comp-7", suggestion.ExistingComponentId);
        Assert.Equal("KYC", suggestion.ExistingComponentName);

        SharedComponentSuggestionNodeDto node = Assert.Single(suggestion.Nodes);
        Assert.Equal("TypeA", node.WorkflowTypeId);
        Assert.Equal("TypeA Name", node.WorkflowTypeName);
        Assert.Equal("Sections[1].Blocks[2]", node.TargetPath);
        Assert.Equal("stable-1", node.TargetStableId);
        Assert.Equal("prov-3", node.ProvenanceComponentId);
        Assert.Equal("Old KYC", node.ProvenanceComponentName);
        Assert.Equal("Diligence", node.SectionTitle);
        Assert.Equal(1, node.SectionOrder);
        Assert.Equal("KYC Check", node.BlockName);
        Assert.Equal(2, node.BlockOrder);
        Assert.Equal("Credit", node.WorkflowTypeCategoryName);
    }

    [Fact]
    public void ToResponse_MapsSectionKindByValue()
    {
        var result = new SharedComponentBootstrapScanResult(
            [
                new SharedComponentBootstrapSuggestion(
                    DomainKind.Section,
                    "hash",
                    "Diligence",
                    CrossType: false,
                    ExistingComponentId: null,
                    ExistingComponentName: null,
                    [Node(blockName: null, blockOrder: null)]),
            ],
            ScannedWorkflowTypeCount: 1,
            ElapsedMilliseconds: 1);

        SharedComponentSuggestionsResponse response = SharedComponentResponseMapper.ToResponse(result);

        SharedComponentSuggestionDto suggestion = Assert.Single(response.Suggestions);
        Assert.Equal(ContractsKind.Section, suggestion.Kind);
        Assert.False(suggestion.CrossType);
        Assert.Null(suggestion.ExistingComponentId);
        Assert.Null(suggestion.ExistingComponentName);

        SharedComponentSuggestionNodeDto node = Assert.Single(suggestion.Nodes);
        Assert.Null(node.BlockName);
        Assert.Null(node.BlockOrder);
        Assert.Null(node.ProvenanceComponentId);
        Assert.Null(node.ProvenanceComponentName);
    }

    [Fact]
    public void ToResponse_PreservesNodeOrderAcrossWorkflowTypes()
    {
        var result = new SharedComponentBootstrapScanResult(
            [
                new SharedComponentBootstrapSuggestion(
                    DomainKind.Block,
                    "hash",
                    "Shared",
                    CrossType: true,
                    ExistingComponentId: null,
                    ExistingComponentName: null,
                    [Node("TypeA"), Node("TypeB"), Node("TypeC")]),
            ],
            ScannedWorkflowTypeCount: 3,
            ElapsedMilliseconds: 7);

        SharedComponentSuggestionsResponse response = SharedComponentResponseMapper.ToResponse(result);

        Assert.Equal(
            ["TypeA", "TypeB", "TypeC"],
            response.Suggestions[0].Nodes.Select(node => node.WorkflowTypeId));
    }

    [Fact]
    public void ToResponse_EmptyScanCarriesCountsNotNullCollections()
    {
        var result = new SharedComponentBootstrapScanResult([], ScannedWorkflowTypeCount: 9, ElapsedMilliseconds: 4);

        SharedComponentSuggestionsResponse response = SharedComponentResponseMapper.ToResponse(result);

        Assert.Empty(response.Suggestions);
        Assert.Equal(9, response.ScannedWorkflowTypeCount);
        Assert.Equal(4, response.ElapsedMilliseconds);
    }
}
