using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Documents;

public class DocumentConstructionTests
{
    [Fact]
    public void ADocumentBornFromDetectedBytes_CarriesThatType_WhateverTheNameSays()
    {
        var fileMetadata = new FileMetadata("org", "report.exe", 128, StorageProvider.Database);

        var document = new Document(fileMetadata, MediaType.ApplicationPdf);

        Assert.Equal(MediaType.ApplicationPdf, document.MediaType);
        Assert.Equal(document.Id, fileMetadata.DocumentId);
    }
}
