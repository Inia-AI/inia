using IniaAi.Domain.Entities.SelfServe;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// The self-serve tables are kept schema: they exist, empty, on every deployment. Ledger rows keep
/// their organization, user and run as plain columns, so deleting any of those never deletes
/// ledger history.
/// </summary>
public sealed class SelfServeTablesTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-self-serve";
    private const string USER_ID = "user-self-serve";
    private const string RUN_ID = "run-self-serve";
    private const string STRIPE = "stripe";
    private const string ADDRESS_HASH = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteSeedDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task EachNewTable_StoresARow_AndReadsItBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var pack = new CreditPack
        {
            OrganizationId = ORGANIZATION_ID,
            Source = CreditPackSource.Subscription,
            Amount = 100,
            Remaining = 60,
            ExpiresAt = Now.AddMonths(2),
            PaymentProvider = STRIPE,
            ProviderInvoiceId = "in_1",
            InvoiceAmountPaid = 2900,
            InvoiceCurrency = "GBP",
        };
        var hold = new CreditEntry
        {
            OrganizationId = ORGANIZATION_ID,
            CreditPackId = pack.Id,
            Kind = CreditEntryKind.Hold,
            Amount = -40,
            AvailableBefore = 100,
            AvailableAfter = 60,
            WorkflowExecutionId = RUN_ID,
            Status = CreditHoldStatus.Pending,
        };
        var help = new HelpRequest { OrganizationId = ORGANIZATION_ID, UserId = USER_ID, WorkflowExecutionId = RUN_ID, Message = "The totals look wrong.", Status = HelpRequestStatus.Received };
        var grant = new SupportGrant { OrganizationId = ORGANIZATION_ID, WorkflowExecutionId = RUN_ID, HelpRequestId = help.Id, GrantedByUserId = USER_ID, ExpiresAt = Now.AddDays(7) };
        var boot = new HostBoot { Application = "IniaAi.WebApi", Machine = "api-1" };

        await using (SqliteUserDataDbContext db = NewContext())
        {
            _ = db.Organizations.Add(new Organization("Self-Serve Customer", ORGANIZATION_ID) { IsSelfServe = true });
            _ = db.Users.Add(new User(ORGANIZATION_ID) { Id = USER_ID, UserName = "customer@example.com", Email = "customer@example.com" });
            _ = db.UserOrganizations.Add(new UserOrganization { UserId = USER_ID, OrganizationId = ORGANIZATION_ID, AssignedByUserId = "operator-1" });
            _ = db.HostBoots.Add(boot);
            _ = db.WorkflowExecutions.Add(new WorkflowExecution { Id = RUN_ID, OrganizationId = ORGANIZATION_ID, HostBootId = boot.Id });
            _ = db.CreditAccounts.Add(new CreditAccount { OrganizationId = ORGANIZATION_ID, Available = 60, Pending = 40, SuspendedReason = CreditSuspension.FailedRunCap, SuspendedAt = Now });
            _ = db.CreditPacks.Add(pack);
            _ = db.CreditEntries.Add(hold);
            _ = db.CreditEntries.Add(new CreditEntry { OrganizationId = ORGANIZATION_ID, CreditPackId = pack.Id, Kind = CreditEntryKind.Release, Amount = 40, AvailableBefore = 60, AvailableAfter = 100, HoldEntryId = hold.Id });
            _ = db.Subscriptions.Add(new Subscription { OrganizationId = ORGANIZATION_ID, PaymentProvider = STRIPE, ProviderCustomerId = "cus_1", ProviderSubscriptionId = "sub_1", Status = SubscriptionStatus.Active, LiveOrganizationId = ORGANIZATION_ID, CurrentPeriodEnd = Now.AddMonths(1) });
            _ = db.PaymentEvents.Add(new PaymentEvent { PaymentProvider = STRIPE, ProviderEventId = "evt_1", Type = "invoice.paid", OrganizationId = ORGANIZATION_ID, Payload = "{}" });
            _ = db.Consents.Add(new Consent { OrganizationId = ORGANIZATION_ID, UserId = USER_ID, Kind = ConsentKind.AutomaticRenewal, TermsVersion = "2026-11", ClientAddress = "203.0.113.7", CheckoutSessionId = "cs_1" });
            _ = db.HelpRequests.Add(help);
            _ = db.HelpRequestReplies.Add(new HelpRequestReply { HelpRequestId = help.Id, AuthorUserId = "operator-1", Message = "We are looking into it." });
            _ = db.SupportGrants.Add(grant);
            _ = db.SupportAccesses.Add(new SupportAccess { SupportGrantId = grant.Id, OperatorUserId = "operator-1", Resource = $"report/{RUN_ID}" });
            _ = db.EmailCodeRequests.Add(new EmailCodeRequest { AddressHash = ADDRESS_HASH, CodeHash = ADDRESS_HASH, ExpiresAt = Now.AddMinutes(10) });
            _ = db.EmailCodeLockouts.Add(new EmailCodeLockout { AddressHash = ADDRESS_HASH, FailedAttempts = 2 });
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        await using SqliteUserDataDbContext read = NewContext();
        Assert.True((await read.Organizations.SingleAsync(o => o.Id == ORGANIZATION_ID, cancellationToken)).IsSelfServe);
        Assert.Equal("operator-1", (await read.UserOrganizations.SingleAsync(cancellationToken)).AssignedByUserId);
        Assert.Equal(boot.Id, (await read.WorkflowExecutions.SingleAsync(cancellationToken)).HostBootId);
        Assert.Equal(CreditSuspension.FailedRunCap, (await read.CreditAccounts.SingleAsync(cancellationToken)).SuspendedReason);
        Assert.Equal(2900, (await read.CreditPacks.SingleAsync(cancellationToken)).InvoiceAmountPaid);
        Assert.Equal(hold.Id, (await read.CreditEntries.SingleAsync(e => e.Kind == CreditEntryKind.Release, cancellationToken)).HoldEntryId);
        Assert.Equal(ORGANIZATION_ID, (await read.Subscriptions.SingleAsync(cancellationToken)).LiveOrganizationId);
        Assert.Equal("invoice.paid", (await read.PaymentEvents.SingleAsync(cancellationToken)).Type);
        Assert.Equal(ConsentKind.AutomaticRenewal, (await read.Consents.SingleAsync(cancellationToken)).Kind);
        Assert.Single((await read.HelpRequests.Include(h => h.Replies).SingleAsync(cancellationToken)).Replies);
        Assert.Single((await read.SupportGrants.Include(g => g.Accesses).SingleAsync(cancellationToken)).Accesses);
        Assert.Equal(ADDRESS_HASH, (await read.EmailCodeRequests.SingleAsync(cancellationToken)).AddressHash);
        Assert.Equal(2, (await read.EmailCodeLockouts.SingleAsync(cancellationToken)).FailedAttempts);
        Assert.Equal("api-1", (await read.HostBoots.SingleAsync(cancellationToken)).Machine);
    }

    [Fact]
    public async Task ASecondPackForOneInvoice_IsRefused()
    {
        await using SqliteUserDataDbContext db = NewContext();
        _ = db.CreditPacks.Add(new CreditPack { OrganizationId = ORGANIZATION_ID, Amount = 10, PaymentProvider = STRIPE, ProviderInvoiceId = "in_twice" });
        _ = db.CreditPacks.Add(new CreditPack { OrganizationId = ORGANIZATION_ID, Amount = 10, PaymentProvider = STRIPE, ProviderInvoiceId = "in_twice" });

        _ = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecondLiveSubscriptionWithOneProvider_IsRefused_AndAnEndedOneIsNot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SqliteUserDataDbContext db = NewContext();
        _ = db.Subscriptions.Add(new Subscription { OrganizationId = ORGANIZATION_ID, PaymentProvider = STRIPE, ProviderCustomerId = "cus_1", ProviderSubscriptionId = "sub_old", Status = SubscriptionStatus.Canceled });
        _ = db.Subscriptions.Add(new Subscription { OrganizationId = ORGANIZATION_ID, PaymentProvider = STRIPE, ProviderCustomerId = "cus_1", ProviderSubscriptionId = "sub_live", Status = SubscriptionStatus.Active, LiveOrganizationId = ORGANIZATION_ID });
        _ = await db.SaveChangesAsync(cancellationToken);

        _ = db.Subscriptions.Add(new Subscription { OrganizationId = ORGANIZATION_ID, PaymentProvider = STRIPE, ProviderCustomerId = "cus_1", ProviderSubscriptionId = "sub_second", Status = SubscriptionStatus.Active, LiveOrganizationId = ORGANIZATION_ID });

        _ = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Theory]
    [InlineData(typeof(CreditAccount))]
    [InlineData(typeof(CreditPack))]
    [InlineData(typeof(CreditEntry))]
    public void ALedgerTable_HasNoForeignKeyOutsideTheLedger(Type ledgerTable)
    {
        using UserDataDbContext db = SqliteModelContexts.CreateUserDataContext();
        Type[] ledger = [typeof(CreditAccount), typeof(CreditPack), typeof(CreditEntry)];

        IEntityType entityType = db.Model.FindEntityType(ledgerTable)!;

        Assert.All(entityType.GetForeignKeys(), foreignKey => Assert.Contains(foreignKey.PrincipalEntityType.ClrType, ledger));
        Assert.All(entityType.GetReferencingForeignKeys(), foreignKey => Assert.Contains(foreignKey.DeclaringEntityType.ClrType, ledger));
    }

    private SqliteUserDataDbContext NewContext()
    {
        return new SqliteUserDataDbContext(_database.Options);
    }
}
