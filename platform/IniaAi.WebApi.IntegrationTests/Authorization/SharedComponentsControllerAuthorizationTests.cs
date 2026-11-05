using IniaAi.WebApi.Controllers.v1;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

public class SharedComponentsControllerAuthorizationTests
{
    private static readonly Type ControllerType = typeof(SharedComponentsController);

    [Theory]
    [InlineData(nameof(SharedComponentsController.ListSharedComponents))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponent))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponentContent))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponentVersions))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponentConsumers))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponentConsumerMatrix))]
    [InlineData(nameof(SharedComponentsController.ListSharedComponentCategories))]
    [InlineData(nameof(SharedComponentsController.SaveSharedComponentCategory))]
    [InlineData(nameof(SharedComponentsController.DeleteSharedComponentCategory))]
    [InlineData(nameof(SharedComponentsController.SetSharedComponentCategory))]
    [InlineData(nameof(SharedComponentsController.GetSharedComponentVersionContent))]
    [InlineData(nameof(SharedComponentsController.CreateSharedComponent))]
    [InlineData(nameof(SharedComponentsController.SaveSharedComponentContent))]
    [InlineData(nameof(SharedComponentsController.PreviewSharedComponentReconcile))]
    [InlineData(nameof(SharedComponentsController.RestoreSharedComponentVersion))]
    [InlineData(nameof(SharedComponentsController.RetireSharedComponent))]
    [InlineData(nameof(SharedComponentsController.UnretireSharedComponent))]
    public void RegistryEndpoints_RestrictToWorkflowEditorAndSuperAdminOnly(string methodName)
    {
        ControllerAuthorizationAssertions.AssertRestrictedToWorkflowEditorAndSuperAdmin(ControllerType, methodName);
    }

    [Fact]
    public void Controller_DoesNotLockEntireClassToARole()
    {
        ControllerAuthorizationAssertions.AssertClassNotLockedToARole(ControllerType);
    }
}
