using ApplicationConsumerChannel = IniaAi.Application.Common.Workflows.SharedComponentConsumerChannel;
using ApplicationEntryCellState = IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix.SharedComponentEntryCellState;
using ApplicationOverrideImpactKind = IniaAi.Application.Common.Workflows.SharedComponentOverrideImpactKind;
using ApplicationSlotRescueReason = IniaAi.Application.Common.Workflows.SharedComponentSlotRescueReason;
using ContractsConsumerChannel = IniaAi.WebApi.Contracts.Enums.SharedComponentConsumerChannel;
using ContractsEntryCellState = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryCellState;
using ContractsEntryOrigin = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryOrigin;
using ContractsSectionEntryKind = IniaAi.WebApi.Contracts.Enums.SectionEntryKind;
using DomainSectionEntryKind = IniaAi.Domain.Entities.Workflows.Enums.SectionEntryKind;
using ContractsEntryOverrideState = IniaAi.WebApi.Contracts.Enums.SharedComponentEntryOverrideState;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using ContractsLinkState = IniaAi.WebApi.Contracts.Enums.SharedComponentLinkState;
using ContractsOverrideImpactKind = IniaAi.WebApi.Contracts.Enums.SharedComponentOverrideImpactKind;
using ContractsReconcileAction = IniaAi.WebApi.Contracts.Enums.SharedComponentReconcileAction;
using ContractsRelation = IniaAi.WebApi.Contracts.Enums.SharedComponentRelation;
using ContractsSlotRescueReason = IniaAi.WebApi.Contracts.Enums.SharedComponentSlotRescueReason;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainEntryOrigin = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentEntryOrigin;
using DomainEntryOverrideState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentEntryOverrideState;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using DomainReconcileAction = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentReconcileAction;
using DomainRelation = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentRelation;
using DomainState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentState;
using ContractsTransferRelation = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferRelation;
using ContractsTransferCorrelationMethod = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferCorrelationMethod;
using DomainTransferRelation = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferRelation;
using DomainTransferCorrelationMethod = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;
using ContractsTransferArm = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferArm;
using DomainTransferArm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;
using ContractsTransferComponentAction = IniaAi.WebApi.Contracts.Enums.SharedComponentTransferComponentAction;
using DomainTransferComponentAction = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferComponentAction;

namespace IniaAi.Application.UnitTests.Contracts;

public class SharedComponentEnumAlignmentTests
{
    [Fact]
    public void KindEnums_StayAligned()
    {
        AssertAligned<DomainKind, ContractsKind>();
    }

    [Fact]
    public void StateEnums_StayAligned()
    {
        AssertAligned<DomainState, ContractsState>();
    }

    [Fact]
    public void LinkStateEnums_StayAligned()
    {
        AssertAligned<DomainLinkState, ContractsLinkState>();
    }

    [Fact]
    public void RelationEnums_StayAligned()
    {
        AssertAligned<DomainRelation, ContractsRelation>();
    }

    [Fact]
    public void ReconcileActionEnums_StayAligned()
    {
        AssertAligned<DomainReconcileAction, ContractsReconcileAction>();
    }

    [Fact]
    public void OverrideImpactKindEnums_StayAligned()
    {
        AssertAligned<ApplicationOverrideImpactKind, ContractsOverrideImpactKind>();
    }

    [Fact]
    public void EntryOriginEnums_StayAligned()
    {
        AssertAligned<DomainEntryOrigin, ContractsEntryOrigin>();
    }

    [Fact]
    public void EntryOverrideStateEnums_StayAligned()
    {
        AssertAligned<DomainEntryOverrideState, ContractsEntryOverrideState>();
    }

    [Fact]
    public void SlotRescueReasonEnums_StayAligned()
    {
        AssertAligned<ApplicationSlotRescueReason, ContractsSlotRescueReason>();
    }

    [Fact]
    public void ConsumerChannelEnums_StayAligned()
    {
        AssertAligned<ApplicationConsumerChannel, ContractsConsumerChannel>();
    }

    [Fact]
    public void EntryCellStateEnums_StayAligned()
    {
        AssertAligned<ApplicationEntryCellState, ContractsEntryCellState>();
    }

    [Fact]
    public void SectionEntryKindEnums_StayAligned()
    {
        AssertAligned<DomainSectionEntryKind, ContractsSectionEntryKind>();
    }

    [Fact]
    public void TransferRelationEnums_StayAligned()
    {
        AssertAligned<DomainTransferRelation, ContractsTransferRelation>();
    }

    [Fact]
    public void TransferCorrelationMethodEnums_StayAligned()
    {
        AssertAligned<DomainTransferCorrelationMethod, ContractsTransferCorrelationMethod>();
    }

    [Fact]
    public void TransferArmEnums_StayAligned()
    {
        AssertAligned<DomainTransferArm, ContractsTransferArm>();
    }

    [Fact]
    public void TransferComponentActionEnums_StayAligned()
    {
        AssertAligned<DomainTransferComponentAction, ContractsTransferComponentAction>();
    }

    [Fact]
    public void ReconcileActionEnums_CarryPerEntryArms()
    {
        Assert.Equal(6, (int)DomainReconcileAction.Hide);
        Assert.Equal(7, (int)DomainReconcileAction.Unhide);
        Assert.Equal(6, (int)ContractsReconcileAction.Hide);
        Assert.Equal(7, (int)ContractsReconcileAction.Unhide);
    }

    private static void AssertAligned<TDomain, TContracts>()
        where TDomain : struct, Enum
        where TContracts : struct, Enum
    {
        Assert.Equal(Enum.GetNames<TDomain>(), Enum.GetNames<TContracts>());

        int[] domainValues = [.. Enum.GetValues<TDomain>().Select(v => Convert.ToInt32(v))];
        int[] contractsValues = [.. Enum.GetValues<TContracts>().Select(v => Convert.ToInt32(v))];
        Assert.Equal(domainValues, contractsValues);
    }
}
