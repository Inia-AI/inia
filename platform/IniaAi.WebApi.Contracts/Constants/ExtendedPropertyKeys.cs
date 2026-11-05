namespace IniaAi.WebApi.Models.Constants;

/// <summary>
/// Provides a collection of constant keys representing various extended properties
/// used across different domains such as General, RMF (Risk Management Framework),
/// Corporate.
/// </summary>
public readonly struct ExtendedPropertyKeys
{
    // General

    /// <summary>
    /// The name of the analyst.
    /// </summary>
    public const string ANALYST_NAME = "AnalystName";

    /// <summary>
    /// The date of the review.
    /// </summary>
    public const string REVIEW_DATE = "ReviewDate"; // date

    // RMF

    /// <summary>
    /// The type of review, e.g., Initial or Annual.
    /// </summary>
    public const string REVIEW_TYPE = "ReviewType"; // Initial, Annual

    /// <summary>
    /// The investment advisor associated with the review.
    /// </summary>
    public const string INVESTMENT_ADVISOR = "InvestmentAdvisor";

    /// <summary>
    /// The location of the fund manager's coverage.
    /// </summary>
    public const string FUND_MANAGER_COVERAGE_LOCATION = "FundManagerCoverageLocation";

    /// <summary>
    /// The website of the fund manager.
    /// </summary>
    public const string FUND_MANAGER_WEBSITE = "FundManagerWebsite";

    /// <summary>
    /// Confidential information, represented as a list of strings.
    /// </summary>
    public const string CONFIDENTIAL = "Confidential"; // list of strings

    /// <summary>
    /// Answer from web search regarding inception.
    /// </summary>
    public const string INCEPTION_WEB_SEARCH_ANSWER = "inception_web_search_answer";

    /// <summary>
    /// Answer from web search regarding ownership.
    /// </summary>
    public const string OWNERSHIP_WEB_SEARCH_ANSWER = "ownership_web_search_answer";

    /// <summary>
    /// Answer from Google search regarding employee count.
    /// </summary>
    public const string EMPLOYEE_COUNT_GOOGLE_SEARCH_ANSWER = "employee_count_google_search_answer";

    /// <summary>
    /// Answer from Google search regarding company AUM (Assets Under Management).
    /// </summary>
    public const string COMPANY_AUM_GOOGLE_SEARCH_ANSWER = "company_aum_google_search_answer";

    // Corporate

    /// <summary>
    /// The name of the corporation.
    /// </summary>
    public const string CORPORATION = "Corporation";

    /// <summary>
    /// The industry of the corporation.
    /// </summary>
    public const string CORPORATION_INDUSTRY = "CorporationIndustry";

    /// <summary>
    /// The type of the corporation.
    /// </summary>
    public const string CORPORATION_TYPE = "CorporationType";

    /// <summary>
    /// The country where the corporation is located.
    /// </summary>
    public const string CORPORATION_COUNTRY = "CorporationCountry";

    /// <summary>
    /// The website of the corporation.
    /// </summary>
    public const string CORPORATION_WEBSITE = "CorporationWebsite";

    /// <summary>
    /// The assessment year of the corporation.
    /// </summary>
    public const string CORPORATION_ASSESSMENT_YEAR = "CorporationAssessmentYear";
}
