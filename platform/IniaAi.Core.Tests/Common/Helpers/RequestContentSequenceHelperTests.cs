using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common;
using IniaAi.Core.Common.Helpers;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Document = IniaAi.Domain.Entities.Documents.Document;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;
using Page = IniaAi.Domain.Entities.Documents.Page;

namespace IniaAi.Core.Tests.Common.Helpers;

public class RequestContentSequenceHelperTests
{
    private const string CitationInstruction = RequestContentSequenceHelper.CITATIONS_APPENDAGE;

    /// <summary>
    /// Computes the expected plain text by naively replacing all non-empty placeholders
    /// in the template. The result should match <see cref="RequestContentSequence.GetPlainText"/>.
    /// </summary>
    private static string NaiveReplace(string template, Dictionary<string, string?> textInputs, bool shouldGetCitationsFromModel = false)
    {
        string result = template;
        foreach ((string key, string? value) in textInputs)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // Replace double braces first to avoid partial matches, case-insensitive
            result = result
                .Replace($"{{{{{key}}}}}", value, StringComparison.InvariantCultureIgnoreCase)
                .Replace($"{{{key}}}", value, StringComparison.InvariantCultureIgnoreCase);
        }

        result = result.Trim();

        if (shouldGetCitationsFromModel)
        {
            result += CitationInstruction;
        }

        return result;
    }

    #region Null and Empty Template Tests

    [Fact]
    public void BuildUserPromptContentSequence_NullTemplate_ReturnsNull()
    {
        // Arrange
        string? template = null;
        var textInputs = new Dictionary<string, string?>();
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BuildUserPromptContentSequence_EmptyTemplate_ReturnsNull()
    {
        // Arrange
        string template = string.Empty;
        var textInputs = new Dictionary<string, string?>();
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BuildUserPromptContentSequence_WhitespaceOnlyTemplate_ReturnsNull()
    {
        // Arrange
        string template = "   \n\t  ";
        var textInputs = new Dictionary<string, string?>();
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Simple Template Tests (No Placeholders)

    [Fact]
    public void BuildUserPromptContentSequence_TemplateWithNoPlaceholders_ReturnsSequenceWithText()
    {
        // Arrange
        string template = "This is a simple prompt with no placeholders.";
        var textInputs = new Dictionary<string, string?>();
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        _ = Assert.Single(items);
        Assert.Equal(template, items[0].TextContent);
        Assert.False(items[0].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    #endregion

    #region Non-Block Placeholder Tests

    [Fact]
    public void BuildUserPromptContentSequence_TemplateWithNonBlockPlaceholders_ReplacesPlaceholders()
    {
        // Arrange
        string template = "Hello {Name}, your company is {Company}.";
        var textInputs = new Dictionary<string, string?>
        {
            ["Name"] = "John",
            ["Company"] = "Acme Corp"
        };
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        _ = Assert.Single(items);
        Assert.Equal("Hello John, your company is Acme Corp.", items[0].TextContent);
        Assert.False(items[0].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_TemplateWithDoubleBracePlaceholders_ReplacesPlaceholders()
    {
        // Arrange
        string template = "Hello {{Name}}, welcome!";
        var textInputs = new Dictionary<string, string?>
        {
            ["Name"] = "Jane"
        };
        var referencedBlocks = new Dictionary<string, Block>();

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        _ = Assert.Single(items);
        Assert.Equal("Hello Jane, welcome!", items[0].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    #endregion

    #region Block Placeholder Tests

    [Fact]
    public void BuildUserPromptContentSequence_TemplateWithBlockPlaceholder_CreatesBlockReferenceContent()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Analysis Block" };

        string template = "Based on the analysis: {block1}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "This is the block output content."
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.Equal(3, items.Count);

        // First item: text before placeholder
        Assert.Equal("Based on the analysis: ", items[0].TextContent);
        Assert.False(items[0].IsBlockReference);

        // Second item: block reference content
        Assert.Equal("This is the block output content.", items[1].TextContent);
        Assert.True(items[1].IsBlockReference);
        Assert.Equal("Analysis Block", items[1].Title);
        Assert.Equal(block.Id, items[1].ReferenceId);
        Assert.Equal("Output from block Analysis Block", items[1].Context);

        // Third item: citation instruction
        Assert.Equal(CitationInstruction, items[2].TextContent);
        Assert.False(items[2].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderIsNotInTheUserPromptTemplate_CreatesNoBlockReference()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Analysis Block" };

        string template = "This prompt has no block placeholders.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "This is the block output content."
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        _ = Assert.Single(items);
        Assert.Equal(template + CitationInstruction, items[0].TextContent);
        Assert.False(items[0].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_DoubleBraceBlockPlaceholder_CreatesBlockReferenceContent()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Analysis Block" };

        string template = "Based on the analysis: {{block1}}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "This is the block output content."
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.Equal(3, items.Count);

        // First item: text before placeholder
        Assert.Equal("Based on the analysis: ", items[0].TextContent);
        Assert.False(items[0].IsBlockReference);

        // Second item: block reference content
        Assert.Equal("This is the block output content.", items[1].TextContent);
        Assert.True(items[1].IsBlockReference);
        Assert.Equal("Analysis Block", items[1].Title);

        // Third item: citation instruction
        Assert.Equal(CitationInstruction, items[2].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_DoubleBraceBlockPlaceholderNotInTemplate_CreatesNoBlockReference()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Analysis Block" };

        string template = "This prompt has no block placeholders.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "This is the block output content."
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act - same scenario as single brace test, verifying double brace check works
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        _ = Assert.Single(items);
        Assert.Equal(template + CitationInstruction, items[0].TextContent);
        Assert.False(items[0].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_SamePlaceholderAppearsTwice_BothAreReplaced()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Data Block" };

        string template = "First: {block1}. Second: {block1}.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // The raw placeholder text should NOT appear in any output
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{block1}", fullOutput);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_SameDoubleBracePlaceholderAppearsTwice_BothAreReplaced()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Data Block" };

        string template = "First: {{block1}}. Second: {{block1}}.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // The raw placeholder text should NOT appear in any output
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{{block1}}", fullOutput);
        Assert.DoesNotContain("{block1}", fullOutput);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_MixedSingleAndDoubleBraces_BothAreReplaced()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Data Block" };

        string template = "Single: {block1}. Double: {{block1}}.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Neither brace style should remain
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{{block1}}", fullOutput);
        Assert.DoesNotContain("{block1}", fullOutput);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_CaseMismatchInPlaceholder_StillReplaces()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Data Block" };

        // Template uses different casing than the key in dictionaries
        string template = "Content: {BLOCK1}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Placeholder should be replaced (case-insensitive)
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{BLOCK1}", fullOutput);
        Assert.Contains("Block content", fullOutput);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockWithExecutionStateOutputTitle_UsesOutputTitle()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Block Name" };
        block.ExecutionState = new BlockExecutionState(block) { OutputTitle = "Custom Output Title" };

        string template = "Content: {block1}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        RequestCommonContent blockContent = items.First(i => i.IsBlockReference);
        Assert.Equal("Custom Output Title", blockContent.Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockWithNoName_UsesPlaceholderAsTitle()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "myBlock") { Name = "" };

        string template = "Output: {myBlock}";
        var textInputs = new Dictionary<string, string?>
        {
            ["myBlock"] = "Block output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["myBlock"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        RequestCommonContent blockContent = items.First(i => i.IsBlockReference);
        Assert.Equal("myBlock", blockContent.Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderWithEmptyInput_SkipsBlock()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Block 1" };

        string template = "Intro {block1} outro";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = ""
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        // Should have only the text content, no block reference
        Assert.DoesNotContain(items, i => i.IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderWithNullInput_SkipsBlock()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Block 1" };

        string template = "Intro {block1} outro";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = null
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.DoesNotContain(items, i => i.IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderNotInTextInputs_SkipsBlock()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Block 1" };

        string template = "Intro {block1} outro";
        var textInputs = new Dictionary<string, string?>(); // No entry for block1
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.DoesNotContain(items, i => i.IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    #endregion

    #region Multiple Block Placeholders Tests

    [Fact]
    public void BuildUserPromptContentSequence_MultipleBlockPlaceholders_CreatesMultipleBlockReferences()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };
        var block2 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };

        string template = "First: {analysis} Second: {summary}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block1,
            ["summary"] = block2
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(2, blockRefs.Count);
        Assert.Equal("Analysis output", blockRefs[0].TextContent);
        Assert.Equal("Summary output", blockRefs[1].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_TemplatePlaceholderOrderDiffersFromDictionaryOrder_ReplacesAllPlaceholders()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };
        var block2 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };

        // Template has summary first, then analysis
        string template = "Summary: {summary} Analysis: {analysis}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        // Dictionary has analysis first, then summary — opposite of template order
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block2,
            ["summary"] = block1
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Both placeholders should be fully replaced
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{summary}", fullOutput);
        Assert.DoesNotContain("{analysis}", fullOutput);

        // Both block references should be present
        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(2, blockRefs.Count);

        // Output should follow the template order: summary content before analysis content
        Assert.Equal("Summary output", blockRefs[0].TextContent);
        Assert.Equal("Summary", blockRefs[0].Title);
        Assert.Equal("Analysis output", blockRefs[1].TextContent);
        Assert.Equal("Analysis", blockRefs[1].Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_DoubleBracePlaceholderOrderDiffersFromDictionaryOrder_ReplacesAllPlaceholders()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };
        var block2 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };

        // Template has summary first, then analysis — both double braces
        string template = "Summary: {{summary}} Analysis: {{analysis}}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        // Dictionary has analysis first, then summary — opposite of template order
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block2,
            ["summary"] = block1
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{{summary}}", fullOutput);
        Assert.DoesNotContain("{{analysis}}", fullOutput);
        Assert.DoesNotContain("{summary}", fullOutput);
        Assert.DoesNotContain("{analysis}", fullOutput);

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(2, blockRefs.Count);

        Assert.Equal("Summary output", blockRefs[0].TextContent);
        Assert.Equal("Summary", blockRefs[0].Title);
        Assert.Equal("Analysis output", blockRefs[1].TextContent);
        Assert.Equal("Analysis", blockRefs[1].Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_MixedBracesOrderDiffersFromDictionaryOrder_SingleBraceFirst_ReplacesAllPlaceholders()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };
        var block2 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };

        // Template: single brace for summary, double brace for analysis
        string template = "Summary: {summary} Analysis: {{analysis}}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block2,
            ["summary"] = block1
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{summary}", fullOutput);
        Assert.DoesNotContain("{{analysis}}", fullOutput);
        Assert.DoesNotContain("{analysis}", fullOutput);

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(2, blockRefs.Count);

        Assert.Equal("Summary output", blockRefs[0].TextContent);
        Assert.Equal("Summary", blockRefs[0].Title);
        Assert.Equal("Analysis output", blockRefs[1].TextContent);
        Assert.Equal("Analysis", blockRefs[1].Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_MixedBracesOrderDiffersFromDictionaryOrder_DoubleBraceFirst_ReplacesAllPlaceholders()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };
        var block2 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };

        // Template: double brace for summary, single brace for analysis
        string template = "Summary: {{summary}} Analysis: {analysis}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block2,
            ["summary"] = block1
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{{summary}}", fullOutput);
        Assert.DoesNotContain("{summary}", fullOutput);
        Assert.DoesNotContain("{analysis}", fullOutput);

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(2, blockRefs.Count);

        Assert.Equal("Summary output", blockRefs[0].TextContent);
        Assert.Equal("Summary", blockRefs[0].Title);
        Assert.Equal("Analysis output", blockRefs[1].TextContent);
        Assert.Equal("Analysis", blockRefs[1].Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_ThreeBlocksReversedDictionaryOrder_ReplacesAllInTemplateOrder()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var blockA = new Block(BlockTypes.Merge, workflow, "alpha") { Name = "Alpha" };
        var blockB = new Block(BlockTypes.Merge, workflow, "beta") { Name = "Beta" };
        var blockC = new Block(BlockTypes.Merge, workflow, "gamma") { Name = "Gamma" };

        // Template order: alpha, beta, gamma — mixed brace styles
        string template = "A: {alpha} B: {{beta}} C: {gamma}";
        var textInputs = new Dictionary<string, string?>
        {
            ["gamma"] = "Gamma output",
            ["beta"] = "Beta output",
            ["alpha"] = "Alpha output"
        };
        // Dictionary order: gamma, beta, alpha — fully reversed
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["gamma"] = blockC,
            ["beta"] = blockB,
            ["alpha"] = blockA
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{alpha}", fullOutput);
        Assert.DoesNotContain("{beta}", fullOutput);
        Assert.DoesNotContain("{{beta}}", fullOutput);
        Assert.DoesNotContain("{gamma}", fullOutput);

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(3, blockRefs.Count);

        Assert.Equal("Alpha output", blockRefs[0].TextContent);
        Assert.Equal("Alpha", blockRefs[0].Title);
        Assert.Equal("Beta output", blockRefs[1].TextContent);
        Assert.Equal("Beta", blockRefs[1].Title);
        Assert.Equal("Gamma output", blockRefs[2].TextContent);
        Assert.Equal("Gamma", blockRefs[2].Title);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Theory]
    [InlineData(42)]
    [InlineData(123)]
    [InlineData(7)]
    [InlineData(999)]
    [InlineData(0)]
    public void BuildUserPromptContentSequence_RealisticTemplate_RandomizedDictionaryOrder_ReplacesAllInTemplateOrder(int seed)
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var blockDefs = new (string key, string name, string output, string braces)[]
        {
            ("executiveSummary", "Executive Summary", "The company showed strong Q3 performance with revenue up 12% YoY.", "single"),
            ("marketAnalysis", "Market Analysis", "Global market conditions remain favorable with emerging opportunities in APAC.", "double"),
            ("financialOverview", "Financial Overview", "Revenue: $45.2M, EBITDA: $12.8M, Net Income: $8.1M.", "single"),
            ("riskAssessment", "Risk Assessment", "Key risks include regulatory changes in EU markets and supply chain disruptions.", "double"),
            ("competitorBenchmark", "Competitor Benchmark", "Company outperforms peers on margins but lags in market share growth.", "single"),
            ("operationalMetrics", "Operational Metrics", "Employee retention at 94%, customer NPS improved to 72.", "double"),
            ("strategicOutlook", "Strategic Outlook", "Management targets 15% growth through M&A and organic expansion.", "single"),
            ("complianceNotes", "Compliance Notes", "All regulatory filings current. No outstanding audit findings.", "double"),
        };

        string template =
            """
            INVESTMENT MEMO — {companyName} ({ticker})
            Prepared by: {analystName}
            Date: {reportDate}

            1. EXECUTIVE SUMMARY
            {executiveSummary}

            2. MARKET ANALYSIS
            {{marketAnalysis}}

            3. FINANCIAL OVERVIEW
            {financialOverview}

            4. RISK ASSESSMENT
            {{riskAssessment}}

            5. COMPETITOR BENCHMARK
            {competitorBenchmark}

            6. OPERATIONAL METRICS
            {{operationalMetrics}}

            7. STRATEGIC OUTLOOK
            {strategicOutlook}

            8. COMPLIANCE
            {{complianceNotes}}

            Disclaimer: This report was prepared for {clientName} by {analystName}.
            """;

        var textInputs = new Dictionary<string, string?>
        {
            ["companyName"] = "Acme Corp",
            ["ticker"] = "ACME",
            ["analystName"] = "Jane Smith",
            ["reportDate"] = "2025-01-15",
            ["clientName"] = "BigFund Capital",
        };
        foreach ((string key, _, string output, _) in blockDefs)
        {
            textInputs[key] = output;
        }

        // Shuffle block entries using the seed for reproducibility
        var rng = new Random(seed);
        (string key, string name, string output, string braces)[] shuffled = [.. blockDefs];
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var referencedBlocks = new Dictionary<string, Block>();
        foreach ((string key, string name, _, _) in shuffled)
        {
            referencedBlocks[key] = new Block(BlockTypes.Merge, workflow, key) { Name = name };
        }

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        string? fullOutput = result.GetPlainText();

        // No raw placeholders should remain
        foreach ((string key, _, _, _) in blockDefs)
        {
            Assert.DoesNotContain($"{{{key}}}", fullOutput);
            Assert.DoesNotContain($"{{{{{key}}}}}", fullOutput);
        }

        // Non-block placeholders should be replaced inline
        Assert.Contains("Acme Corp", fullOutput);
        Assert.Contains("ACME", fullOutput);
        Assert.Contains("Jane Smith", fullOutput);
        Assert.Contains("BigFund Capital", fullOutput);

        // All block outputs should be present as block references in template order
        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(blockDefs.Length, blockRefs.Count);

        for (int i = 0; i < blockDefs.Length; i++)
        {
            Assert.Equal(blockDefs[i].output, blockRefs[i].TextContent);
            Assert.Equal(blockDefs[i].name, blockRefs[i].Title);
        }

        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), fullOutput);
    }

    [Fact]
    public void BuildUserPromptContentSequence_InterleavedPlaceholders_ReplacesAllInLeftToRightOrder()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var blockA = new Block(BlockTypes.Merge, workflow, "a") { Name = "Block A" };
        var blockB = new Block(BlockTypes.Merge, workflow, "b") { Name = "Block B" };

        // {a} appears twice with {b} in between
        string template = "Start {a} middle {b} end {a} done";
        var textInputs = new Dictionary<string, string?>
        {
            ["a"] = "AAA",
            ["b"] = "BBB"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["a"] = blockA,
            ["b"] = blockB
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // No raw placeholders should remain
        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{a}", fullOutput);
        Assert.DoesNotContain("{b}", fullOutput);

        // Three block references in template order: a, b, a
        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(3, blockRefs.Count);
        Assert.Equal("AAA", blockRefs[0].TextContent);
        Assert.Equal("Block A", blockRefs[0].Title);
        Assert.Equal("BBB", blockRefs[1].TextContent);
        Assert.Equal("Block B", blockRefs[1].Title);
        Assert.Equal("AAA", blockRefs[2].TextContent);
        Assert.Equal("Block A", blockRefs[2].Title);

        // Text segments between placeholders
        var textItems = items.Where(i => !i.IsBlockReference).ToList();
        Assert.Contains(textItems, t => t.TextContent == "Start ");
        Assert.Contains(textItems, t => t.TextContent == " middle ");
        Assert.Contains(textItems, t => t.TextContent == " end ");
        Assert.Contains(textItems, t => t.TextContent == " done" + CitationInstruction);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_InterleavedWithMixedBraces_ReplacesAllInLeftToRightOrder()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var blockA = new Block(BlockTypes.Merge, workflow, "a") { Name = "Block A" };
        var blockB = new Block(BlockTypes.Merge, workflow, "b") { Name = "Block B" };

        // Mix of single and double braces, interleaved
        string template = "Start {a} middle {{b}} end {{a}} done";
        var textInputs = new Dictionary<string, string?>
        {
            ["a"] = "AAA",
            ["b"] = "BBB"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["b"] = blockB,
            ["a"] = blockA
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        string fullOutput = string.Join("", items.Select(i => i.TextContent));
        Assert.DoesNotContain("{a}", fullOutput);
        Assert.DoesNotContain("{b}", fullOutput);
        Assert.DoesNotContain("{{a}}", fullOutput);
        Assert.DoesNotContain("{{b}}", fullOutput);

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(3, blockRefs.Count);
        Assert.Equal("AAA", blockRefs[0].TextContent);
        Assert.Equal("BBB", blockRefs[1].TextContent);
        Assert.Equal("AAA", blockRefs[2].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderAtStart_HandlesCorrectly()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "First Block" };

        string template = "{block1} followed by text.";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.Equal(2, items.Count);

        // First item: block reference (no text before)
        Assert.True(items[0].IsBlockReference);
        Assert.Equal("Block content", items[0].TextContent);

        // Second item: trailing text with citation instruction
        Assert.False(items[1].IsBlockReference);
        Assert.Equal(" followed by text." + CitationInstruction, items[1].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_BlockPlaceholderAtEnd_HandlesCorrectly()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Last Block" };

        string template = "Text before: {block1}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "Block content"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        Assert.Equal(3, items.Count);

        // First item: text before
        Assert.False(items[0].IsBlockReference);
        Assert.Equal("Text before: ", items[0].TextContent);

        // Second item: block reference
        Assert.True(items[1].IsBlockReference);
        Assert.Equal("Block content", items[1].TextContent);

        // Third item: citation instruction
        Assert.Equal(CitationInstruction, items[2].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    #endregion

    #region Mixed Placeholders Tests

    [Fact]
    public void BuildUserPromptContentSequence_MixedPlaceholders_ReplacesNonBlockAndCreatesBlockReferences()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "data") { Name = "Data Block" };

        string template = "Hello {userName}, here is your data: {data}. Have a nice day!";
        var textInputs = new Dictionary<string, string?>
        {
            ["userName"] = "Alice",
            ["data"] = "The data content from the block"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["data"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Should have: "Hello Alice, here is your data: " (text) + block content + ". Have a nice day!" (text)
        Assert.Equal(3, items.Count);

        Assert.Equal("Hello Alice, here is your data: ", items[0].TextContent);
        Assert.False(items[0].IsBlockReference);

        Assert.Equal("The data content from the block", items[1].TextContent);
        Assert.True(items[1].IsBlockReference);
        Assert.Equal("Data Block", items[1].Title);

        Assert.Equal(". Have a nice day!" + CitationInstruction, items[2].TextContent);
        Assert.False(items[2].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_CaseInsensitivePlaceholders_MatchesCorrectly()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "MyBlock") { Name = "My Block" };

        string template = "Content: {myblock}";
        var textInputs = new Dictionary<string, string?>
        {
            ["MyBlock"] = "Block output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["MyBlock"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();
        RequestCommonContent? blockContent = items.FirstOrDefault(i => i.IsBlockReference);
        Assert.NotNull(blockContent);
        Assert.Equal("Block output", blockContent.TextContent);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    #endregion

    #region Complex Template Tests

    [Fact]
    public void BuildUserPromptContentSequence_ComplexTemplate_ProcessesCorrectly()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "intro") { Name = "Introduction" };
        var block2 = new Block(BlockTypes.Merge, workflow, "details") { Name = "Details" };
        var block3 = new Block(BlockTypes.Merge, workflow, "conclusion") { Name = "Conclusion" };

        string template = """
            Report for {companyName}

            Introduction:
            {intro}

            Details:
            {details}

            Conclusion:
            {conclusion}

            Prepared by {author}
            """;

        var textInputs = new Dictionary<string, string?>
        {
            ["companyName"] = "TechCorp",
            ["intro"] = "This is the introduction section.",
            ["details"] = "These are the detailed findings.",
            ["conclusion"] = "This is the conclusion.",
            ["author"] = "AI Assistant"
        };

        var referencedBlocks = new Dictionary<string, Block>
        {
            ["intro"] = block1,
            ["details"] = block2,
            ["conclusion"] = block3
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        var blockRefs = items.Where(i => i.IsBlockReference).ToList();
        Assert.Equal(3, blockRefs.Count);

        Assert.Equal("This is the introduction section.", blockRefs[0].TextContent);
        Assert.Equal("Introduction", blockRefs[0].Title);

        Assert.Equal("These are the detailed findings.", blockRefs[1].TextContent);
        Assert.Equal("Details", blockRefs[1].Title);

        Assert.Equal("This is the conclusion.", blockRefs[2].TextContent);
        Assert.Equal("Conclusion", blockRefs[2].Title);

        // Check that non-block placeholders were replaced
        var textItems = items.Where(i => !i.IsBlockReference).ToList();
        Assert.Contains(textItems, t => t.TextContent?.Contains("TechCorp") == true);
        Assert.Contains(textItems, t => t.TextContent?.Contains("AI Assistant") == true);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_OnlyBlocksNoTextBetween_ProcessesCorrectly()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "a") { Name = "Block A" };
        var block2 = new Block(BlockTypes.Merge, workflow, "b") { Name = "Block B" };

        string template = "{a}{b}";
        var textInputs = new Dictionary<string, string?>
        {
            ["a"] = "Content A",
            ["b"] = "Content B"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["a"] = block1,
            ["b"] = block2
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: true);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        Assert.Equal(3, items.Count);
        Assert.True(items[0].IsBlockReference);
        Assert.True(items[1].IsBlockReference);
        Assert.Equal("Content A", items[0].TextContent);
        Assert.Equal("Content B", items[1].TextContent);
        Assert.Equal(CitationInstruction, items[2].TextContent);
        Assert.False(items[2].IsBlockReference);
        Assert.Equal(NaiveReplace(template, textInputs, shouldGetCitationsFromModel: true), result.GetPlainText());
    }

    #endregion

    #region ShouldGetCitationsFromModel = false Tests

    [Fact]
    public void BuildUserPromptContentSequence_WhenNotGettingCitationsFromModel_BlockContentIsPlainText()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow, "block1") { Name = "Analysis Block" };

        string template = "Based on the analysis: {block1}";
        var textInputs = new Dictionary<string, string?>
        {
            ["block1"] = "This is the block output content."
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["block1"] = block
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Plain text items are condensed into one
        _ = Assert.Single(items);

        // All content merged into single plain text (no metadata)
        Assert.Equal("Based on the analysis: This is the block output content.", items[0].TextContent);
        Assert.False(items[0].IsBlockReference);
        Assert.Null(items[0].Title);
        Assert.Null(items[0].ReferenceId);
        Assert.Null(items[0].Context);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    [Fact]
    public void BuildUserPromptContentSequence_WhenNotGettingCitationsFromModel_MultipleBlocksAsPlainText()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block1 = new Block(BlockTypes.Merge, workflow, "analysis") { Name = "Analysis" };
        var block2 = new Block(BlockTypes.Merge, workflow, "summary") { Name = "Summary" };

        string template = "First: {analysis} Second: {summary}";
        var textInputs = new Dictionary<string, string?>
        {
            ["analysis"] = "Analysis output",
            ["summary"] = "Summary output"
        };
        var referencedBlocks = new Dictionary<string, Block>
        {
            ["analysis"] = block1,
            ["summary"] = block2
        };

        // Act
        RequestContentSequence? result = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            template,
            textInputs,
            referencedBlocks,
            shouldGetCitationsFromModel: false);

        // Assert
        Assert.NotNull(result);
        var items = result.ToList();

        // Plain text items are condensed into one
        _ = Assert.Single(items);

        // No block references when not getting citations from model
        Assert.False(items[0].IsBlockReference);

        // All content merged into single plain text
        Assert.Contains("Analysis output", items[0].TextContent);
        Assert.Contains("Summary output", items[0].TextContent);
        Assert.Equal(NaiveReplace(template, textInputs), result.GetPlainText());
    }

    #endregion

    #region BuildContentSequence Tests

    private static Page CreatePage(Document document, int pageNumber, string? text = null, string? overview = null, BinaryContent? image = null)
    {
        Page page = new(document, pageNumber, pageNumber) { Text = text, Overview = overview, Image = image };
        return page;
    }

    private static Document CreateDocument(string name)
    {
        FileMetadata fileMetadata = new("org1", name, 100, StorageProvider.Database, Guid.NewGuid().ToString());
        return new Document(fileMetadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(name)));
    }

    [Fact]
    public void BuildContentSequence_NullPromptAndNoPages_ReturnsNull()
    {
        RequestContentSequence? seq = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.Null(seq);
    }

    [Fact]
    public void BuildContentSequence_PagesWithTextOnly_UserPromptAppended()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Page 1 text") },
            { 1, CreatePage(doc, 2, text: "Page 2 text") }
        };

        RequestContentSequence? seq = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Final user prompt.",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(seq);
        List<RequestCommonContent> items = [.. seq];

        Assert.Equal(3, items.Count);
        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("Page 1 text", items[0].TextContent);
        Assert.Contains("<page2(Doc.pdf)-text>", items[1].TextContent);
        Assert.Contains("Page 2 text", items[1].TextContent);
        Assert.Equal("Final user prompt.", items[2].TextContent);
    }

    [Fact]
    public void BuildContentSequence_ShouldReturnNull_WhenNoPromptAndNoPages()
    {
        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.Null(result);
    }

    [Fact]
    public void BuildContentSequence_ShouldIncludePagesWithImageAndTextAndPromptInOrder()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img1 = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Page 1 text", image: img1) },
            { 1, CreatePage(doc, 2, text: "Page 2 text") }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Final prompt",
            inputContext: pages,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);

        List<RequestCommonContent> list = [.. sequence];
        Assert.Equal(6, list.Count);

        Assert.Equal("<page1(Doc.pdf)-image>", list[0].TextContent);
        Assert.Same(img1, list[1].BinaryContent);
        Assert.Equal("</page1(Doc.pdf)-image>", list[2].TextContent);
        Assert.Contains("<page1(Doc.pdf)-text>", list[3].TextContent);
        Assert.Contains("Page 1 text", list[3].TextContent);
        Assert.Contains("<page2(Doc.pdf)-text>", list[4].TextContent);
        Assert.Contains("Page 2 text", list[4].TextContent);
        Assert.Equal("Final prompt", list[5].TextContent);
    }

    [Fact]
    public void BuildContentSequence_ShouldHandlePageWithOnlyImage()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: null, image: img) }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];
        Assert.Equal(3, list.Count);

        Assert.Equal("<page1(Doc.pdf)-image>", list[0].TextContent);
        Assert.Same(img, list[1].BinaryContent);
        Assert.Equal("</page1(Doc.pdf)-image>", list[2].TextContent);
    }

    [Fact]
    public void BuildContentSequence_OcrImageOnlyPage_EmitsImageWhenImagesDisabledAndNoTextBlock()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
        Page ocrPage = CreatePage(doc, 1, text: null, image: img);
        ocrPage.Status = PageStatus.OCRNeedsReview;
        Dictionary<int, Page> pages = new() { { 0, ocrPage } };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];
        Assert.Equal(3, list.Count);
        Assert.Equal("<page1(Doc.pdf)-image>", list[0].TextContent);
        Assert.Same(img, list[1].BinaryContent);
        Assert.Equal("</page1(Doc.pdf)-image>", list[2].TextContent);
        Assert.DoesNotContain(list, c => c.TextContent is not null && c.TextContent.Contains("-text"));
    }

    [Fact]
    public void BuildContentSequence_OcrImageOnlyPage_WithOverviews_EmitsImageAndOverviewText()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
        Page ocrPage = CreatePage(doc, 1, text: null, overview: "Image-derived overview", image: img);
        ocrPage.Status = PageStatus.OCRNeedsReview;
        Dictionary<int, Page> pages = new() { { 0, ocrPage } };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: true,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Contains(list, c => ReferenceEquals(c.BinaryContent, img));
        Assert.Contains(list, c => c.TextContent is not null && c.TextContent.Contains("<page1(Doc.pdf)-image>"));
        Assert.Contains(list, c => c.TextContent is not null && c.TextContent.Contains("<page1(Doc.pdf)-text>"));
        Assert.Contains(list, c => c.TextContent is not null && c.TextContent.Contains("Image-derived overview"));
    }

    [Fact]
    public void BuildContentSequence_ImagesExceedPerRequestCap_AttachesOnlyCapAndKeepsAllText()
    {
        Document doc = CreateDocument("Doc.pdf");
        int pageCount = RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST + 3;
        Dictionary<int, Page> pages = new();
        for (int i = 0; i < pageCount; i++)
        {
            BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
            pages[i] = CreatePage(doc, i + 1, text: $"Page {i + 1} text", image: img);
        }

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Equal(RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST, list.Count(c => c.BinaryContent is not null));
        Assert.Equal(pageCount, list.Count(c => c.IsInputContext));
        Assert.DoesNotContain(list, c => c.TextContent == $"<page{pageCount}(Doc.pdf)-image>");
        Assert.Contains(list, c => c.TextContent is not null && c.TextContent.Contains($"<page{pageCount}(Doc.pdf)-text>"));
    }

    [Fact]
    public void BuildContentSequence_ImagesExceedPerRequestCap_OcrImageOnlyPagesKeepTheirImages()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new();
        for (int i = 0; i < RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST; i++)
        {
            BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
            pages[i] = CreatePage(doc, i + 1, text: $"Page {i + 1} text", image: img);
        }

        List<BinaryContent> flaggedImages = [];
        for (int i = 0; i < 3; i++)
        {
            BinaryContent flaggedImg = new(new BinaryData([0x0A, 0x0B, (byte)i]), MediaType.ImagePng);
            flaggedImages.Add(flaggedImg);
            int index = RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST + i;
            Page flaggedPage = CreatePage(doc, index + 1, text: null, image: flaggedImg);
            flaggedPage.Status = PageStatus.OCRNeedsReview;
            pages[index] = flaggedPage;
        }

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Equal(RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST, list.Count(c => c.BinaryContent is not null));
        foreach (BinaryContent flaggedImg in flaggedImages)
        {
            Assert.Contains(list, c => ReferenceEquals(c.BinaryContent, flaggedImg));
        }

        Assert.Contains(list, c => c.TextContent == "<page97(Doc.pdf)-image>");
        Assert.DoesNotContain(list, c => c.TextContent == "<page98(Doc.pdf)-image>");
        Assert.DoesNotContain(list, c => c.TextContent == "<page99(Doc.pdf)-image>");
        Assert.DoesNotContain(list, c => c.TextContent == "<page100(Doc.pdf)-image>");
    }

    [Fact]
    public void BuildContentSequence_ImagesExactlyAtPerRequestCap_AllImagesAttached()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new();
        for (int i = 0; i < RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST; i++)
        {
            BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
            pages[i] = CreatePage(doc, i + 1, text: $"Page {i + 1} text", image: img);
        }

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Equal(RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST, list.Count(c => c.BinaryContent is not null));
    }

    [Fact]
    public void BuildContentSequence_ShouldExcludeNullPageTextAndNullImage()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: null, image: null) }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Prompt",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        _ = Assert.Single(list);
        Assert.Equal("Prompt", list[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_ShouldUseOverviews_WhenFlagIsTrue()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Full text content", overview: "Brief overview") }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Prompt",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: true,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Equal(2, list.Count);
        Assert.Contains("Brief overview", list[0].TextContent);
        Assert.DoesNotContain("Full text content", list[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_ShouldUseText_WhenOverviewsFlagIsFalse()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Full text content", overview: "Brief overview") }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Prompt",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        Assert.Equal(2, list.Count);
        Assert.Contains("Full text content", list[0].TextContent);
        Assert.DoesNotContain("Brief overview", list[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_ShouldExcludeImages_WhenFlagIsFalse()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Page text", image: img) }
        };

        RequestContentSequence? sequence = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Prompt",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(sequence);
        List<RequestCommonContent> list = [.. sequence];

        // Only text + prompt, no image tags
        Assert.Equal(2, list.Count);
        Assert.Contains("Page text", list[0].TextContent);
        Assert.DoesNotContain(list, item => item.BinaryContent is not null);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_NullPromptContentAndNoContext_ReturnsNull()
    {
        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: null,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.Null(result);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_EmptyPromptContentAndNoContext_ReturnsNull()
    {
        RequestContentSequence emptySequence = [];

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: emptySequence,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.Null(result);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_NullPromptContentWithTextContext_ReturnsContextOnly()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Document content") }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: null,
            inputContext: inputContext,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        _ = Assert.Single(items);
        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("Document content", items[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_NullPromptContentWithImageContext_ReturnsContextOnly()
    {
        Document doc = CreateDocument("Doc.pdf");
        BinaryContent img = new(new BinaryData([0x01, 0x02]), MediaType.ImagePng);
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: null, image: img) }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: null,
            inputContext: inputContext,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(3, items.Count);
        Assert.Equal("<page1(Doc.pdf)-image>", items[0].TextContent);
        Assert.Same(img, items[1].BinaryContent);
        Assert.Equal("</page1(Doc.pdf)-image>", items[2].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_PromptContentWithEmptyContext_ReturnsPromptContentOnly()
    {
        RequestContentSequence promptContent = [new RequestCommonContent("User question here")];

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        _ = Assert.Single(items);
        Assert.Equal("User question here", items[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_BothPromptContentAndContext_CombinesInCorrectOrder()
    {
        RequestContentSequence promptContent = [new RequestCommonContent("User question")];

        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Page content") }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: inputContext,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(2, items.Count);
        // Input context comes first
        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("Page content", items[0].TextContent);
        // User prompt content comes last
        Assert.Equal("User question", items[1].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_PromptContentWithImage_PreservesImageContent()
    {
        RequestContentSequence promptContent = [];
        BinaryContent userImage = new(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImageJpeg);
        promptContent.Add("Look at this image:");
        promptContent.Add(userImage);

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(2, items.Count);
        Assert.Equal("Look at this image:", items[0].TextContent);
        Assert.Same(userImage, items[1].BinaryContent);
        Assert.Equal(CommonContentType.Image, items[1].Type);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_PreservesSourceContentFlags()
    {
        RequestContentSequence promptContent = [];
        RequestCommonContent sourceContent = new("Previous block output")
        {
            IsBlockReference = true,
            ReferenceId = "block-123"
        };
        promptContent.Add(sourceContent);
        promptContent.Add("Analyze the above");

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsBlockReference);
        Assert.Equal("block-123", items[0].ReferenceId);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_WhitespaceOnlyPromptContent_ReturnsNull()
    {
        RequestContentSequence promptContent = [new RequestCommonContent("   "), new RequestCommonContent("\n\t")];

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.Null(result);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_MixedContentWithContext_CombinesCorrectly()
    {
        RequestContentSequence promptContent = [];
        BinaryContent userImage = new(new BinaryData([0x01]), MediaType.ImagePng);
        promptContent.Add("Question about document:");
        promptContent.Add(userImage);

        Document doc = CreateDocument("Doc.pdf");
        BinaryContent docImage = new(new BinaryData([0x02]), MediaType.ImagePng);
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Document text", image: docImage) }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: inputContext,
            shouldIncludeImages: true,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];

        // Order: <doc-image>, image, </doc-image>, <doc-text>..., user text, user image
        Assert.Equal(6, items.Count);
        Assert.Equal("<page1(Doc.pdf)-image>", items[0].TextContent);
        Assert.Same(docImage, items[1].BinaryContent);
        Assert.Equal("</page1(Doc.pdf)-image>", items[2].TextContent);
        Assert.Contains("<page1(Doc.pdf)-text>", items[3].TextContent);
        Assert.Equal("Question about document:", items[4].TextContent);
        Assert.Same(userImage, items[5].BinaryContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_MultipleContextChunks_PreservesOrder()
    {
        RequestContentSequence promptContent = [new RequestCommonContent("Summary question")];

        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Page 1 content") },
            { 1, CreatePage(doc, 2, text: "Page 2 content") },
            { 2, CreatePage(doc, 3, text: "Page 3 content") }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: inputContext,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(4, items.Count);

        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("<page2(Doc.pdf)-text>", items[1].TextContent);
        Assert.Contains("<page3(Doc.pdf)-text>", items[2].TextContent);
        Assert.Equal("Summary question", items[3].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_ContextSetsIsInputContextFlag()
    {
        Document doc = CreateDocument("TestDoc.pdf");
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Test content") }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: null,
            inputContext: inputContext,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: true);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        _ = Assert.Single(items);
        Assert.True(items[0].IsInputContext);
        Assert.Equal("page1(TestDoc.pdf)", items[0].Title);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_PromptContentWithMultipleItems_AllItemsPreserved()
    {
        RequestContentSequence promptContent = [];
        BinaryContent img1 = new(new BinaryData([0x01]), MediaType.ImagePng);
        BinaryContent img2 = new(new BinaryData([0x02]), MediaType.ImageJpeg);

        promptContent.Add("First text");
        promptContent.Add(img1);
        promptContent.Add("Second text");
        promptContent.Add(img2);
        promptContent.Add("Third text");

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: [],
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];
        Assert.Equal(5, items.Count);
        Assert.Equal("First text", items[0].TextContent);
        Assert.Same(img1, items[1].BinaryContent);
        Assert.Equal("Second text", items[2].TextContent);
        Assert.Same(img2, items[3].BinaryContent);
        Assert.Equal("Third text", items[4].TextContent);
    }

    #endregion

    #region ShouldGetCitationsFromModel = false Tests (BuildContentSequence)

    [Fact]
    public void BuildContentSequence_WhenNotGettingCitationsFromModel_PageContentIsPlainText()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Page 1 text") }
        };

        RequestContentSequence? seq = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: "Final user prompt.",
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(seq);
        List<RequestCommonContent> items = [.. seq];

        // Plain text items are condensed into one
        _ = Assert.Single(items);

        // All content merged into single plain text (no input context metadata)
        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("Page 1 text", items[0].TextContent);
        Assert.Contains("Final user prompt.", items[0].TextContent);
        Assert.False(items[0].IsInputContext);
        Assert.Null(items[0].Title);
        Assert.Null(items[0].ReferenceId);
        Assert.Null(items[0].Context);
    }

    [Fact]
    public void BuildContentSequence_WhenNotGettingCitationsFromModel_MultiplePagesAsPlainText()
    {
        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> pages = new()
        {
            { 0, CreatePage(doc, 1, text: "Page 1 text") },
            { 1, CreatePage(doc, 2, text: "Page 2 text") }
        };

        RequestContentSequence? seq = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null,
            inputContext: pages,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(seq);
        List<RequestCommonContent> items = [.. seq];

        // Plain text items are condensed into one
        _ = Assert.Single(items);

        // All pages merged into single plain text (no input context metadata)
        Assert.False(items[0].IsInputContext);
        Assert.Null(items[0].Title);
        Assert.Null(items[0].ReferenceId);

        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("<page2(Doc.pdf)-text>", items[0].TextContent);
    }

    [Fact]
    public void BuildContentSequence_WithSequence_WhenNotGettingCitationsFromModel_PageContentIsPlainText()
    {
        RequestContentSequence promptContent = [new RequestCommonContent("User question")];

        Document doc = CreateDocument("Doc.pdf");
        Dictionary<int, Page> inputContext = new()
        {
            { 0, CreatePage(doc, 1, text: "Page content") }
        };

        RequestContentSequence? result = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent: promptContent,
            inputContext: inputContext,
            shouldIncludeImages: false,
            shouldUseOverviews: false,
            shouldGetCitationsFromModel: false);

        Assert.NotNull(result);
        List<RequestCommonContent> items = [.. result];

        // Plain text items are condensed into one
        _ = Assert.Single(items);

        // All content merged into single plain text (no metadata)
        Assert.Contains("<page1(Doc.pdf)-text>", items[0].TextContent);
        Assert.Contains("Page content", items[0].TextContent);
        Assert.Contains("User question", items[0].TextContent);
        Assert.False(items[0].IsInputContext);
        Assert.Null(items[0].Title);
        Assert.Null(items[0].ReferenceId);
    }

    #endregion
}
