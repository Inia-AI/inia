using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Documents;

public class DocumentInfoTests
{

    [Fact]
    public void CanBeProcessedForBlock_SupportedDocumentClassesContainsNone_ReturnsFalse()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow()));
        block.SupportedDocumentClasses.Add("None");
        var documentInfo = new DocumentInfo();

        // Act
        bool result = documentInfo.CanBeProcessedForBlock(block);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanBeProcessedForBlock_SupportedDocumentClassesIsEmpty_ReturnsTrue()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow()));
        var documentInfo = new DocumentInfo();

        // Act
        bool result = documentInfo.CanBeProcessedForBlock(block);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanBeProcessedForBlock_SupportedDocumentClassesContainsAny_ReturnsTrue()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow()));
        block.SupportedDocumentClasses.Add("Any");
        var documentInfo = new DocumentInfo();

        // Act
        bool result = documentInfo.CanBeProcessedForBlock(block);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanBeProcessedForBlock_SupportedDocumentClassesContainsDocumentClass_ReturnsTrue()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow()));
        block.SupportedDocumentClasses.Add("DocumentClass");
        var documentInfo = new DocumentInfo
        {
            DocumentClass = "DocumentClass"
        };

        // Act
        bool result = documentInfo.CanBeProcessedForBlock(block);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanBeProcessedForBlock_ListTitleIsNull_ReturnsTrue()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow()));
        var documentInfo = new DocumentInfo();

        // Act
        bool result = documentInfo.CanBeProcessedForBlock(block);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ExcludeFromRun_StampsTheTimeAndTheReason_AndKeepsTheJob()
    {
        var documentInfo = new DocumentInfo("document-1") { JobId = "job-1" };
        var excludedAt = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

        documentInfo.ExcludeFromRun("Unreadable", excludedAt);

        Assert.Equal(excludedAt, documentInfo.ExcludedAt);
        Assert.Equal("Unreadable", documentInfo.ExclusionReason);
        Assert.True(documentInfo.IsExcludedFromRun);
        Assert.Equal("job-1", documentInfo.JobId);
    }

    [Fact]
    public void ANewDocumentInfo_IsRunInput()
    {
        var documentInfo = new DocumentInfo("document-1");

        Assert.Null(documentInfo.ExcludedAt);
        Assert.Null(documentInfo.ExclusionReason);
        Assert.False(documentInfo.IsExcludedFromRun);
    }
}
