using IniaAi.FileFormats.ContractTests.Unlock;
using IniaAi.FileFormats.Unlock;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenDocumentUnlockerContractTests : DocumentUnlockerContract
{
    protected override IDocumentUnlocker Unlocker { get; } = new OpenDocumentUnlocker();
}
