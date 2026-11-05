using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class WorkflowTemplateStructuralValidatorTests
{
    private readonly WorkflowTemplateStructuralValidator _validator = new();
    private readonly WorkflowCanonicalizer _canonicalizer = new();

    [Fact]
    public void MalformedJson_ReturnsParseError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate("{ this is not json");

        Assert.False(result.IsValid);
        Assert.Null(result.Workflow);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("workflow", error.Code);
    }

    [Fact]
    public void NonObjectJson_ReturnsError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate("[1, 2, 3]");

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("workflow", error.Code);
    }

    [Fact]
    public void NullRagSettings_ReturnsErrorWithExactPath()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json["Sections"]![0]!["Blocks"]![0]!["RagSettings"] = JValue.CreateNull();

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].RagSettings", error.Code);
    }

    [Fact]
    public void NullBlockProperties_ReturnsErrorNoCrash()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json["Sections"]![0]!["Blocks"]![0]!["Properties"] = JValue.CreateNull();

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Sections[0].Blocks[0].Properties");
    }

    [Fact]
    public void NullSections_ReturnsError()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json["Sections"] = JValue.CreateNull();

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Sections");
    }

    [Fact]
    public void NullSectionElement_ReturnsErrorNoCrash()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        ((JArray)json["Sections"]!).Add(JValue.CreateNull());

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Sections[1]");
    }

    [Fact]
    public void NullBlockElement_ReturnsErrorNoCrash()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        ((JArray)json["Sections"]![0]!["Blocks"]!).Add(JValue.CreateNull());

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Sections[0].Blocks[1]");
    }

    [Fact]
    public void NullVariableElement_ReturnsErrorNoCrash()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json["Variables"] = new JArray(JValue.CreateNull());

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Variables[0]");
    }

    [Fact]
    public void SameSectionDuplicateTag_ReturnsWarningOnly()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "dup_tag")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt one" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "dup_tag")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt two" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("dup_tag"));
    }

    [Fact]
    public void OverlongReplacementTag_ReturnsBlockingError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, new string('t', Block.MAX_REPLACEMENT_TAG_LENGTH + 1))
        {
            Name = "Too long",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Description.Contains("the maximum is"));
    }

    [Fact]
    public void OverlongDataOutputName_ReturnsBlockingError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.MergeJson, section, "facilities_json")
        {
            Name = "Facilities JSON",
            DataOutputName = new string('n', Block.MAX_REPLACEMENT_TAG_LENGTH + 1),
            Properties = new MergeJsonProperties(),
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Description.Contains("DataOutputName is 101 characters"));
    }

    [Fact]
    public void DataOutputNameTwoBlocksShare_ReturnsWarningOnly()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var sectionOne = new Section("One", workflow);
        var sectionTwo = new Section("Two", workflow);
        _ = new Block(BlockTypes.MergeJson, sectionOne, "first_json") { Name = "First", DataOutputName = "facilities", Properties = new MergeJsonProperties() };
        _ = new Block(BlockTypes.MergeJson, sectionTwo, "second_json") { Name = "Second", DataOutputName = "facilities", Properties = new MergeJsonProperties() };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("DataOutputName 'facilities' is used by 2 blocks"));
    }

    [Fact]
    public void ReplacementTagExactlyAtTheLimit_IsAccepted()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, new string('t', Block.MAX_REPLACEMENT_TAG_LENGTH))
        {
            Name = "At limit",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CrossSectionDuplicateTag_ReturnsWarningOnly()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var sectionOne = new Section("One", workflow);
        var sectionTwo = new Section("Two", workflow);
        _ = new Block(BlockTypes.AiQuery, sectionOne, "shared_tag")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt one" },
        };
        _ = new Block(BlockTypes.AiQuery, sectionTwo, "shared_tag")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt two" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("shared_tag"));
    }

    [Fact]
    public void UnknownPlaceholder_ReturnsWarning()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflow(prompt: "Use {unknown_tag} here")));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("unknown_tag"));
    }

    [Fact]
    public void ABlockThatSearchesOverviews_OfAClassWithoutAPrompt_ReturnsAWarningOnly()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflowSearchingOverviews(workflowDefault: null)));

        Assert.True(result.IsValid);
        WorkflowStructuralValidationWarning warning = Assert.Single(result.Warnings, warning => warning.Path == "DocumentClasses");
        Assert.Equal(
            "1 block reads page overviews, but Presentation documents get none. Give each an overview prompt, or set a default.",
            warning.Message);
    }

    [Fact]
    public void AWorkflowDefaultOverviewPrompt_CoversEveryClass_SoNothingWarns()
    {
        var workflowDefault = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}: <answer>[summary]</answer>" } };

        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflowSearchingOverviews(workflowDefault)));

        Assert.DoesNotContain(result.Warnings, warning => warning.Path == "DocumentClasses" || warning.Path.Contains("PageOverviews"));
    }

    [Fact]
    public void AnOverviewPromptThatNeverClosesItsAnswerTag_ReturnsAWarningOnly()
    {
        var unclosed = new PageOverviewPrompt { UserPrompt = "Summarise page {0}:\n<answer>\nPage {0}: [summary]\n<answer>" };
        var untagged = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." };
        var workflowDefault = new PageOverviewPrompts
        {
            Pages = unclosed,
            SinglePage = new PageOverviewPrompt { UserPrompt = "Summarise sheet {0}: <answer>[summary]</answer>" },
        };
        Workflow workflow = BuildWorkflowSearchingOverviews(workflowDefault);
        workflow.DocumentClasses.Single().PageOverviews = new PageOverviewPrompts { Pages = untagged };

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.True(result.IsValid);
        Assert.Equal(
            [
                new WorkflowStructuralValidationWarning("Configuration.PageOverviews.Pages", unclosed.AnswerFormatProblem!),
                new WorkflowStructuralValidationWarning("DocumentClasses[deck].PageOverviews.Pages", untagged.AnswerFormatProblem!),
            ],
            result.Warnings.Where(warning => warning.Path.Contains("PageOverviews")));
    }

    [Fact]
    public void KnownPlaceholders_NoWarnings()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "About {Counterparty.Name}" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "block2")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize {block1.answer}" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void BracketEntityTypo_ReturnsWarning()
    {
        // {PeerCompany[0]} is a typo of reserved {PeerCompanies[0]}; after stripping the
        // [index] the base "PeerCompany" is unknown and must be warned (G2, backend).
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflow(prompt: "Compare {PeerCompany[0].Name}")));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("PeerCompany"));
    }

    [Fact]
    public void BracketReservedEntity_NoWarning()
    {
        // {PeerCompanies[0].Name} strips to reserved base "PeerCompanies" — not warned.
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflow(prompt: "Compare {PeerCompanies[0].Name}")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EmptyName_ReturnsError()
    {
        var workflow = new Workflow(" ") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Name");
    }

    [Fact]
    public void DuplicateOrders_ReturnsWarning()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var sectionOne = new Section("One", workflow);
        var sectionTwo = new Section("Two", workflow);
        _ = new Block(BlockTypes.AiQuery, sectionOne, "block1")
        {
            Name = "First",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, sectionTwo, "block2")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(workflow));
        json["Sections"]![0]!["Order"] = 0;
        json["Sections"]![1]!["Order"] = 0;

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Path == "Sections");
    }

    [Fact]
    public void FormatVersionNewerThanCurrent_ReturnsClearErrorBeforeDeserializing()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json[nameof(Workflow.FormatVersion)] = Workflow.CURRENT_FORMAT_VERSION + 1;

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Null(result.Workflow);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(nameof(Workflow.FormatVersion), error.Code);
    }

    [Fact]
    public void FormatVersionCurrent_Passes()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json[nameof(Workflow.FormatVersion)] = Workflow.CURRENT_FORMAT_VERSION;

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
        Assert.NotNull(result.Workflow);
    }

    [Fact]
    public void FormatVersionBeyondInt32_ReturnsValidationErrorNotException()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json[nameof(Workflow.FormatVersion)] = 3000000000L;

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == nameof(Workflow.FormatVersion));
    }

    [Fact]
    public void FormatVersionStringToken_CaughtByEntityBackstop()
    {
        var json = JObject.Parse(_canonicalizer.ToCanonicalJson(BuildWorkflow()));
        json[nameof(Workflow.FormatVersion)] = (Workflow.CURRENT_FORMAT_VERSION + 1).ToString();

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == nameof(Workflow.FormatVersion));
    }

    [Fact]
    public void EntityWithNewerFormatVersion_ReturnsError()
    {
        Workflow workflow = BuildWorkflow();
        workflow.FormatVersion = Workflow.CURRENT_FORMAT_VERSION + 1;

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == nameof(Workflow.FormatVersion));
    }

    [Fact]
    public void ValidWorkflow_RoundTrips()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflow()));

        Assert.True(result.IsValid);
        Assert.NotNull(result.Workflow);
        Assert.Equal("Report", result.Workflow!.Name);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MisCasedBlockReference_ReturnsWarning()
    {
        // Block tag is "block1"; {Block1.answer} is case-mismatched. Backend screening must agree
        // with the case-sensitive runtime lookup and warn (it would resolve to "no data" at run time).
        WorkflowStructuralValidationResult result = _validator.Validate(
            _canonicalizer.ToCanonicalJson(BuildWorkflow(prompt: "See {Block1.answer}")));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("Block1"));
    }

    [Fact]
    public void RegexSource_ValidPattern_NoError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"regex\",\"Locator\":\"AUM: ([0-9]+)\"}}", "aum")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RegexSource_MissingLocator_ReturnsError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"regex\"}}", "aum")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "Sections[0].Blocks[0].RegressionSchema");
    }

    [Fact]
    public void RegexSource_InvalidPattern_ReturnsError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"regex\",\"Locator\":\"(\"}}", "aum")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Description.Contains("regex"));
    }

    [Fact]
    public void UnknownSource_ReturnsError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"bogus\"}}", "aum")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Description.Contains("bogus"));
    }

    [Fact]
    public void XmlSource_NoLocator_NoError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"xml\"}}", "aum")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NoSourceBinding_NoError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{\"aum\":{}}", "aum")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidBindingJson_ReturnsError()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(
            BuildWorkflowWithBinding("{ this is not json", "aum")));

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].RegressionSchema", error.Code);
        Assert.Equal("RegressionSchema is not valid JSON.", error.Description);
    }

    [Fact]
    public void DuplicateDefinitionNames_ReturnsError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, "AUM");
        _ = new WorkflowRegressionField(workflow, "aum");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("RegressionFields[1]", error.Code);
        Assert.Contains("defined more than once", error.Description);
    }

    [Fact]
    public void EmptyDefinitionName_ReturnsError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, " ");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("RegressionFields[0]", error.Code);
    }

    [Fact]
    public void UnknownDefinitionType_ReturnsError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, "aum") { Type = "bogus" };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("RegressionFields[0]", error.Code);
        Assert.Contains("unknown type 'bogus'", error.Description);
    }

    [Fact]
    public void NegativeTolerance_ReturnsError()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, "aum") { Tolerance = -1 };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("RegressionFields[0]", error.Code);
        Assert.Contains("negative tolerance", error.Description);
    }

    [Fact]
    public void BindingWithoutDefinition_ReturnsErrorNamingBlock()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(
            BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"xml\"}}"));

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].RegressionSchema", error.Code);
        Assert.Contains("block1", error.Description);
        Assert.Contains("defines no field", error.Description);
    }

    [Fact]
    public void SameFieldBoundByTwoBlocks_ReturnsErrorNamingBothBlocks()
    {
        Workflow workflow = BuildWorkflowWithBinding("{\"aum\":{\"Source\":\"xml\"}}", "aum");
        Section section = workflow.Sections.First();
        _ = new Block(BlockTypes.AiQuery, section, "block2")
        {
            Name = "Second binder",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = "{\"aum\":{\"Source\":\"json\"}}",
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[1].RegressionSchema", error.Code);
        Assert.Contains("block1", error.Description);
        Assert.Contains("block2", error.Description);
    }

    [Fact]
    public void IsRegressionError_TrueForRegressionErrors_FalseForOthers()
    {
        var workflow = new Workflow(" ") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, "aum") { Tolerance = -1 };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = "{\"orphan\":{\"Source\":\"xml\"}}",
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Error nameError = Assert.Single(result.Errors, error => error.Code == "Name");
        Assert.False(WorkflowTemplateStructuralValidator.IsRegressionError(nameError));

        List<Error> regressionErrors = [.. result.Errors.Where(error => error.Code != "Name")];
        Assert.Equal(2, regressionErrors.Count);
        Assert.All(regressionErrors, error => Assert.True(WorkflowTemplateStructuralValidator.IsRegressionError(error)));
    }

    [Fact]
    public void LegacyFullShapeSchemaJson_IsHoistedAndValidatesClean()
    {
        Workflow workflow = BuildWorkflow();
        workflow.GetAllBlocks().First().RegressionSchema =
            "{\"aum\":{\"type\":\"numeric\",\"tolerance\":1,\"source\":\"xml\"}}";

        WorkflowStructuralValidationResult result = _validator.Validate(_canonicalizer.ToCanonicalJson(workflow));

        Assert.True(result.IsValid);
        WorkflowRegressionField definition = Assert.Single(result.Workflow!.RegressionFields);
        Assert.Equal("aum", definition.Name);
        Assert.Equal(RegressionFieldTypes.NUMERIC, definition.Type);
        Assert.Equal(1m, definition.Tolerance);
        Assert.False(RegressionSchemaBindings.AnyLegacyKeys(result.Workflow.GetAllBlocks().First().RegressionSchema));
    }

    [Fact]
    public void LinkFields_ValidLinkedBlock_PassesShapeValidation()
    {
        JObject json = LinkedWorkflowJson();

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void LinkFields_WithoutSharedComponentId_ReturnsErrorPerOrphanField()
    {
        JObject json = LinkedWorkflowJson();
        JObject block = LinkedBlock(json);
        Assert.True(block.Remove("SharedComponentId"));

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Code == "Sections[0].Blocks[0].SharedComponentLinkState");
        Assert.Contains(result.Errors, e => e.Code == "Sections[0].Blocks[0].SharedComponentVersionHash");
        Assert.Contains(result.Errors, e => e.Code == "Sections[0].Blocks[0].SharedComponentBindings");
        Assert.All(result.Errors, e => Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, e.Description));
    }

    [Fact]
    public void LinkFields_IdWithoutLinkState_ReturnsError()
    {
        JObject json = LinkedWorkflowJson();
        JObject block = LinkedBlock(json);
        Assert.True(block.Remove("SharedComponentLinkState"));

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentLinkState", error.Code);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, error.Description);
    }

    [Fact]
    public void LinkFields_SectionIdWithoutLinkState_ReturnsError()
    {
        JObject json = SectionLinkedWorkflowJson();
        var section = (JObject)json["Sections"]![0]!;
        Assert.True(section.Remove("SharedComponentLinkState"));

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentLinkState", error.Code);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, error.Description);
    }

    [Fact]
    public void LinkState_UnknownString_ReturnsExactPathError()
    {
        JObject json = LinkedWorkflowJson();
        LinkedBlock(json)["SharedComponentLinkState"] = "FutureState";

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentLinkState", error.Code);
        Assert.Contains("\"Linked\" or \"BrokenOut\"", error.Description);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, error.Description);
    }

    [Fact]
    public void VersionHash_Malformed_ReturnsError()
    {
        JObject json = LinkedWorkflowJson();
        LinkedBlock(json)["SharedComponentVersionHash"] = "NOT-A-HASH";

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentVersionHash", error.Code);
    }

    [Fact]
    public void VersionHash_BoundaryShapes_AreRejected()
    {
        foreach (string hash in new[] { new string('A', 64), new string('a', 63), new string('a', 65) })
        {
            JObject json = LinkedWorkflowJson();
            LinkedBlock(json)["SharedComponentVersionHash"] = hash;

            WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

            Assert.False(result.IsValid);
            Error error = Assert.Single(result.Errors);
            Assert.Equal("Sections[0].Blocks[0].SharedComponentVersionHash", error.Code);
        }
    }

    [Fact]
    public void LinkState_BrokenOut_PassesShapeValidation()
    {
        JObject json = LinkedWorkflowJson();
        LinkedBlock(json)["SharedComponentLinkState"] = "BrokenOut";

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Bindings_NonStringValue_ReturnsError()
    {
        JObject json = LinkedWorkflowJson();
        LinkedBlock(json)["SharedComponentBindings"] = new JObject { ["slot"] = 42 };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentBindings", error.Code);
    }

    [Fact]
    public void SectionLinkFields_WithoutId_ReturnsSectionPathError()
    {
        JObject json = LinkedWorkflowJson();
        var section = (JObject)json["Sections"]![0]!;
        section["SharedComponentLinkState"] = "Linked";

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentLinkState", error.Code);
    }

    [Fact]
    public void LinkedBlockInsideLinkedSection_HydratedRefShape_Passes()
    {
        JObject json = LinkedWorkflowJson();
        var section = (JObject)json["Sections"]![0]!;
        section["SharedComponentId"] = "outer-component";
        section["SharedComponentLinkState"] = "Linked";

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SlotStates_OnLinkedSection_ValidShape_Passes()
    {
        JObject json = SectionLinkedWorkflowJson();
        ((JObject)json["Sections"]![0]!)["SharedComponentSlotStates"] = new JObject
        {
            ["kyc"] = "Hidden",
            ["risk"] = "BrokenOut",
        };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SlotStates_WithoutSectionId_ReturnsError()
    {
        JObject json = SectionLinkedWorkflowJson();
        var section = (JObject)json["Sections"]![0]!;
        Assert.True(section.Remove("SharedComponentId"));
        Assert.True(section.Remove("SharedComponentLinkState"));
        Assert.True(section.Remove("SharedComponentVersionHash"));
        section["SharedComponentSlotStates"] = new JObject { ["kyc"] = "Hidden" };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentSlotStates", error.Code);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, error.Description);
    }

    [Fact]
    public void SlotStates_UnknownStateName_ReturnsError()
    {
        JObject json = SectionLinkedWorkflowJson();
        ((JObject)json["Sections"]![0]!)["SharedComponentSlotStates"] = new JObject { ["kyc"] = "Ghosted" };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentSlotStates", error.Code);
        Assert.Contains("\"Hidden\" or \"BrokenOut\"", error.Description);
    }

    [Fact]
    public void SlotStates_NonObject_ReturnsError()
    {
        JObject json = SectionLinkedWorkflowJson();
        ((JObject)json["Sections"]![0]!)["SharedComponentSlotStates"] = 42;

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentSlotStates", error.Code);
    }

    [Fact]
    public void SlotStates_NonStringValue_ReturnsError()
    {
        JObject json = SectionLinkedWorkflowJson();
        ((JObject)json["Sections"]![0]!)["SharedComponentSlotStates"] = new JObject { ["kyc"] = 1 };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].SharedComponentSlotStates", error.Code);
    }

    [Fact]
    public void SlotStates_OnBlock_ReturnsError()
    {
        JObject json = LinkedWorkflowJson();
        LinkedBlock(json)["SharedComponentSlotStates"] = new JObject { ["kyc"] = "Hidden" };

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.False(result.IsValid);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentSlotStates", error.Code);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, error.Description);
    }

    [Fact]
    public void Entries_OnSection_IsStrippedWithAWarningSoAContentGetRoundTrips()
    {
        JObject json = LinkedWorkflowJson();
        ((JObject)json["Sections"]![0]!)["Entries"] = new JArray(
            new JObject
            {
                ["Kind"] = "ComponentRef",
                ["SlotTag"] = "alpha",
                ["SharedComponentId"] = "component-1",
            });

        WorkflowStructuralValidationResult result = _validator.Validate(json.ToString());

        Assert.True(result.IsValid);
        Assert.Null(result.Workflow!.Sections.First().Entries);
        WorkflowStructuralValidationWarning warning = Assert.Single(
            result.Warnings, w => w.Path == "Sections[0].Entries");
        Assert.Contains("dropped", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JObject SectionLinkedWorkflowJson()
    {
        JObject json = LinkedWorkflowJson();
        JObject block = LinkedBlock(json);
        Assert.True(block.Remove("SharedComponentId"));
        Assert.True(block.Remove("SharedComponentLinkState"));
        Assert.True(block.Remove("SharedComponentVersionHash"));
        Assert.True(block.Remove("SharedComponentBindings"));
        var section = (JObject)json["Sections"]![0]!;
        section["SharedComponentId"] = "section-component";
        section["SharedComponentLinkState"] = "Linked";
        section["SharedComponentVersionHash"] = new string('a', 64);

        return json;
    }

    private static JObject LinkedWorkflowJson()
    {
        return JObject.Parse(SharedComponentLinkTestData.LinkedWorkflowJson());
    }

    private static JObject LinkedBlock(JObject workflowJson)
    {
        return (JObject)workflowJson["Sections"]![0]!["Blocks"]![0]!;
    }

    [Fact]
    public void SignatureField_EmptyName_ReturnsError()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("", prompt: "prompt");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "SignatureFields[0]" && error.Description.Contains("must have a name"));
    }

    [Fact]
    public void SignatureField_DuplicateNames_CaseInsensitive_ReturnsError()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("field_a", prompt: "prompt {field_a}");
        _ = new WorkflowSignatureField(workflow, "FIELD_A") { DefaultValue = "other" };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "SignatureFields[1]" && error.Description.Contains("more than once"));
    }

    [Fact]
    public void SignatureField_NameCollidingWithBlockReplacementTag_ReturnsError()
    {
        // The block in BuildWorkflowWithSignatureField carries ReplacementTag "block1",
        // so a same-named definition would make the {block1} token ambiguous.
        Workflow workflow = BuildWorkflowWithSignatureField("block1", prompt: "prompt {block1}");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "SignatureFields[0]" && error.Description.Contains("ReplacementTag"));
    }

    [Fact]
    public void SignatureField_NameCollidingWithVariableTag_ReturnsError()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("my_variable", prompt: "prompt {my_variable}");
        _ = new WorkflowVariable(workflow, "my_variable");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "SignatureFields[0]" && error.Description.Contains("variable tag"));
    }

    [Fact]
    public void SignatureField_UnreferencedDefinition_ReturnsWarningNotError()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("orphan_field", prompt: "prompt without token");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.DoesNotContain(result.Errors, WorkflowTemplateStructuralValidator.IsSignatureError);
        Assert.Contains(result.Warnings, warning =>
            warning.Path == "SignatureFields[0]" && warning.Message.Contains("orphan_field"));
    }

    [Fact]
    public void SignatureField_ResidualInlineDictOnWorkflowBlock_ReturnsError()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("field_a", prompt: "prompt {field_a}");
        var properties = (AiQueryProperties)workflow.GetAllBlocks()[0].Properties;
        properties.SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["field_a"] = new("Field A", "desc", "inline-default"),
        };

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "Sections[0].Blocks[0].SignatureFields"
            && error.Description.Contains("inline signature field dict"));
    }

    [Fact]
    public void SignatureField_ValidReferencedDefinition_ProducesNoSignatureErrorsOrWarnings()
    {
        Workflow workflow = BuildWorkflowWithSignatureField("field_a", prompt: "prompt {field_a}");

        WorkflowStructuralValidationResult result = _validator.Validate(workflow);

        Assert.DoesNotContain(result.Errors, WorkflowTemplateStructuralValidator.IsSignatureError);
        Assert.DoesNotContain(result.Warnings, warning => warning.Path.Contains("SignatureFields"));
    }

    [Fact]
    public void IsSignatureError_MatchesOnlySignaturePaths()
    {
        Assert.True(WorkflowTemplateStructuralValidator.IsSignatureError(
            Error.Validation("SignatureFields[0]", "x")));
        Assert.True(WorkflowTemplateStructuralValidator.IsSignatureError(
            Error.Validation("Sections[0].Blocks[0].SignatureFields", "x")));
        Assert.False(WorkflowTemplateStructuralValidator.IsSignatureError(
            Error.Validation("Name", "x")));
        Assert.False(WorkflowTemplateStructuralValidator.IsSignatureError(
            Error.Validation("RegressionFields[0]", "x")));
    }

    private static Workflow BuildWorkflowSearchingOverviews(PageOverviewPrompts? workflowDefault)
    {
        var workflow = new Workflow("Report") { Title = "Report", Configuration = new WorkflowConfiguration { PageOverviews = workflowDefault } };
        _ = new DocumentClass(workflow, "deck") { DisplayName = "Presentation" };
        var section = new Section("Body", workflow);
        Block search = new(BlockTypes.AiQuery, section, "search")
        {
            Name = "Search",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            SupportedDocumentClasses = ["deck"],
        };
        _ = new RagSettings(search) { Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors };

        return workflow;
    }

    private static Workflow BuildWorkflowWithSignatureField(string fieldName, string prompt)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowSignatureField(workflow, fieldName)
        {
            DisplayName = "Field",
            Description = "desc",
            DefaultValue = "staff-default",
        };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private static Workflow BuildWorkflowWithBinding(string bindingJson, params string[] definedFieldNames)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        foreach (string fieldName in definedFieldNames)
        {
            _ = new WorkflowRegressionField(workflow, fieldName)
            {
                Type = RegressionFieldTypes.NUMERIC,
                Tolerance = 1,
            };
        }

        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
            RegressionSchema = bindingJson,
        };

        return workflow;
    }

    private static Workflow BuildWorkflow(string prompt = "Who is the counterparty?")
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }
}
