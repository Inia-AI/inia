using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class ReplacementTagValidationServiceTests
{
    [Theory]
    [InlineData("{InputContext}", "InputContext")]
    [InlineData("{block_0}", "block_0")]
    [InlineData("prefix {block_12.answer} suffix", "block_12.answer")]
    [InlineData("Text {A_b0.C_d9} more text", "A_b0.C_d9")]
    [InlineData("{_underscore}", "_underscore")]
    [InlineData("{CamelCase}", "CamelCase")]
    [InlineData("{mixed_Case123}", "mixed_Case123")]
    [InlineData("{tag.field}", "tag.field")]
    [InlineData("{Variable123.Property456}", "Variable123.Property456")]
    public void ValidateReplacementTags_WithValidTags_ExtractsCorrectly(string template, string expectedTag)
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = expectedTag.Contains('.') ? expectedTag.Split('.')[0] : expectedTag
        });

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        // Should not have invalid tags since we added the base tag as a workflow variable
        Assert.Empty(result);
    }

    // The validator now uses the SAME grammar as the runtime engine (GET_PLACEHOLDERS):
    // characters the engine cannot put in a placeholder (" : , |) still never match, so
    // genuine JSON is ignored without any code-fence stripping.
    [Theory]
    [InlineData("{ \"narrative\": \"text\" }")] // JSON with spaces (quote + colon)
    [InlineData("{\"narrative\":\"text\"}")] // JSON without spaces
    [InlineData("{abc:def}")] // Colon
    [InlineData("{abc,field}")] // Comma
    [InlineData("{abc|field}")] // Pipe
    [InlineData("```json\n{ \"narrative\": \"example\" }\n```")] // Code fence (JSON has quotes)
    [InlineData("Inline `{ \"x\": 1 }` example")] // Inline code
    [InlineData("Mixed: {InputContext} and { \"json\": \"value\" }")] // reserved + JSON
    public void ValidateReplacementTags_WithNonMatchingGrammar_DoesNotExtract(string template)
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithSystemTags();

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    // Anything the runtime engine WOULD treat as a placeholder (and silently resolve to
    // "no data") must now be flagged when its base tag is unknown — including the forms the
    // old strict grammar silently dropped: spaces, dangling/leading dots, brackets, nested
    // braces, dashes. These are the confirmed false-negative gaps (G2/G3/G5).
    [Theory]
    [InlineData("{ not_valid }")] // Space (G5)
    [InlineData("{1abc}")] // Leading digit
    [InlineData("{abc.}")] // Dangling dot
    [InlineData("{.field}")] // Leading dot
    [InlineData("{abc[field]}")] // Square brackets (G2)
    [InlineData("{a{b}c}")] // Nested braces -> inner {b} extracted
    [InlineData("{gross-margin}")] // Dash (G5)
    public void ValidateReplacementTags_WithEngineGrammarUnknownBase_IsFlagged(string template)
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithSystemTags();

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithSystemReservedTags_ReturnsNoErrors()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        string template = $"Use {{InputContext}} and {{PeerCompanies}} and {{Counterparty}}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithPeerCompaniesArrayNotation_ReturnsNoErrors()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        // PeerCompanies[0] IS now extracted (brackets are part of the engine grammar); its
        // base tag, after stripping the [index], is "PeerCompanies" — a reserved tag — so it
        // is valid and not flagged.
        string template = "Compare {PeerCompanies[0]} with {PeerCompanies[1]}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithWorkflowVariables_ReturnsNoErrors()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "company_name"
        });
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "report_date"
        });

        string template = "Hello {company_name} for {report_date}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithValidBlockTags_ReturnsNoErrors()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        string template = "Compare {block_0.answer} and {block_1.json}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithInvalidBlockTags_ReturnsErrors()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        string template = "Missing: {block_999} and {nonexistent.field}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("{block_999}", result);
        Assert.Contains("{nonexistent.field}", result);
    }

    [Fact]
    public void ValidateReplacementTags_AiQuerySignaturePlaceholder_NotFlagged()
    {
        // The workflow defines a signature field; the prompt template references it
        // via `{cogs_instructions}`. The validator must treat the token as a valid tag
        // because workflow-level signature values feed through the TextInputs pipeline
        // at execution time.
        var workflow = new Workflow("test-workflow");
        _ = new WorkflowSignatureField(workflow, "cogs_instructions")
        {
            DisplayName = "COGS",
            Description = "desc",
            DefaultValue = "default-cogs",
        };
        var section = new Section("default", workflow);
        var aiBlock = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "ai_block",
            Properties = new AiQueryProperties(),
        };

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "Apply {cogs_instructions} to the data.",
            workflow,
            aiBlock);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_MergeSignaturePlaceholder_NotFlagged()
    {
        // Merge blocks resolve workflow-level signature tokens exactly like AI Query's —
        // the validator whitelists every defined field name regardless of block type.
        var workflow = new Workflow("test-workflow");
        _ = new WorkflowSignatureField(workflow, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "formal",
        };
        var section = new Section("default", workflow);
        var mergeBlock = new Block(BlockTypes.Merge, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "merge_block",
            Properties = new MergeProperties
            {
                TextTemplate = "Use {tone} please.",
            },
        };

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "Use {tone} please.",
            workflow,
            mergeBlock);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithCurrentBlockSelfReference_ReturnsError()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        Block currentBlock = workflow.GetAllBlocks().First(b => b.ReplacementTag == "block_1");
        string template = "Self reference: {block_1.answer}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow, currentBlock);

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("{block_1.answer}", result);
    }

    [Fact]
    public void ValidateReplacementTags_WithMixedContent_HandlesCorrectly()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "company_name"
        });

        string template = @"
        Valid tag: {InputContext}
        Valid variable: {company_name}
        Valid block: {block_0.answer}
        JSON example: { ""narrative"": ""This should be ignored"" }
        Invalid block: {missing_block}
        Code example: ```json
        { ""data"": { ""field"": ""value"" } }
        ```
        ";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("{missing_block}", result);
    }

    [Fact]
    public void ValidateReplacementTags_WithEmptyOrNullTemplate_ReturnsEmpty()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();

        // Act
        List<string> resultNull = ReplacementTagValidationService.ValidateReplacementTags(null, workflow);
        List<string> resultEmpty = ReplacementTagValidationService.ValidateReplacementTags("", workflow);
        List<string> resultWhitespace = ReplacementTagValidationService.ValidateReplacementTags("   ", workflow);

        // Assert
        Assert.Empty(resultNull);
        Assert.Empty(resultEmpty);
        Assert.Empty(resultWhitespace);
    }

    [Fact]
    public void ValidateReplacementTags_WithDuplicateTags_DeduplicatesResults()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        string template = "Duplicate {missing_tag} and another {missing_tag}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("{missing_tag}", result);
    }

    [Theory]
    [InlineData("Complex JSON: { \"user\": { \"name\": \"John\", \"data\": [1,2,3] } }", 0)]
    [InlineData("Nested objects: { \"a\": { \"b\": { \"c\": \"value\" } } }", 0)]
    [InlineData("Array notation: { \"items\": [\"item1\", \"item2\"] }", 0)]
    [InlineData("Mixed braces: {valid_tag} and { \"json\": \"data\" }", 1)] // Only valid_tag should be extracted
    public void ValidateReplacementTags_WithComplexJsonExamples_DoesNotExtractJson(string template, int expectedInvalidCount)
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Equal(expectedInvalidCount, result.Count);
    }

    [Fact]
    public void ValidateReplacementTags_GrammarValidation_FollowsSpecification()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();

        // Add workflow variables for valid tags
        foreach (string? tag in new[] { "InputContext", "block_0", "block_12", "A_b0", "some_variable", "_underscore" })
        {
            workflow.Variables.Add(new WorkflowVariable(workflow) { ReplacementTag = tag });
        }

        string[] validExamples = new[]
        {
            "{InputContext}",           // system reserved
            "{block_0}",               // single identifier
            "{block_12.answer}",       // identifier.identifier
            "{some_variable.field}",   // identifier.identifier
            "{A_b0.C_d9}",            // mixed case with numbers
            "{_underscore}",          // starts with underscore
        };

        // Forms the engine grammar cannot represent (contain " : , |) — never extracted.
        string[] nonMatchingExamples = new[]
        {
            "{ \"narrative\": \"…\" }", // JSON
            "{tag:field}",             // colon
            "{tag,field}",             // comma
            "{tag|field}",             // pipe
        };

        // Forms the engine WOULD resolve (and silently blank) but whose base is unknown —
        // must be flagged now (previously dropped by the strict grammar).
        string[] matchingUnknownExamples = new[]
        {
            "{not valid}",             // space
            "{tag.}",                  // dangling dot
            "{.field}",                // leading dot
            "{tag[field]}",            // brackets
        };

        // Act & Assert - Valid examples should not produce errors
        foreach (string? example in validExamples)
        {
            List<string> result = ReplacementTagValidationService.ValidateReplacementTags(example, workflow);
            Assert.Empty(result);
        }

        // Act & Assert - Non-matching grammar produces no errors (nothing extracted)
        foreach (string? example in nonMatchingExamples)
        {
            List<string> result = ReplacementTagValidationService.ValidateReplacementTags(example, workflow);
            Assert.Empty(result);
        }

        // Act & Assert - Engine-matching but unknown-base tags are flagged
        foreach (string? example in matchingUnknownExamples)
        {
            List<string> result = ReplacementTagValidationService.ValidateReplacementTags(example, workflow);
            Assert.NotEmpty(result);
        }
    }

    // ----- Confirmed screening gaps: each fails on the old strict-grammar validator -----

    [Fact]
    public void ValidateReplacementTags_G2_BracketEntityTypo_IsFlagged()
    {
        // {PeerCompany[0]} is a typo of the reserved {PeerCompanies[0]} (missing the 's').
        // The old regex couldn't match brackets so it was silently accepted; now the base
        // "PeerCompany" (brackets stripped) is unknown and must be flagged.
        Workflow workflow = CreateWorkflowWithBlocks();

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "Compare against {PeerCompany[0].Name}.", workflow);

        Assert.Contains("{PeerCompany[0].Name}", result);
    }

    [Fact]
    public void ValidateReplacementTags_G3_DoubleBraceTypo_IsFlagged()
    {
        // Double-brace placeholders resolve at runtime; a base-tag typo inside {{...}} was
        // discarded by the old nested-brace guard. Now it must be flagged.
        Workflow workflow = CreateWorkflowWithBlocks();

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "Refer to {{blokc_0.answer}}.", workflow);

        Assert.Contains("{blokc_0.answer}", result);
    }

    [Fact]
    public void ValidateReplacementTags_G3_DoubleBraceValidBase_NotFlagged()
    {
        // The valid double-brace form must NOT be flagged.
        Workflow workflow = CreateWorkflowWithBlocks();

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "Refer to {{block_0.answer}}.", workflow);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_G4_TagInsideCodeFence_IsFlagged()
    {
        // The runtime resolves placeholders inside ``` fences; the validator used to strip
        // fences and miss typos there. A brace-only tag (no quotes) inside a fence must flag.
        Workflow workflow = CreateWorkflowWithBlocks();
        string template = "```\nUse {block_typo.answer} here\n```";

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        Assert.Contains("{block_typo.answer}", result);
    }

    [Fact]
    public void ValidateReplacementTags_G5_DashedVariable_ValidNotFlagged_TypoFlagged()
    {
        // Dashes are legal in authored tags and resolve at runtime; the strict grammar was
        // blind to them entirely. The valid reference passes; a typo of it is flagged.
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow) { ReplacementTag = "gross-margin" });

        Assert.Empty(ReplacementTagValidationService.ValidateReplacementTags(
            "Summarise the {gross-margin}.", workflow));

        Assert.Contains("{gross-margn}", ReplacementTagValidationService.ValidateReplacementTags(
            "Summarise the {gross-margn}.", workflow));
    }

    private static Workflow CreateWorkflowWithBlocks()
    {
        var workflow = new Workflow("test-workflow");
        var section = new Section("default", workflow);

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_0",
        };

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_1",
        };

        return workflow;
    }

    [Fact]
    public void ValidateReplacementTags_RealWorldExample_FixesProblemStatement()
    {
        // Arrange - Test the exact scenario from the problem statement
        Workflow workflow = CreateWorkflowWithBlocks();
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "company_name"
        });

        // This template includes JSON examples that should NOT be treated as tags
        string problemTemplate = @"
        Use {InputContext} to analyze {company_name}.

        OUTPUT FORMAT:
        { ""narrative"": ""Your analysis here"", ""confidence"": 0.8 }

        Reference {block_0.answer} but ignore this JSON:
        { ""example"": { ""nested"": ""object"" } }

        Missing reference: {nonexistent_block}
        ";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(problemTemplate, workflow);

        // Assert - Should only report the genuinely missing tag, not the JSON
        _ = Assert.Single(result);
        Assert.Contains("{nonexistent_block}", result);

        // Should NOT contain any JSON fragments
        Assert.DoesNotContain(result, tag => tag.Contains("narrative"));
        Assert.DoesNotContain(result, tag => tag.Contains("confidence"));
        Assert.DoesNotContain(result, tag => tag.Contains("example"));
        Assert.DoesNotContain(result, tag => tag.Contains("nested"));
        Assert.DoesNotContain(result, tag => tag.Contains("object"));
    }

    [Fact]
    public void ValidateReplacementTags_WithParameterizedSectionParams_ReturnsNoErrors()
    {
        // Arrange
        var workflow = new Workflow("test-workflow");
        var section = new Section("test-section", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_0",
        };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "block_1",
        };
        var config = new ParameterizedSectionConfig(
        [
            new SectionVariant(
                Key: "AR",
                Label: "Annual Report",
                Params: new()
                {
                    ["_PERIOD_QUALIFIER"] = "",
                    ["_PERIOD_SCOPE"] = "for the years {CorporationAssessmentYear} and {CorporationAssessmentYear-1}",
                    ["_VARIANT_SUFFIX"] = "_AR"
                }),
            new SectionVariant(
                Key: "QR",
                Label: "Quarterly Report",
                Params: new()
                {
                    ["_PERIOD_QUALIFIER"] = "{InterimPeriod} month ",
                    ["_PERIOD_SCOPE"] = "for ALL available periods",
                    ["_VARIANT_SUFFIX"] = "_QR"
                })
        ]);
        string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        section.ParameterizedConfig = serialized;
        workflow.ParameterizedConfig = serialized;

        var template = "Extract from the {_PERIOD_QUALIFIER}income statement {_PERIOD_SCOPE}. Use {block_0{_VARIANT_SUFFIX}.answer}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert — params are ignored, and {block_0{_VARIANT_SUFFIX}.answer} expands to
        // the real suffixed tag block_0_AR / block_0_QR, so nothing is flagged.
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_WithParameterizedConfig_StillFlagsUnknownTags()
    {
        // Arrange
        Workflow workflow = CreateWorkflowWithBlocks();
        var config = new ParameterizedSectionConfig(
        [
            new SectionVariant(
                Key: "AR",
                Label: "Annual Report",
                Params: new() { ["_PERIOD_QUALIFIER"] = "" })
        ]);
        workflow.ParameterizedConfig = Newtonsoft.Json.JsonConvert.SerializeObject(config);

        var template = "Valid param {_PERIOD_QUALIFIER} but unknown {_TYPO_PARAM}";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert — _PERIOD_QUALIFIER ignored, _TYPO_PARAM flagged
        _ = Assert.Single(result);
        Assert.Contains("{_TYPO_PARAM}", result);
    }

    /// <summary>
    /// Builds a parameterized section with a block tagged "revenue_note" and two
    /// variants whose PERIOD_ID param resolves to the variant key. At runtime the
    /// block's tag is suffixed per variant (revenue_note_AR, revenue_note_QR), and
    /// a reference like {revenue_note_{PERIOD_ID}.answer} expands to those tags.
    /// </summary>
    private static Workflow CreateParameterizedSectionWithRevenueNote()
    {
        var workflow = new Workflow("test-workflow");
        var section = new Section("test-section", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "revenue_note",
        };

        var config = new ParameterizedSectionConfig(
        [
            new SectionVariant("AR", "Annual Report", new() { ["PERIOD_ID"] = "AR" }),
            new SectionVariant("QR", "Quarterly Report", new() { ["PERIOD_ID"] = "QR" })
        ]);
        string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        section.ParameterizedConfig = serialized;
        workflow.ParameterizedConfig = serialized;

        return workflow;
    }

    [Fact]
    public void ValidateReplacementTags_NestedParameterizedTag_WithValidBase_ReturnsNoErrors()
    {
        // Arrange — references the live base "revenue_note"; expands to
        // revenue_note_AR / revenue_note_QR, both real suffixed tags.
        Workflow workflow = CreateParameterizedSectionWithRevenueNote();
        var template = "Refer to {revenue_note_{PERIOD_ID}.answer}.";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_NestedParameterizedTag_WithStaleBase_IsFlagged()
    {
        // Arrange — the block was renamed revenue_notes -> revenue_note, but the
        // template still references the stale "revenue_notes". Each variant expands
        // it to revenue_notes_AR / revenue_notes_QR, neither of which exists.
        Workflow workflow = CreateParameterizedSectionWithRevenueNote();
        var template = "Refer to {revenue_notes_{PERIOD_ID}.answer}.";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("{revenue_notes_{PERIOD_ID}.answer}", result);
    }

    [Fact]
    public void ValidateReplacementTags_NestedParameterizedTag_ValidInOneVariantButNotAnother_IsFlagged()
    {
        // Arrange — a reference must resolve validly for EVERY variant the section
        // expands into. Here the QR variant's PERIOD_ID is misconfigured ("XYZ"), so
        // {revenue_note_{PERIOD_ID}.answer} expands to revenue_note_XYZ for QR, which
        // does not exist (only revenue_note_AR / revenue_note_QR do).
        var workflow = new Workflow("test-section");
        var section = new Section("test-section", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "revenue_note",
        };
        var config = new ParameterizedSectionConfig(
        [
            new SectionVariant("AR", "Annual Report", new() { ["PERIOD_ID"] = "AR" }),
            new SectionVariant("QR", "Quarterly Report", new() { ["PERIOD_ID"] = "XYZ" })
        ]);
        string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        section.ParameterizedConfig = serialized;
        workflow.ParameterizedConfig = serialized;

        var template = "Refer to {revenue_note_{PERIOD_ID}.answer}.";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("{revenue_note_{PERIOD_ID}.answer}", result);
    }

    [Fact]
    public void ValidateReplacementTags_ParameterizedWorkflowWithEmbeddedJson_DoesNotFlagJson()
    {
        // Arrange — a parameterized section whose prompt contains nested JSON braces.
        // The nested-tag validator must not treat JSON as a parameterized reference,
        // because its inner brace is not a known parameter name.
        Workflow workflow = CreateParameterizedSectionWithRevenueNote();
        var template = @"Return JSON shaped like { ""outer"": { ""inner"": 1 } } and use {revenue_note_{PERIOD_ID}.answer}.";

        // Act
        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(template, workflow);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_LoopItemPropertyInsideLoopBody_IsValid()
    {
        Workflow workflow = CreateWorkflowWithLoopSection();
        Block body = GetBlock(workflow, "body");

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds.ReportingStyle.Currency}",
            workflow,
            body);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_LoopItemPropertyAfterLoopEnd_IsInvalid()
    {
        // Outside the body there is no iteration to bind to, so the placeholder would reach the
        // model verbatim instead of resolving.
        Workflow workflow = CreateWorkflowWithLoopSection();
        Block afterLoop = GetBlock(workflow, "after_loop");

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds.ReportingStyle.Currency}",
            workflow,
            afterLoop);

        Assert.Equal(["{Funds.ReportingStyle.Currency}"], result);
    }

    [Fact]
    public void ValidateReplacementTags_LoopItemPropertyInLaterSection_IsInvalid()
    {
        Workflow workflow = CreateWorkflowWithLoopSection();
        Block report = GetBlock(workflow, "report");

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds.ReportingStyle}",
            workflow,
            report);

        Assert.Equal(["{Funds.ReportingStyle}"], result);
    }

    [Fact]
    public void ValidateReplacementTags_BareListTitleOutsideLoop_StaysValid()
    {
        Workflow workflow = CreateWorkflowWithLoopSection();
        Block report = GetBlock(workflow, "report");

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds}",
            workflow,
            report);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_ListTitleSharedByAnotherSectionsLoop_IsValidInEitherBody()
    {
        // Monitoring workflows fork the same loop into per-manager sections, all iterating "Funds".
        Workflow workflow = CreateWorkflowWithLoopSection();
        Block forkBody = GetBlock(workflow, "fork_body");

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds.ReportingStyle}",
            workflow,
            forkBody);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateReplacementTags_LoopItemPropertyWithoutCurrentBlock_IsValid()
    {
        Workflow workflow = CreateWorkflowWithLoopSection();

        List<string> result = ReplacementTagValidationService.ValidateReplacementTags(
            "{Funds.ReportingStyle}",
            workflow);

        Assert.Empty(result);
    }

    private static Block GetBlock(Workflow workflow, string replacementTag)
    {
        return workflow.GetAllBlocks().Single(b => b.ReplacementTag == replacementTag);
    }

    private static Workflow CreateWorkflowWithLoopSection()
    {
        var workflow = new Workflow("test-workflow");

        var loopSection = new Section("loop", workflow);
        _ = new Block(BlockTypes.LoopStart, loopSection)
        {
            ReplacementTag = "loop_start",
            Properties = new LoopStartProperties
            {
                ListTitle = "Funds",
                LoopedListTemplate = "{shortlist.funds}",
            },
        };
        _ = new Block(BlockTypes.AiQuery, loopSection) { ReplacementTag = "body" };
        _ = new Block(BlockTypes.LoopEnd, loopSection) { ReplacementTag = "loop_end" };
        _ = new Block(BlockTypes.Merge, loopSection) { ReplacementTag = "after_loop" };

        var forkSection = new Section("fork", workflow);
        _ = new Block(BlockTypes.LoopStart, forkSection)
        {
            ReplacementTag = "fork_loop_start",
            Properties = new LoopStartProperties
            {
                ListTitle = "Funds",
                LoopedListTemplate = "{shortlist.funds}",
            },
        };
        _ = new Block(BlockTypes.AiQuery, forkSection) { ReplacementTag = "fork_body" };
        _ = new Block(BlockTypes.LoopEnd, forkSection) { ReplacementTag = "fork_loop_end" };

        var reportSection = new Section("report", workflow);
        _ = new Block(BlockTypes.Merge, reportSection) { ReplacementTag = "report" };

        return workflow;
    }

    private static Workflow CreateWorkflowWithSystemTags()
    {
        var workflow = new Workflow("test-workflow");
        // No additional setup needed - system tags are handled by SharedInputKeys
        return workflow;
    }
}
