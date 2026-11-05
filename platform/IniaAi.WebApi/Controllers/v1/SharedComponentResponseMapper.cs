using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Commands.SharedComponents.CreateSharedComponent;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;
using IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;
using IniaAi.Application.Queries.SharedComponents.PreviewSharedComponentReconcile;
using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using IniaAi.WebApi.Contracts.Responses.WorkflowTransfer;
using ContractsCellState = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryCellState;
using ContractsEntryKind = IniaAi.WebApi.Contracts.Enums.SectionEntryKind;
using ContractsEntryOrigin = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryOrigin;
using ContractsEntryOverrideState = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryOverrideState;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using ContractsLinkState = IniaAi.WebApi.Contracts.Enums.SharedComponentLinkState;
using ContractsOverrideImpactKind = IniaAi.WebApi.Contracts.Enums.SharedComponentOverrideImpactKind;
using ContractsConsumerChannel = IniaAi.WebApi.Contracts.Enums.SharedComponentConsumerChannel;
using ContractsReconcileAction = IniaAi.WebApi.Contracts.Enums.SharedComponentReconcileAction;
using ContractsRelation = IniaAi.WebApi.Contracts.Enums.SharedComponentRelation;
using ContractsSlotRescueReason = IniaAi.WebApi.Contracts.Enums.SharedComponentSlotRescueReason;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using ContractsTransferComponentAction = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferComponentAction;
using ContractsTransferCorrelationMethod = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferCorrelationMethod;
using ContractsTransferRelation = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferRelation;

namespace IniaAi.WebApi.Controllers.v1;

internal static class SharedComponentResponseMapper
{
    internal static WorkflowSharedComponentReconcileResponse ToResponse(WorkflowSharedComponentReconcileResult result)
    {
        return new WorkflowSharedComponentReconcileResponse(
            result.WorkflowTypeId,
            result.BaseVersionId,
            result.BaseContentHash,
            [.. result.Instances.Select(ToDto)]);
    }

    internal static SharedComponentSuggestionsResponse ToResponse(SharedComponentBootstrapScanResult result)
    {
        return new SharedComponentSuggestionsResponse(
            [.. result.Suggestions.Select(ToDto)],
            result.ScannedWorkflowTypeCount,
            result.ElapsedMilliseconds);
    }

    private static SharedComponentSuggestionDto ToDto(SharedComponentBootstrapSuggestion suggestion)
    {
        return new SharedComponentSuggestionDto(
            (ContractsKind)(int)suggestion.Kind,
            suggestion.ContentHash,
            suggestion.SuggestedName,
            suggestion.CrossType,
            suggestion.ExistingComponentId,
            suggestion.ExistingComponentName,
            [.. suggestion.Nodes.Select(ToDto)]);
    }

    private static SharedComponentSuggestionNodeDto ToDto(SharedComponentBootstrapNode node)
    {
        return new SharedComponentSuggestionNodeDto(
            node.WorkflowTypeId,
            node.WorkflowTypeName,
            node.TargetPath,
            node.TargetStableId,
            node.ProvenanceComponentId,
            node.ProvenanceComponentName,
            node.SectionTitle,
            node.SectionOrder,
            node.BlockName,
            node.BlockOrder,
            node.WorkflowTypeCategoryName);
    }

    internal static SharedComponentReconcilePreviewResponse ToResponse(SharedComponentReconcilePreviewResult result)
    {
        return new SharedComponentReconcilePreviewResponse(
            result.SharedComponentId,
            result.HeadVersionId,
            result.HeadContentHash,
            result.CandidateContentHash,
            [.. result.Consumers.Select(ToDto)],
            ToDto(result.UpdateSharedImpact));
    }

    internal static SharedComponentReconcileApplyResponse ToResponse(SharedComponentReconcileApplyResult result)
    {
        return new SharedComponentReconcileApplyResponse(
            result.WorkflowTypeId,
            [.. result.DecisionOutcomes.Select(ToDto)],
            [.. result.WorkflowSaves.Select(ToDto)],
            [.. result.ComponentHeads.Select(ToDto)]);
    }

    internal static CreateSharedComponentResponse ToResponse(CreateSharedComponentResult result)
    {
        return new CreateSharedComponentResponse(
            result.SharedComponentId,
            result.Kind,
            result.Name,
            result.HeadVersionId,
            result.HeadContentHash);
    }

    internal static WorkflowTransferPreviewResponse ToResponse(ImportWorkflowTemplatePreviewResult result)
    {
        return new WorkflowTransferPreviewResponse(
            result.WorkflowTypeId,
            result.OriginWorkflowTypeId,
            result.BaseVersionId,
            ToDto(result.Template),
            [.. result.Components.Select(ToDto)],
            result.SkippedCarriers);
    }

    private static TransferTemplatePreviewRow ToDto(ImportTemplatePreviewRow row)
    {
        return new TransferTemplatePreviewRow(
            (ContractsTransferRelation)(int)row.Relation,
            row.ConflictCode,
            row.BundleHashTrail,
            row.LocalHashTrail);
    }

    private static TransferComponentPreviewRow ToDto(ImportComponentPreviewRow row)
    {
        return new TransferComponentPreviewRow(
            row.BundleComponentId,
            row.LocalComponentId,
            (ContractsKind)(int)row.Kind,
            row.Name,
            (ContractsTransferRelation)(int)row.Relation,
            (ContractsTransferCorrelationMethod)(int)row.CorrelationMethod,
            row.ConflictCode,
            row.BundleHeadContentHash,
            row.LocalHeadContentHash,
            row.LocalHeadVersionId,
            row.BundleHashTrail,
            row.LocalHashTrail,
            row.RefWarnings);
    }

    internal static WorkflowTransferApplyResponse ToResponse(ImportWorkflowTemplateApplyResult result)
    {
        return new WorkflowTransferApplyResponse(
            result.WorkflowTypeId,
            new TransferTemplateMutation(
                result.Template.VersionId,
                result.Template.ContentHash,
                result.Template.WasDeduped),
            [.. result.ComponentOutcomes.Select(ToDto)],
            result.SkippedCarriers);
    }

    private static TransferComponentOutcome ToDto(ImportComponentOutcome outcome)
    {
        return new TransferComponentOutcome(
            outcome.BundleComponentId,
            outcome.LocalComponentId,
            (ContractsTransferComponentAction)(int)outcome.Action,
            outcome.HeadVersionId,
            outcome.HeadContentHash);
    }

    internal static SharedComponentAppendResponse ToResponse(SharedComponentAppendResult result)
    {
        return new SharedComponentAppendResponse(
            result.SharedComponentId,
            result.HeadVersionId,
            result.HeadContentHash,
            result.WasDeduped,
            [.. result.ConsumerSaves.Select(ToDto)]);
    }

    internal static List<ConsumingWorkflowTypeDto> ToResponse(List<SharedComponentConsumer> consumers)
    {
        return [.. consumers.Select(ToDto)];
    }

    internal static SharedComponentConsumerMatrixResponse ToResponse(SharedComponentConsumerMatrix matrix)
    {
        return new SharedComponentConsumerMatrixResponse(
            matrix.SharedComponentId,
            matrix.HeadVersionId,
            matrix.HeadContentHash,
            [.. matrix.Entries.Select(entry => new SharedComponentMatrixEntryDto(
                entry.SlotTag,
                (ContractsEntryKind)(int)entry.Kind,
                entry.RefSharedComponentId,
                entry.IsLocalOnly))],
            [.. matrix.Rows.Select(row => new SharedComponentConsumerMatrixRowDto(
                row.WorkflowTypeId,
                row.WorkflowTypeName,
                row.StableSectionId,
                [.. row.Cells.Select(cell => new SharedComponentConsumerMatrixCellDto(
                    cell.SlotTag,
                    (ContractsCellState)(int)cell.State))]))]);
    }

    internal static SharedComponentRetireResponse ToResponse(SharedComponentRetireResult result)
    {
        return new SharedComponentRetireResponse(
            result.SharedComponentId,
            (ContractsState)(int)result.State,
            result.BrokenOutInstanceCount,
            [.. result.WorkflowSaves.Select(ToDto)],
            [.. result.InlinedContainerIds]);
    }

    private static SharedComponentInstanceReconcileDto ToDto(SharedComponentInstanceReconcileRow row)
    {
        return new SharedComponentInstanceReconcileDto(
            row.TargetPath,
            row.TargetStableId,
            (ContractsKind)(int)row.Kind,
            row.SharedComponentId,
            row.ComponentName,
            (ContractsState?)(int?)row.ComponentState,
            (ContractsLinkState)(int)row.LinkState,
            (ContractsRelation)(int)row.Relation,
            row.InstanceContentHash,
            row.LinkVersionHash,
            row.HeadVersionId,
            row.HeadContentHash,
            row.LinkHashStale,
            row.OldDefinitionAvailable,
            [.. row.AvailableActions.Select(action => (ContractsReconcileAction)(int)action)],
            Entries: row.Entries?.Select(ToDto).ToList(),
            OrderDeviates: row.OrderDeviates,
            HasLocalOverrides: row.HasLocalOverrides,
            LocalInsertCount: row.LocalInsertCount);
    }

    private static SharedComponentEntryReconcileDto ToDto(SharedComponentEntryReconcileRow entry)
    {
        return new SharedComponentEntryReconcileDto(
            entry.EntryKey,
            entry.EntryStableId,
            entry.EntryPath,
            entry.DisplayName,
            (ContractsEntryOrigin)(int)entry.Origin,
            (ContractsEntryOverrideState)(int)entry.OverrideState,
            entry.RefSharedComponentId,
            entry.RefComponentName,
            (ContractsState?)(int?)entry.RefComponentState,
            entry.RefHeadVersionId,
            entry.RefHeadContentHash,
            (ContractsRelation?)(int?)entry.Relation,
            entry.InstanceContentHash,
            entry.DeletedByDefinition,
            [.. entry.AvailableActions.Select(action => (ContractsReconcileAction)(int)action)],
            (ContractsLinkState?)(int?)entry.InsertLinkState);
    }

    private static ConsumingWorkflowTypeDto ToDto(SharedComponentConsumer consumer)
    {
        return new ConsumingWorkflowTypeDto(
            consumer.WorkflowTypeId,
            consumer.WorkflowTypeName,
            consumer.LinkedInstanceCount,
            consumer.CategoryId,
            consumer.CategoryName,
            (ContractsConsumerChannel)(int)consumer.Channel,
            consumer.ViaSharedComponentId,
            consumer.ViaSharedComponentName);
    }

    private static SharedComponentUpdateImpactDto ToDto(SharedComponentUpdateImpact impact)
    {
        return new SharedComponentUpdateImpactDto(
            [.. impact.AffectedBaselines.Select(ToDto)],
            [.. impact.OrphanedSignatureOverrides.Select(ToDto)],
            ToDto(impact.SlotDelta));
    }

    private static SharedComponentBaselineImpactDto ToDto(SharedComponentBaselineImpact baseline)
    {
        return new SharedComponentBaselineImpactDto(
            baseline.RegistrationId,
            baseline.WorkflowTypeId,
            baseline.OrganizationId,
            baseline.CounterpartyId,
            baseline.CounterpartyName,
            baseline.IsCurrentlyFresh);
    }

    private static SharedComponentOverrideImpactDto ToDto(SharedComponentOverrideImpact impact)
    {
        return new SharedComponentOverrideImpactDto(
            impact.OrganizationId,
            impact.WorkflowTypeId,
            impact.StableBlockId,
            impact.BlockTag,
            impact.PlaceholderName,
            impact.IsPending,
            (ContractsOverrideImpactKind)(int)impact.Kind);
    }

    private static SharedComponentSlotDeltaDto? ToDto(SharedComponentSlotDelta? delta)
    {
        if (delta is null)
        {
            return null;
        }

        return new SharedComponentSlotDeltaDto(
            [.. delta.MatchedSlotTags],
            [.. delta.AddedSlotTags],
            [.. delta.RemovedSlotTags],
            new Dictionary<string, string>(delta.RenamedSlots),
            [.. delta.RescuedSlots.Select(ToDto)]);
    }

    private static SharedComponentSlotRescueDto ToDto(SharedComponentSlotRescue rescue)
    {
        return new SharedComponentSlotRescueDto(
            rescue.OldTag,
            rescue.NewTag,
            (ContractsSlotRescueReason)(int)rescue.Reason);
    }

    private static SharedComponentReconcileDecisionOutcomeDto ToDto(SharedComponentReconcileDecisionOutcome outcome)
    {
        return new SharedComponentReconcileDecisionOutcomeDto(
            outcome.SharedComponentId,
            outcome.TargetPath,
            outcome.TargetStableId,
            (ContractsReconcileAction)(int)outcome.Action,
            outcome.AlreadyCurrent);
    }

    private static SharedComponentReconciledHeadDto ToDto(SharedComponentReconciledHead head)
    {
        return new SharedComponentReconciledHeadDto(
            head.SharedComponentId,
            head.HeadVersionId,
            head.HeadContentHash,
            head.Appended);
    }

    private static SharedComponentWorkflowSaveDto ToDto(SharedComponentConsumerSaveResult save)
    {
        return new SharedComponentWorkflowSaveDto(
            save.WorkflowTypeId,
            save.VersionId,
            save.ContentHash,
            save.WasDeduped,
            [.. save.Warnings.Select(warning => new WorkflowTemplateWarningDto(warning.Path, warning.Message))],
            ToDto(save.SignatureSync),
            [.. save.OrphanedOverrides.Select(ToDto)]);
    }

    private static SignatureSyncDto ToDto(SignatureSyncResult sync)
    {
        return new SignatureSyncDto(
            sync.OverridesRenamed,
            sync.OverridesDeleted,
            sync.PendingsDeleted);
    }
}
