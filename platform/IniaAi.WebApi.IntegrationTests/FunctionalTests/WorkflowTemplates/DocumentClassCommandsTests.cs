using ErrorOr;
using IniaAi.Application.Commands.WorkflowTemplates;
using IniaAi.Application.Commands.WorkflowTemplates.CreateDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.DeleteDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.RenameDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.UpdateDocumentClass;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateDocumentClasses;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowTemplates;

public class DocumentClassCommandsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;

    public DocumentClassCommandsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    [Fact]
    public async Task Create_CreatesNewDocumentClass_Persists()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_Create");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", DisplayName: "Invoice", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));
        Assert.Equal("invoice", result.Value.Key);
        Assert.NotNull(result.Value.DocumentClass);
        Assert.Equal("Invoice", result.Value.DocumentClass!.DisplayName);

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        Assert.Contains(saved.DocumentClasses, dc => dc.Key == "invoice");
    }

    [Fact]
    public async Task Create_EmptyDocumentCategoryId_PersistsNull()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_CreateEmptyCat");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", DisplayName: "Invoice", Description: "",
                DocumentCategoryId: "", Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        DocumentClass persisted = saved.DocumentClasses.Single(dc => dc.Key == "invoice");
        Assert.Null(persisted.DocumentCategoryId);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_CreateDup");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", DisplayName: "Invoice", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Create_ReservedKey_ReturnsValidationError()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_Reserved");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "Any", DisplayName: "Bad", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public async Task Create_UndefinedRequirement_ReturnsValidationError()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_CreateBadEnum");

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", DisplayName: "Invoice", Description: "",
                DocumentCategoryId: null, Requirement: (DocumentClassRequirement)99,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: null));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public async Task Update_UndefinedRequirement_ReturnsValidationError()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_UpdateBadEnum");

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new UpdateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId, Key: "invoice",
                DisplayName: null, Requirement: (DocumentClassRequirement)99,
                ShouldBeSingular: null, IsOutputDocument: null,
                Description: null, DocumentCategoryId: null, BaseVersionId: null));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public async Task Update_OnlyDisplayName_LeavesOtherFieldsUnchanged()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_UpdateMerge");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice", configureClass: dc =>
        {
            dc.DisplayName = "Invoice";
            dc.Description = "Original description";
            dc.Requirement = DocumentClassRequirement.Required;
            dc.ShouldBeSingular = true;
            dc.IsOutputDocument = true;
            dc.DocumentCategoryId = "cat-1";
        });
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new UpdateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice",
                DisplayName: "Renamed Display",
                Requirement: null, ShouldBeSingular: null, IsOutputDocument: null,
                Description: null, DocumentCategoryId: null, BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        DocumentClass persisted = saved.DocumentClasses.Single(dc => dc.Key == "invoice");
        Assert.Equal("Renamed Display", persisted.DisplayName);
        Assert.Equal("Original description", persisted.Description);
        Assert.Equal(DocumentClassRequirement.Required, persisted.Requirement);
        Assert.True(persisted.ShouldBeSingular);
        Assert.True(persisted.IsOutputDocument);
        Assert.Equal("cat-1", persisted.DocumentCategoryId);
    }

    [Fact]
    public async Task Update_EmptyStrings_ClearDescriptionAndCategory()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_UpdateClear");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice", configureClass: dc =>
        {
            dc.Description = "Original description";
            dc.DocumentCategoryId = "cat-1";
        });
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new UpdateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice",
                DisplayName: null, Requirement: null, ShouldBeSingular: null, IsOutputDocument: null,
                Description: "", DocumentCategoryId: "", BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        DocumentClass persisted = saved.DocumentClasses.Single(dc => dc.Key == "invoice");
        Assert.Equal(string.Empty, persisted.Description);
        Assert.Null(persisted.DocumentCategoryId);
    }

    [Fact]
    public async Task Update_KeyDiffersOnlyByCase_FindsClassWithoutRenaming()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_UpdateCase");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new UpdateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "INVOICE",
                DisplayName: "Renamed Display",
                Requirement: null, ShouldBeSingular: null, IsOutputDocument: null,
                Description: null, DocumentCategoryId: null, BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        DocumentClass persisted = Assert.Single(saved.DocumentClasses);
        Assert.Equal("invoice", persisted.Key);
        Assert.Equal("Renamed Display", persisted.DisplayName);
    }

    [Fact]
    public async Task Update_NonexistentClass_ReturnsNotFound()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_UpdateMissing");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new UpdateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "ghost",
                DisplayName: "X", Requirement: null, ShouldBeSingular: null, IsOutputDocument: null,
                Description: null, DocumentCategoryId: null, BaseVersionId: baseVersionId));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Rename_ExistingClass_CascadesToBlocks_Persists()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_Rename");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice", configureClass: dc =>
        {
            dc.DisplayName = "Invoice";
            dc.Requirement = DocumentClassRequirement.Required;
        });
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new RenameDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", NewKey: "invoice_v2", BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));
        Assert.Equal("invoice_v2", result.Value.Key);

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        DocumentClass persisted = saved.DocumentClasses.Single(dc => dc.Key == "invoice_v2");
        Assert.Equal("Invoice", persisted.DisplayName);
        Assert.Equal(DocumentClassRequirement.Required, persisted.Requirement);
        Assert.DoesNotContain(saved.DocumentClasses, dc => dc.Key == "invoice");
        Block block = saved.GetAllBlocks().Single(b => b.ReplacementTag == "consumer");
        Assert.Equal(["invoice_v2"], block.SupportedDocumentClasses);
    }

    [Fact]
    public async Task Rename_KeyDiffersOnlyByCase_FindsClass()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_RenameCase");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new RenameDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "INVOICE", NewKey: "receipt", BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));
        Assert.Equal("receipt", result.Value.Key);

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        Block block = saved.GetAllBlocks().Single(b => b.ReplacementTag == "consumer");
        Assert.Equal(["receipt"], block.SupportedDocumentClasses);
    }

    [Fact]
    public async Task Rename_ToExistingKey_ReturnsConflict()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_RenameDup");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> created = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "receipt", DisplayName: "Receipt", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));
        Assert.False(created.IsError, ErrorText(created));

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new RenameDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "invoice", NewKey: "receipt", BaseVersionId: created.Value.VersionId));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Delete_RemovesClass_CascadesToBlocks_Persists()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_Delete");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new DeleteDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId, Key: "invoice", BaseVersionId: baseVersionId));

        Assert.False(result.IsError, ErrorText(result));
        Assert.Null(result.Value.DocumentClass);

        Workflow saved = await ReadWorkflowAsync(handle.User, typeId);
        Assert.DoesNotContain(saved.DocumentClasses, dc => dc.Key == "invoice");
        Block block = saved.GetAllBlocks().Single(b => b.ReplacementTag == "consumer");
        Assert.Equal([DocumentClass.KEY_ANY], block.SupportedDocumentClasses);
    }

    [Fact]
    public async Task Delete_NonexistentClass_ReturnsNotFound()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_DeleteMissing");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> result = await _apiBuilder.SendAsync(
            new DeleteDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId, Key: "ghost", BaseVersionId: baseVersionId));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Create_StaleBaseVersionId_ReturnsConflict()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_Conflict");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: null);
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> first = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "first", DisplayName: "First", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));
        Assert.False(first.IsError, ErrorText(first));

        ErrorOr<DocumentClassMutationResult> stale = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "second", DisplayName: "Second", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));

        Assert.True(stale.IsError);
        Assert.Equal(ErrorType.Conflict, stale.FirstError.Type);
    }

    [Fact]
    public async Task List_AfterSave_ReturnsClassesAndVersionId()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_List");
        await SeedWorkflowAsync(handle.User.OrganizationId, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> created = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "receipt", DisplayName: "Receipt", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));
        Assert.False(created.IsError, ErrorText(created));

        ErrorOr<DocumentClassListResult> result = await _apiBuilder.SendAsync(
            new GetWorkflowTemplateDocumentClassesQuery(handle.User.OrganizationId, handle.User.Id, typeId));

        Assert.False(result.IsError, ErrorText(result));
        Assert.Equal(created.Value.VersionId, result.Value.VersionId);
        Assert.Contains(result.Value.DocumentClasses, dc => dc.Key == "invoice");
        Assert.Contains(result.Value.DocumentClasses, dc => dc.Key == "receipt");
    }

    [Fact]
    public async Task Commands_CallerOrgHasNoAccessToType_StillMutate()
    {
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        string typeId = UniqueTypeId("DocClassCmd_NoAccess");
        string owningOrg = "owning-org-" + Guid.NewGuid().ToString("N")[..8];
        await SeedWorkflowAsync(owningOrg, typeId, existingClassKey: "invoice");
        string? baseVersionId = await GetHeadVersionIdAsync(handle.User, typeId);

        ErrorOr<DocumentClassMutationResult> create = await _apiBuilder.SendAsync(
            new CreateDocumentClassCommand(
                handle.User.OrganizationId, handle.User.Id, typeId,
                Key: "receipt", DisplayName: "Receipt", Description: "",
                DocumentCategoryId: null, Requirement: DocumentClassRequirement.Optional,
                ShouldBeSingular: false, IsOutputDocument: false, BaseVersionId: baseVersionId));
        Assert.False(create.IsError, ErrorText(create));

        ErrorOr<DocumentClassListResult> list = await _apiBuilder.SendAsync(
            new GetWorkflowTemplateDocumentClassesQuery(handle.User.OrganizationId, handle.User.Id, typeId));
        Assert.False(list.IsError, ErrorText(list));
        Assert.Contains(list.Value.DocumentClasses, dc => dc.Key == "receipt");
    }

    private static string UniqueTypeId(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string ErrorText<T>(ErrorOr<T> result)
    {
        return result.IsError ? result.FirstError.Description : string.Empty;
    }

    private async Task<string?> GetHeadVersionIdAsync(User user, string typeId)
    {
        ErrorOr<WorkflowTemplateContentResult> content = await _apiBuilder.SendAsync(
            new GetWorkflowTemplateContentQuery(user.OrganizationId, user.Id, typeId));
        Assert.False(content.IsError, ErrorText(content));
        return content.Value.VersionId;
    }

    private async Task<Workflow> ReadWorkflowAsync(User user, string typeId)
    {
        ErrorOr<WorkflowTemplateContentResult> content = await _apiBuilder.SendAsync(
            new GetWorkflowTemplateContentQuery(user.OrganizationId, user.Id, typeId));
        Assert.False(content.IsError, ErrorText(content));
        Workflow? workflow = Workflow.FromJson(content.Value.WorkflowJson);
        Assert.NotNull(workflow);
        return workflow;
    }

    private async Task SeedWorkflowAsync(
        string organizationId,
        string workflowTypeId,
        string? existingClassKey,
        Action<DocumentClass>? configureClass = null)
    {
        (IServiceScope? scope, ICoreUnitOfWork? coreUnitOfWork) = _apiBuilder.GetNewScope<ICoreUnitOfWork>();
        using (scope)
        {
            await EnsureWorkflowTypeAsync(coreUnitOfWork, workflowTypeId, organizationId);

            Workflow? existing = await coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowTypeId);
            if (existing is not null)
            {
                coreUnitOfWork.Workflows.Remove(existing);
                _ = await coreUnitOfWork.CompleteAsync();
            }

            var workflow = new Workflow("Document Class Command Test")
            {
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            };
            var section = new Section("Section 1", workflow);
            var block = new Block(BlockTypes.AiQuery, section)
            {
                ReplacementTag = "consumer",
                Properties = new AiQueryProperties { UserPromptTemplate = "Analyze" },
            };

            if (existingClassKey is not null)
            {
                var documentClass = new DocumentClass(workflow, existingClassKey) { DisplayName = existingClassKey };
                configureClass?.Invoke(documentClass);
                block.SupportedDocumentClasses = [existingClassKey];
            }

            await coreUnitOfWork.Workflows.AddAsync(workflow);
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }

    private static async Task EnsureWorkflowTypeAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string workflowTypeId,
        string organizationId)
    {
        bool typeExists = await coreUnitOfWork.WorkflowTypes.ExistsAsync(workflowTypeId);
        if (!typeExists)
        {
            WorkflowTypeCategory? category = await coreUnitOfWork.Repository<WorkflowTypeCategory>()
                .GetAll().FirstOrDefaultAsync();
            string categoryId;
            if (category is not null)
            {
                categoryId = category.Id;
            }
            else
            {
                categoryId = "test-category";
                await coreUnitOfWork.Repository<WorkflowTypeCategory>().AddAsync(new WorkflowTypeCategory
                {
                    Id = categoryId,
                    Name = "Test Workflows",
                    Description = "Category for test workflows",
                    DisplayOrder = 999,
                });
                _ = await coreUnitOfWork.CompleteAsync();
            }

            await coreUnitOfWork.WorkflowTypes.AddAsync(new WorkflowType(workflowTypeId, $"Test: {workflowTypeId}")
            {
                Description = "Test workflow type",
                CategoryId = categoryId,
            });
            await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(new WorkflowTypeOrganizationAccess
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            });
            _ = await coreUnitOfWork.CompleteAsync();
            return;
        }

        WorkflowTypeOrganizationAccess? existingAccess = await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
            .GetAll()
            .Where(a => a.WorkflowTypeId == workflowTypeId && a.OrganizationId == organizationId)
            .FirstOrDefaultAsync();
        if (existingAccess is null)
        {
            await coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(new WorkflowTypeOrganizationAccess
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowTypeId = workflowTypeId,
                OrganizationId = organizationId,
            });
            _ = await coreUnitOfWork.CompleteAsync();
        }
    }
}
