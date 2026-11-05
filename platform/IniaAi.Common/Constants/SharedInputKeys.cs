namespace IniaAi.Common.Constants;

public struct SharedInputKeys
{
    public const string ORGANIZATION_ID = "OrganizationId";
    public const string COUNTERPARTIES_LIST = "FundsListFromDb";
    public const string PARENT_COUNTERPARTY = "base_info_fund_manager";
    public const string REPORT_DATE = "ReportDate";
    public const string JOB_ID = "JobId";
    public const string COUNTERPARTY = "Counterparty";
    public const string PEER_COMPANIES = "PeerCompanies";
    public const string ORGANIZATION = "Organization";
    public const string INPUT_CONTEXT = "InputContext";
    public const string INPUT_CONTEXT_OVERVIEWS = "InputContext.Overviews";
    public const string TRIGGER_EMAIL_IDENTIFIER = "TriggerEmailIdentifier";
    public const string STORED_KEY = "StoredKey";

    public static readonly IReadOnlyList<string> ReservedReplacementTags =
    [
        ORGANIZATION_ID,
        COUNTERPARTIES_LIST,
        PARENT_COUNTERPARTY,
        REPORT_DATE,
        JOB_ID,
        COUNTERPARTY,
        PEER_COMPANIES,
        ORGANIZATION,
        INPUT_CONTEXT,
        INPUT_CONTEXT_OVERVIEWS,
        TRIGGER_EMAIL_IDENTIFIER,
        STORED_KEY
    ];
}
