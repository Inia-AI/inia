using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Core.Dto.Exa.Search.Enums;

public enum ExaSearchCategory
{
    /// <summary>
    /// Searches for company-related content
    /// </summary>
    [EnumMember(Value = "company")]
    [Description("Searches for company-related content")]
    Company,

    /// <summary>
    /// Searches for academic research papers
    /// </summary>
    [EnumMember(Value = "research paper")]
    [Description("Searches for academic research papers")]
    ResearchPaper,

    /// <summary>
    /// Searches for news articles and updates
    /// </summary>
    [EnumMember(Value = "news")]
    [Description("Searches for news articles and updates")]
    News,

    /// <summary>
    /// Searches for PDF documents
    /// </summary>
    [EnumMember(Value = "pdf")]
    [Description("Searches for PDF documents")]
    Pdf,

    /// <summary>
    /// Searches for GitHub repositories and issues
    /// </summary>
    [EnumMember(Value = "github")]
    [Description("Searches for GitHub repositories and issues")]
    Github,

    /// <summary>
    /// Searches for tweets and Twitter content
    /// </summary>
    [EnumMember(Value = "tweet")]
    [Description("Searches for tweets and Twitter content")]
    Tweet,

    /// <summary>
    /// Searches for personal websites and portfolios
    /// </summary>
    [EnumMember(Value = "personal site")]
    [Description("Searches for personal websites and portfolios")]
    PersonalSite,

    /// <summary>
    /// Searches for LinkedIn profiles
    /// </summary>
    [EnumMember(Value = "linkedin profile")]
    [Description("Searches for LinkedIn profiles")]
    LinkedinProfile,

    /// <summary>
    /// Searches for financial reports and documents
    /// </summary>
    [EnumMember(Value = "financial report")]
    [Description("Searches for financial reports and documents")]
    FinancialReport
}
