using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Services.Tests.Evaluation.MarkerInstruction;

/// <summary>
/// Points every Web API call block of a workflow at the local stand-in before a measured run, keeping each address's path
/// and query, so the run calls no third party and the stand-in's echo shows what each call would have sent.
/// </summary>
public static class StandInHosts
{
    private const string SCHEME_SEPARATOR = "://";
    private const string WHOLE_ADDRESS_PATH = "/whole-address-placeholder";

    /// <summary>Redirects the workflow in place and returns how many Web API call blocks it changed.</summary>
    public static int Redirect(Workflow workflow, string standInBaseUrl)
    {
        int redirected = 0;
        foreach (Block block in workflow.GetAllBlocks())
        {
            if (block.Properties is not WebApiCallProperties call)
            {
                continue;
            }

            call.UrlTemplate = Redirected(call.UrlTemplate, standInBaseUrl);
            call.PollCompletionUrlTemplate = Redirected(call.PollCompletionUrlTemplate, standInBaseUrl);
            redirected++;
        }

        return redirected;
    }

    /// <summary>The template with its scheme and authority replaced by the stand-in's; a template that is one placeholder becomes a fixed stand-in path.</summary>
    public static string? Redirected(string? template, string standInBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return template;
        }

        int schemeEnd = template.IndexOf(SCHEME_SEPARATOR, StringComparison.Ordinal);
        if (schemeEnd < 0 || template[..schemeEnd].Contains('{', StringComparison.Ordinal))
        {
            return standInBaseUrl.TrimEnd('/') + WHOLE_ADDRESS_PATH;
        }

        int authorityStart = schemeEnd + SCHEME_SEPARATOR.Length;
        int pathStart = template.IndexOfAny(['/', '?'], authorityStart);
        return standInBaseUrl.TrimEnd('/') + (pathStart < 0 ? string.Empty : template[pathStart..]);
    }
}
