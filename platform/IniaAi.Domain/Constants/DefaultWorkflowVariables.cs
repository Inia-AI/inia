namespace IniaAi.Domain.Constants;

/// <summary>
/// Constants for default workflow variables that can be added to workflows.
/// </summary>
public static class DefaultWorkflowVariables
{
    /// <summary>
    /// Replacement tag for the "Has Parent Company" dropdown variable.
    /// Options: "Yes" (displays/stores "Yes"), <see cref="HAS_PARENT_NO_VALUE"/> (internal value for "No" display, triggers section skip).
    ///
    /// <para>
    /// <strong><see cref="HAS_PARENT_NO_VALUE"/> Stop Term Convention:</strong> <see cref="HAS_PARENT_NO_VALUE"/> is the system-wide
    /// stop marker (<see cref="StopMarker"/>). When a section's TextInputTemplate evaluates to text that holds
    /// <see cref="HAS_PARENT_NO_VALUE"/> as a whole token during execution, the workflow engine will skip executing that entire
    /// section's blocks. This enables conditional workflow logic based on variable values.
    /// </para>
    ///
    /// <para>
    /// <strong>WARNING:</strong> Do NOT change the <see cref="HAS_PARENT_NO_VALUE"/> value without migrating existing workflows,
    /// as this convention is relied upon throughout the system for conditional section execution.
    /// </para>
    /// </summary>
    public const string HAS_PARENT = "hasParent";

    /// <summary>
    /// Replacement tag for the "Parent Company Name" text variable.
    /// </summary>
    public const string NAME_PARENT = "nameParent";

    /// <summary>
    /// Replacement tag for the "Parent Company URL" text variable.
    /// </summary>
    public const string PARENT_URL = "parentURL";

    /// <summary>
    /// Replacement tag for the "Parent Company Reputation" text variable.
    /// </summary>
    public const string PARENT_REPUTATION = "parentReputation";

    /// <summary>
    /// Display name for the Has Parent Company variable.
    /// </summary>
    public const string HAS_PARENT_DISPLAY_NAME = "Has Parent Company";

    /// <summary>
    /// Display name for the Parent Company Name variable.
    /// </summary>
    public const string NAME_PARENT_DISPLAY_NAME = "Parent Company Name";

    /// <summary>
    /// Display name for the Parent Company URL variable.
    /// </summary>
    public const string PARENT_URL_DISPLAY_NAME = "Parent Company URL";

    /// <summary>
    /// Display name for the Parent Company Reputation variable.
    /// </summary>
    public const string PARENT_REPUTATION_DISPLAY_NAME = "Parent Company Reputation";

    /// <summary>
    /// Description for the Has Parent Company variable.
    /// </summary>
    public const string HAS_PARENT_DESCRIPTION = "Indicates whether the organization has a parent company. Selecting 'No' will skip parent-related workflow sections.";

    /// <summary>
    /// Description for the Parent Company Name variable.
    /// </summary>
    public const string NAME_PARENT_DESCRIPTION = "The name of the parent company.";

    /// <summary>
    /// Description for the Parent Company URL variable.
    /// </summary>
    public const string PARENT_URL_DESCRIPTION = "The website URL of the parent company.";

    /// <summary>
    /// Description for the Parent Company Reputation variable.
    /// </summary>
    public const string PARENT_REPUTATION_DESCRIPTION = "Information about the parent company's reputation or standing.";

    /// <summary>
    /// Gets all parent company variable replacement tags.
    /// </summary>
    public static readonly string[] AllParentCompanyTags =
    [
        HAS_PARENT,
        NAME_PARENT,
        PARENT_URL,
        PARENT_REPUTATION
    ];

    /// <summary>
    /// Stop term value that triggers skipping workflow sections.
    /// When this value is selected, it indicates "No" and causes workflow blocks to be skipped.
    /// </summary>
    public const string HAS_PARENT_NO_VALUE = StopMarker.VALUE;

    /// <summary>
    /// Display value for "Yes" option in hasParent dropdown.
    /// </summary>
    public const string HAS_PARENT_YES_VALUE = "Yes";

    /// <summary>
    /// Display value for "No" option in hasParent dropdown.
    /// </summary>
    public const string HAS_PARENT_NO_DISPLAY = "No";

    /// <summary>
    /// Gets the dropdown options for the Has Parent Company variable.
    /// </summary>
    public static Dictionary<string, string> GetHasParentOptions()
    {
        return new()
    {
        { HAS_PARENT_YES_VALUE, HAS_PARENT_YES_VALUE },
        { HAS_PARENT_NO_VALUE, HAS_PARENT_NO_DISPLAY }
    };
    }
}
