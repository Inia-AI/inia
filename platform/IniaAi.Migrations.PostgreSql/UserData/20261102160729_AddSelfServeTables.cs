using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class AddSelfServeTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostBootId",
                schema: "UserData",
                table: "WorkflowExecutions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                schema: "UserData",
                table: "UserOrganizations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelfServe",
                schema: "UserData",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Consents",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GivenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ConfirmationSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditAccounts",
                schema: "UserData",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Available = table.Column<int>(type: "integer", nullable: false),
                    Pending = table.Column<int>(type: "integer", nullable: false),
                    Debt = table.Column<int>(type: "integer", nullable: false),
                    LockVersion = table.Column<long>(type: "bigint", nullable: false),
                    SuspendedReason = table.Column<int>(type: "integer", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccounts", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "CreditPacks",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Remaining = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProviderInvoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    InvoiceAmountPaid = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Reversed = table.Column<int>(type: "integer", nullable: false),
                    DebtRepaid = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditPacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailCodeLockouts",
                schema: "UserData",
                columns: table => new
                {
                    AddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCodeLockouts", x => x.AddressHash);
                });

            migrationBuilder.CreateTable(
                name: "EmailCodeRequests",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClientAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCodeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HelpRequests",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OperatorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostBoots",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Application = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Machine = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostBoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LiveOrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportGrants",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    HelpRequestId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    GrantedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OperatorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditEntries",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreditPackId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    AvailableBefore = table.Column<int>(type: "integer", nullable: false),
                    AvailableAfter = table.Column<int>(type: "integer", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PaymentEventId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    HoldEntryId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditEntries_CreditEntries_HoldEntryId",
                        column: x => x.HoldEntryId,
                        principalSchema: "UserData",
                        principalTable: "CreditEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditEntries_CreditPacks_CreditPackId",
                        column: x => x.CreditPackId,
                        principalSchema: "UserData",
                        principalTable: "CreditPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HelpRequestReplies",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    HelpRequestId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequestReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpRequestReplies_HelpRequests_HelpRequestId",
                        column: x => x.HelpRequestId,
                        principalSchema: "UserData",
                        principalTable: "HelpRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportAccesses",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SupportGrantId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OperatorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportAccesses_SupportGrants_SupportGrantId",
                        column: x => x.SupportGrantId,
                        principalSchema: "UserData",
                        principalTable: "SupportGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_HostBootId",
                schema: "UserData",
                table: "WorkflowExecutions",
                column: "HostBootId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_CheckoutSessionId",
                schema: "UserData",
                table: "Consents",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_OrganizationId",
                schema: "UserData",
                table: "Consents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_UserId",
                schema: "UserData",
                table: "Consents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_CreditPackId",
                schema: "UserData",
                table: "CreditEntries",
                column: "CreditPackId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_HoldEntryId",
                schema: "UserData",
                table: "CreditEntries",
                column: "HoldEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "CreditEntries",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_OrganizationId_Status",
                schema: "UserData",
                table: "CreditEntries",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_PaymentEventId",
                schema: "UserData",
                table: "CreditEntries",
                column: "PaymentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_WorkflowExecutionId",
                schema: "UserData",
                table: "CreditEntries",
                column: "WorkflowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPacks_OrganizationId_ExpiresAt",
                schema: "UserData",
                table: "CreditPacks",
                columns: new[] { "OrganizationId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditPacks_PaymentProvider_ProviderInvoiceId",
                schema: "UserData",
                table: "CreditPacks",
                columns: new[] { "PaymentProvider", "ProviderInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailCodeRequests_AddressHash_CreatedAt",
                schema: "UserData",
                table: "EmailCodeRequests",
                columns: new[] { "AddressHash", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequestReplies_HelpRequestId_CreatedAt",
                schema: "UserData",
                table: "HelpRequestReplies",
                columns: new[] { "HelpRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "HelpRequests",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_Status",
                schema: "UserData",
                table: "HelpRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostBoots_LastSeenAt",
                schema: "UserData",
                table: "HostBoots",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_OrganizationId",
                schema: "UserData",
                table: "PaymentEvents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentProvider_ProviderEventId",
                schema: "UserData",
                table: "PaymentEvents",
                columns: new[] { "PaymentProvider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ProcessedAt_ReceivedAt",
                schema: "UserData",
                table: "PaymentEvents",
                columns: new[] { "ProcessedAt", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_OrganizationId",
                schema: "UserData",
                table: "Subscriptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentProvider_LiveOrganizationId",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "PaymentProvider", "LiveOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentProvider_ProviderSubscriptionId",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "PaymentProvider", "ProviderSubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccesses_SupportGrantId_ReadAt",
                schema: "UserData",
                table: "SupportAccesses",
                columns: new[] { "SupportGrantId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportGrants_HelpRequestId",
                schema: "UserData",
                table: "SupportGrants",
                column: "HelpRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportGrants_OrganizationId_ExpiresAt",
                schema: "UserData",
                table: "SupportGrants",
                columns: new[] { "OrganizationId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consents",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CreditAccounts",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CreditEntries",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EmailCodeLockouts",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EmailCodeRequests",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HelpRequestReplies",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HostBoots",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "PaymentEvents",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "SupportAccesses",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CreditPacks",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HelpRequests",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "SupportGrants",
                schema: "UserData");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowExecutions_HostBootId",
                schema: "UserData",
                table: "WorkflowExecutions");

            migrationBuilder.DropColumn(
                name: "HostBootId",
                schema: "UserData",
                table: "WorkflowExecutions");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                schema: "UserData",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "IsSelfServe",
                schema: "UserData",
                table: "Organizations");
        }
    }
}
