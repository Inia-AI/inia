using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Domain.Business.Reporting;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.DataManagement;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.EntityFrameworkCore;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Common.Interfaces.Persistence;

public interface IUserDataDbContext : IBaseDbContext
{
    DbSet<Counterparty> Counterparties { get; set; }

    DbSet<CounterpartyType> CounterpartyTypes { get; set; }

    DbSet<HedgeFundsMonitoringObservation> HedgeFundsMonitoringObservations { get; set; }

    DbSet<PrivateEquityMonitoringObservation> PrivateEquityMonitoringObservations { get; set; }

    DbSet<PrivateEquityMonitoringAllocation> PrivateEquityMonitoringAllocations { get; set; }

    DbSet<Document> Documents { get; set; }

    DbSet<DocumentUnlockAuditLog> DocumentUnlockAuditLogs { get; set; }

    DbSet<ScreeningRecord> ScreeningRecords { get; set; }

    DbSet<DocumentInfo> DocumentInfos { get; set; }

    DbSet<EmbeddingVector> EmbeddingVectors { get; set; }

    DbSet<File> Files { get; set; } // For blob storage only

    DbSet<FileMetadata> FileMetadata { get; set; } // For file metadata

    DbSet<Organization> Organizations { get; set; }

    DbSet<Page> Pages { get; set; }

    DbSet<Block> Blocks { get; set; }

    DbSet<BlockResponse> BlockResponses { get; set; }

    DbSet<Workflow> Workflows { get; set; }

    DbSet<RagSettings> RagSettings { get; set; }

    DbSet<TextInput> TextInputs { get; set; }

    DbSet<WorkflowExecutionCounterparty> WorkflowExecutionCounterparties { get; set; }

    DbSet<ValidationRun> ValidationRuns { get; set; }

    //DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    DbSet<WorkflowExecution> WorkflowExecutions { get; set; }

    DbSet<WorkflowExecutionLog> WorkflowExecutionLogs { get; set; }

    DbSet<TokenUsage> TokenUsages { get; set; }

    DbSet<TokenUsageDailyTotal> TokenUsageDailyTotals { get; set; }

    DbSet<TokenUsageExecutionTotal> TokenUsageExecutionTotals { get; set; }

    DbSet<OrganizationModelSettings> OrganizationModelSettings { get; set; }

    DbSet<OrganizationCleanupSettings> OrganizationCleanupSettings { get; set; }

    DbSet<User> Users { get; }

    // OpenIddict sets
    // DbSet<OpenIddictEntityFrameworkCoreApplication> Applications { get; }
    // DbSet<OpenIddictEntityFrameworkCoreAuthorization> Authorizations { get; }
    // DbSet<OpenIddictEntityFrameworkCoreScope> Scopes { get; }
    // DbSet<OpenIddictEntityFrameworkCoreToken> Tokens { get; }

    /// <summary>
    /// Checks for records in the key tables and returns true/false map.
    /// </summary>
    /// <returns>A dictionary with table names as keys and bool values for record existence.</returns>
    Dictionary<string, bool> GetTableStatuses();
}
