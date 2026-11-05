namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// A role a closed module adds to the deployment. A build without the module neither seeds nor
/// offers it, and an assignment left in the database gates nothing there.
/// </summary>
/// <param name="Name">The stored role name the module's gates check.</param>
/// <param name="DisplayName">The name the administration screens show.</param>
/// <param name="ActsWithinOrganizations">Whether the role acts only within the organizations its holder belongs to, so an organization admin may grant it.</param>
/// <param name="GrantedToTheCustomerAdmin">Whether the seed grants the role to the customer admin it creates.</param>
public sealed record ModuleRole(string Name, string DisplayName, bool ActsWithinOrganizations, bool GrantedToTheCustomerAdmin = false);
