using IniaAi.Application.Common.CreditCosts;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The workflows a finished run leads to, read from the Made by links: the current workflows that have an input made
/// by the run's workflow, that the organization can reach and can start now. Each takes the run's output that its
/// input names, and the run's inputs whose class it also has.
/// </summary>
public sealed class NextWorkflows
{
    private readonly ICoreUnitOfWork _core;
    private readonly IWorkflowAvailability _availability;
    private readonly IWorkflowCreditCost _creditCost;

    public NextWorkflows(ICoreUnitOfWork core, IWorkflowAvailability availability, IWorkflowCreditCost creditCost)
    {
        _core = core;
        _availability = availability;
        _creditCost = creditCost;
    }

    /// <param name="documents">The documents of the run that can still be attached: neither taken out of the run nor deleted.</param>
    public async Task<List<NextWorkflowDto>> OfAsync(
        string organizationId,
        string makerWorkflowTypeId,
        IReadOnlyList<RunDocument> documents,
        CancellationToken cancellationToken)
    {
        List<string> outputKeys = [.. documents.Where(document => document.IsOutput).Select(document => document.DocumentClass).Distinct(StringComparer.Ordinal)];
        if (outputKeys.Count == 0)
        {
            return [];
        }

        List<NextWorkflowDto> next = [];
        foreach (MadeByReader reader in await MadeByLinks.ReadersOfAsync(_core, makerWorkflowTypeId, outputKeys, organizationId, cancellationToken))
        {
            List<MadeByReaderInput> madeInputs = [.. reader.Inputs.Where(input => input.IsMadeBy(makerWorkflowTypeId)).DistinctBy(input => input.Key, StringComparer.Ordinal)];
            List<NextWorkflowDocumentDto> made = [.. madeInputs.SelectMany(input => MadeFor(input, documents))];
            if (made.Count == 0 || !await _availability.CanStartRunAsync(organizationId, reader.Configuration, cancellationToken))
            {
                continue;
            }

            HashSet<string> shared = [.. reader.Inputs.Select(input => input.Key).Except(madeInputs.Select(input => input.Key), StringComparer.Ordinal)];
            next.Add(new NextWorkflowDto
            {
                WorkflowId = reader.WorkflowId,
                WorkflowTypeId = reader.WorkflowTypeId,
                WorkflowTypeName = reader.WorkflowTypeName,
                CreditCost = _creditCost.Of(reader.Configuration),
                Documents =
                [
                    .. made,
                    .. documents
                        .Where(document => !document.IsOutput && shared.Contains(document.DocumentClass))
                        .Select(document => Attached(document, document.DocumentClass)),
                ],
            });
        }

        return next;
    }

    /// <summary>
    /// The run's files of the output that the input names. Where the run made a workbook of the output, only the
    /// workbooks go: a copy of the same output in another format is not what the next workflow reads.
    /// </summary>
    private static IEnumerable<NextWorkflowDocumentDto> MadeFor(MadeByReaderInput input, IReadOnlyList<RunDocument> documents)
    {
        List<RunDocument> outputs = [.. documents.Where(document => document.IsOutput
            && string.Equals(document.DocumentClass, input.MadeByDocumentClassKey, StringComparison.Ordinal))];
        bool hasWorkbook = outputs.Any(output => output.IsWorkbook);
        return outputs.Where(output => output.IsWorkbook || !hasWorkbook).Select(output => Attached(output, input.Key));
    }

    private static NextWorkflowDocumentDto Attached(RunDocument document, string documentClass)
    {
        return new NextWorkflowDocumentDto
        {
            FileId = document.FileId,
            Name = document.Name,
            Extension = document.Extension,
            DocumentClass = documentClass,
        };
    }

    /// <summary>A document of the run, with the class it had in the run.</summary>
    public sealed record RunDocument(string FileId, string Name, string Extension, string DocumentClass, bool IsOutput, bool IsWorkbook);
}
