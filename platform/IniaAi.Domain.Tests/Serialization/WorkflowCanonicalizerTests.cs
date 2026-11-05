using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

public class WorkflowCanonicalizerTests
{
    private const string DROPPED_BLOCK_FIELD = "ShouldExcludeBlockOutputFromCitationSources";

    private readonly WorkflowCanonicalizer _canonicalizer = new();

    [Fact]
    public void StoredContentHash_VersionSavedBeforeABlockFieldWasDropped_EqualsTheLiveTemplate()
    {
        Workflow live = BuildSampleWorkflow();
        var stored = JObject.Parse(_canonicalizer.ToCanonicalJson(live));
        foreach (JObject block in stored.SelectTokens("$.Sections[*].Blocks[*]").OfType<JObject>())
        {
            block[DROPPED_BLOCK_FIELD] = true;
        }

        string storedJson = stored.ToString(Formatting.None);

        Assert.Equal(_canonicalizer.ComputeContentHash(live), _canonicalizer.ComputeStoredContentHash(storedJson));
    }

    [Fact]
    public void ChangeFingerprint_StaysTheSame_WhileTheWorkflowDoesNotChange()
    {
        Workflow sample = BuildSampleWorkflow();

        Assert.Equal(_canonicalizer.ComputeChangeFingerprint(sample), _canonicalizer.ComputeChangeFingerprint(sample));
    }

    [Fact]
    public void ChangeFingerprint_Changes_WhenTheContentChanges()
    {
        Workflow sample = BuildSampleWorkflow();
        string before = _canonicalizer.ComputeChangeFingerprint(sample);

        sample.GetAllBlocks().First().Name += " (edited)";

        Assert.NotEqual(before, _canonicalizer.ComputeChangeFingerprint(sample));
    }

    [Fact]
    public void StoredContentHash_VersionSavedToday_EqualsItsLiveTemplate()
    {
        Workflow sample = BuildSampleWorkflow();
        Workflow fixture = Workflow.FromJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Serialization", "Fixtures", "RegressionFieldsTemplate.workflow.json")))!;

        Assert.Equal(
            _canonicalizer.ComputeContentHash(sample),
            _canonicalizer.ComputeStoredContentHash(_canonicalizer.ToCanonicalJson(sample)));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(fixture),
            _canonicalizer.ComputeStoredContentHash(_canonicalizer.ToCanonicalJson(fixture)));
    }

    [Fact]
    public void Payload_KeepsABlocksDataOutputName_ThroughTheRoundTrip()
    {
        Workflow sample = BuildSampleWorkflow();
        Block block = sample.GetAllBlocks().First(candidate => candidate.ReplacementTag == "counterpartyName");
        block.DataOutputName = "counterparty";

        Workflow reloaded = Workflow.FromJson(_canonicalizer.ToCanonicalJson(sample))!;

        Assert.Equal(
            "counterparty",
            reloaded.GetAllBlocks().First(candidate => candidate.ReplacementTag == "counterpartyName").DataOutputName);
    }

    [Fact]
    public void StoredContentHash_UnreadablePayload_IsNull()
    {
        Assert.Null(_canonicalizer.ComputeStoredContentHash("not json"));
    }

    [Fact]
    public void Canonicalize_DifferentEntityIds_ProducesIdenticalJson()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        Assert.NotEqual(a.ToJson(pretty: false), b.ToJson(pretty: false));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_DifferentWorkflowTypeId_ProducesIdenticalHash()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();
        b.WorkflowTypeId = "SameWorkflowDifferentEnvironmentTypeId";

        Assert.DoesNotContain("\"WorkflowTypeId\":", _canonicalizer.ToCanonicalJson(a));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_DifferentCreatedAtUpdatedAt_ProducesIdenticalJson()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        foreach (Section section in a.Sections)
        {
            section.CreatedAt = new DateTime(2020, 1, 1);
            section.UpdatedAt = new DateTime(2020, 2, 2);
        }

        foreach (Section section in b.Sections)
        {
            section.CreatedAt = new DateTime(2025, 12, 31);
            section.UpdatedAt = new DateTime(2025, 11, 30);
        }

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_VariablesInDifferentOrder_Identical()
    {
        var a = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowVariable(a, "alpha");
        _ = new WorkflowVariable(a, "bravo");
        _ = new WorkflowVariable(a, "charlie");

        var b = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowVariable(b, "charlie");
        _ = new WorkflowVariable(b, "alpha");
        _ = new WorkflowVariable(b, "bravo");

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_DocumentClassesInDifferentOrder_Identical()
    {
        var a = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new DocumentClass(a, "alpha");
        _ = new DocumentClass(a, "bravo");

        var b = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new DocumentClass(b, "bravo");
        _ = new DocumentClass(b, "alpha");

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_RegressionFieldsInDifferentOrder_Identical()
    {
        var a = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowRegressionField(a, "alpha") { Tolerance = 1 };
        _ = new WorkflowRegressionField(a, "bravo") { Type = RegressionFieldTypes.TEXT };

        var b = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowRegressionField(b, "bravo") { Type = RegressionFieldTypes.TEXT };
        _ = new WorkflowRegressionField(b, "alpha") { Tolerance = 1 };

        Assert.Equal(
            _canonicalizer.ToCanonicalJson(a),
            _canonicalizer.ToCanonicalJson(b));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_TextInputsInDifferentOrder_Identical()
    {
        Workflow a = WorkflowWithAiBlock(out Block blockA);
        _ = new TextInput(blockA, "alpha", "1");
        _ = new TextInput(blockA, "bravo", "2");

        Workflow b = WorkflowWithAiBlock(out Block blockB);
        _ = new TextInput(blockB, "bravo", "2");
        _ = new TextInput(blockB, "alpha", "1");

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_SupportedDocumentClassesInDifferentOrder_Identical()
    {
        Workflow a = WorkflowWithAiBlock(out Block blockA);
        blockA.SupportedDocumentClasses = ["bravo", "alpha"];

        Workflow b = WorkflowWithAiBlock(out Block blockB);
        blockB.SupportedDocumentClasses = ["alpha", "bravo"];

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_RagSettingsReorderedSameContent_Identical()
    {
        Workflow a = WorkflowWithAiBlock(out Block blockA);
        _ = new RagSettings(blockA) { Type = RagTypes.WholeDocument };

        Workflow b = WorkflowWithAiBlock(out Block blockB);
        _ = new RagSettings(blockB) { Type = RagTypes.WholeDocument };

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_SwitchCaseCasesDictKeysReordered_Identical()
    {
        Workflow a = WorkflowWithSwitchCase(new Dictionary<string, string>
        {
            ["xray"] = "1",
            ["alpha"] = "2",
        });

        Workflow b = WorkflowWithSwitchCase(new Dictionary<string, string>
        {
            ["alpha"] = "2",
            ["xray"] = "1",
        });

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_AiQuerySignatureFieldsReordered_Identical()
    {
        Workflow a = WorkflowWithSignatureFields(new Dictionary<string, SignatureFieldDefinition>
        {
            ["zeta"] = new("Zeta", "z help", "z default"),
            ["alpha"] = new("Alpha", "a help", "a default"),
        });

        Workflow b = WorkflowWithSignatureFields(new Dictionary<string, SignatureFieldDefinition>
        {
            ["alpha"] = new("Alpha", "a help", "a default"),
            ["zeta"] = new("Zeta", "z help", "z default"),
        });

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_WorkflowSignatureFieldsInDifferentOrder_Identical()
    {
        var a = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowSignatureField(a, "zeta")
        {
            DisplayName = "Zeta",
            Description = "z help",
            DefaultValue = "z default",
        };
        _ = new WorkflowSignatureField(a, "alpha")
        {
            DisplayName = "Alpha",
            Description = "a help",
            DefaultValue = "a default",
        };

        var b = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowSignatureField(b, "alpha")
        {
            DisplayName = "Alpha",
            Description = "a help",
            DefaultValue = "a default",
        };
        _ = new WorkflowSignatureField(b, "zeta")
        {
            DisplayName = "Zeta",
            Description = "z help",
            DefaultValue = "z default",
        };

        Assert.Equal(
            _canonicalizer.ToCanonicalJson(a),
            _canonicalizer.ToCanonicalJson(b));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_WorkflowSignatureFields_SortedByName()
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new WorkflowSignatureField(workflow, "zeta") { DefaultValue = "z" };
        _ = new WorkflowSignatureField(workflow, "alpha") { DefaultValue = "a" };

        string canonicalJson = _canonicalizer.ToCanonicalJson(workflow);

        int alphaIndex = canonicalJson.IndexOf("\"Name\":\"alpha\"", StringComparison.Ordinal);
        int zetaIndex = canonicalJson.IndexOf("\"Name\":\"zeta\"", StringComparison.Ordinal);
        Assert.True(alphaIndex >= 0);
        Assert.True(zetaIndex >= 0);
        Assert.True(alphaIndex < zetaIndex);
    }

    [Fact]
    public void Canonicalize_EmptySignatureFields_SuppressedFromCanonicalJson()
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };

        Assert.DoesNotContain("\"SignatureFields\"", _canonicalizer.ToCanonicalJson(workflow));
    }

    [Fact]
    public void Canonicalize_ExecutionStateAndBlockResponseIgnored()
    {
        Workflow workflow = BuildSampleWorkflow();
        string before = _canonicalizer.ComputeContentHash(workflow);

        Block block = workflow.GetAllBlocks().First();
        block.SetExecutionState(asPlaceHolder: false, inputs: []);
        block.BlockResponse = new BlockResponse(block) { RawResponse = "model output" };

        string after = _canonicalizer.ComputeContentHash(workflow);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Canonicalize_PropertiesListReordered_HashDiffers()
    {
        Workflow a = WorkflowWithIntelligentSearch(["alpha.com", "bravo.com"]);
        Workflow b = WorkflowWithIntelligentSearch(["bravo.com", "alpha.com"]);

        Assert.NotEqual(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_TwoIdenticalWorkflows_HashesEqual()
    {
        Assert.Equal(
            _canonicalizer.ComputeContentHash(BuildSampleWorkflow()),
            _canonicalizer.ComputeContentHash(BuildSampleWorkflow()));
    }

    [Fact]
    public void Canonicalize_OneFieldDifferent_HashesDiffer()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        b.GetAllBlocks().First(block => block.ReplacementTag == "counterpartyName").Name = "Changed";

        Assert.NotEqual(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_RoundTripsToSemanticallyEqualWorkflow()
    {
        Workflow workflow = BuildSampleWorkflow();

        string canonicalJson = _canonicalizer.ToCanonicalJson(workflow);
        var restored = Workflow.FromJson(canonicalJson);

        Assert.NotNull(restored);
        Assert.Equal(
            _canonicalizer.ComputeContentHash(workflow),
            _canonicalizer.ComputeContentHash(restored));
    }

    [Fact]
    public void ComputeContentHash_IsStableAcrossRepeatedCalls()
    {
        Workflow workflow = BuildSampleWorkflow();

        Assert.Equal(
            _canonicalizer.ComputeContentHash(workflow),
            _canonicalizer.ComputeContentHash(workflow));
    }

    [Fact]
    public void ComputeContentHash_IsLowercaseHex64()
    {
        string hash = _canonicalizer.ComputeContentHash(BuildSampleWorkflow());

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Canonicalize_StableBlockIdDiffersOnEveryBlock_HashesEqual()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        foreach (Block block in a.GetAllBlocks())
        {
            block.StableBlockId = "a_" + Guid.NewGuid().ToString("N");
        }

        foreach (Block block in b.GetAllBlocks())
        {
            block.StableBlockId = "b_" + Guid.NewGuid().ToString("N");
        }

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_BackfillingStableBlockId_HashUnchanged()
    {
        Workflow workflow = BuildSampleWorkflow();
        foreach (Block block in workflow.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        string before = _canonicalizer.ComputeContentHash(workflow);

        foreach (Block block in workflow.GetAllBlocks())
        {
            block.StableBlockId = Guid.NewGuid().ToString("N");
        }

        string after = _canonicalizer.ComputeContentHash(workflow);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Canonicalize_PayloadForm_RoundTripsStableIds()
    {
        Workflow workflow = BuildSampleWorkflow();
        foreach (Section section in workflow.Sections)
        {
            section.ReMintStableSectionId();
        }

        Workflow roundTripped = Workflow.FromJson(_canonicalizer.ToCanonicalJson(workflow))!;

        Assert.Equal(
            workflow.GetAllBlocks().OrderBy(b => b.ReplacementTag).Select(b => b.StableBlockId),
            roundTripped.GetAllBlocks().OrderBy(b => b.ReplacementTag).Select(b => b.StableBlockId));
        Assert.Equal(
            workflow.Sections.OrderBy(s => s.Order).Select(s => s.StableSectionId),
            roundTripped.Sections.OrderBy(s => s.Order).Select(s => s.StableSectionId));
    }

    [Fact]
    public void Canonicalize_HashForm_ExcludesStableIds()
    {
        Workflow workflow = BuildSampleWorkflow();
        foreach (Section section in workflow.Sections)
        {
            section.ReMintStableSectionId();
        }

        string hashJson = _canonicalizer.ToHashCanonicalJson(workflow);

        Assert.DoesNotContain("\"StableBlockId\"", hashJson);
        Assert.DoesNotContain("\"StableSectionId\"", hashJson);
    }

    [Fact]
    public void Canonicalize_FormatVersionStamped_ProducesIdenticalHash()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();
        b.FormatVersion = Workflow.CURRENT_FORMAT_VERSION;

        Assert.DoesNotContain("\"FormatVersion\":", _canonicalizer.ToCanonicalJson(b));
        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_NullVersusSetStableBlockId_HashesEqual()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        foreach (Block block in a.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        foreach (Block block in b.GetAllBlocks())
        {
            block.StableBlockId = "set_" + Guid.NewGuid().ToString("N");
        }

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_StableSectionIdDiffersOnEverySection_HashesEqual()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        foreach (Section section in a.Sections)
        {
            section.StableSectionId = "a_" + Guid.NewGuid().ToString("N");
        }

        foreach (Section section in b.Sections)
        {
            section.StableSectionId = "b_" + Guid.NewGuid().ToString("N");
        }

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_BackfillingStableSectionId_HashUnchanged()
    {
        Workflow workflow = BuildSampleWorkflow();
        foreach (Section section in workflow.Sections)
        {
            section.StableSectionId = null;
        }

        string before = _canonicalizer.ComputeContentHash(workflow);

        foreach (Section section in workflow.Sections)
        {
            section.StableSectionId = Guid.NewGuid().ToString("N");
        }

        string after = _canonicalizer.ComputeContentHash(workflow);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Canonicalize_NullVersusSetStableSectionId_HashesEqual()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();

        foreach (Section section in a.Sections)
        {
            section.StableSectionId = null;
        }

        foreach (Section section in b.Sections)
        {
            section.StableSectionId = "set_" + Guid.NewGuid().ToString("N");
        }

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_NullVersusSetSharedComponentLinkFields_HashesEqual()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();
        LinkEverything(b);

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
    }

    [Fact]
    public void Canonicalize_SharedComponentLinkFields_AbsentFromHashJson_PresentInPayloadJson()
    {
        Workflow workflow = BuildSampleWorkflow();
        LinkEverything(workflow);

        string hashJson = _canonicalizer.ToHashCanonicalJson(workflow);
        string payloadJson = _canonicalizer.ToCanonicalJson(workflow);

        Assert.DoesNotContain("\"SharedComponentId\"", hashJson);
        Assert.DoesNotContain("\"SharedComponentLinkState\"", hashJson);
        Assert.DoesNotContain("\"SharedComponentVersionHash\"", hashJson);
        Assert.DoesNotContain("\"SharedComponentBindings\"", hashJson);
        Assert.Contains("\"SharedComponentId\"", payloadJson);
        Assert.Contains("\"SharedComponentLinkState\"", payloadJson);
        Assert.Contains("\"SharedComponentVersionHash\"", payloadJson);
        Assert.Contains("\"SharedComponentBindings\"", payloadJson);
    }

    [Fact]
    public void Canonicalize_LinkOnlyDifference_SameHashDifferentPayload()
    {
        Workflow a = BuildSampleWorkflow();
        Workflow b = BuildSampleWorkflow();
        LinkEverything(b);

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
        Assert.NotEqual(
            _canonicalizer.ToCanonicalJson(a),
            _canonicalizer.ToCanonicalJson(b));
    }

    [Fact]
    public void Canonicalize_StableIds_AbsentFromHashJson_PresentInPayloadJson()
    {
        Workflow workflow = BuildSampleWorkflow();

        string hashJson = _canonicalizer.ToHashCanonicalJson(workflow);
        string payloadJson = _canonicalizer.ToCanonicalJson(workflow);

        Assert.DoesNotContain("\"StableBlockId\"", hashJson);
        Assert.DoesNotContain("\"StableSectionId\"", hashJson);
        Assert.Contains("\"StableBlockId\"", payloadJson);
        Assert.Contains("\"StableSectionId\"", payloadJson);
    }

    [Fact]
    public void Canonicalize_PayloadRoundTrip_PreservesStableIds()
    {
        Workflow workflow = BuildSampleWorkflow();
        List<string?> blockIds = [.. workflow.GetAllBlocks().Select(b => b.StableBlockId).Order()];
        List<string?> sectionIds = [.. workflow.Sections.Select(s => s.StableSectionId).Order()];

        var restored = Workflow.FromJson(_canonicalizer.ToCanonicalJson(workflow));

        Assert.NotNull(restored);
        Assert.Equal(blockIds, [.. restored.GetAllBlocks().Select(b => b.StableBlockId).Order()]);
        Assert.Equal(sectionIds, [.. restored.Sections.Select(s => s.StableSectionId).Order()]);
    }

    [Fact]
    public void Canonicalize_StableIdOnlyDifference_SameHashDifferentPayload()
    {
        Workflow a = BuildSampleWorkflow();
        NullAllStableIds(a);
        Workflow b = BuildSampleWorkflow();

        Assert.Equal(
            _canonicalizer.ComputeContentHash(a),
            _canonicalizer.ComputeContentHash(b));
        Assert.NotEqual(
            _canonicalizer.ToCanonicalJson(a),
            _canonicalizer.ToCanonicalJson(b));
    }

    [Fact]
    public void HashProfile_DoesNotSuppressSameNamedMembersOnOtherTypes()
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CanonicalContractResolver(payloadProfile: false),
        };

        string json = JsonConvert.SerializeObject(
            new UnrelatedCarrier(
                "component-ref",
                "Linked",
                "hash",
                new Dictionary<string, string> { ["a"] = "b" },
                "slot-states",
                "stable-block",
                "stable-section"),
            settings);

        Assert.Contains("\"SharedComponentId\":\"component-ref\"", json);
        Assert.Contains("\"SharedComponentLinkState\"", json);
        Assert.Contains("\"SharedComponentVersionHash\"", json);
        Assert.Contains("\"SharedComponentBindings\"", json);
        Assert.Contains("\"SharedComponentSlotStates\":\"slot-states\"", json);
        Assert.Contains("\"StableBlockId\":\"stable-block\"", json);
        Assert.Contains("\"StableSectionId\":\"stable-section\"", json);
    }

    [Fact]
    public void Canonicalize_PayloadRoundTrip_PreservesSlotStates()
    {
        Workflow workflow = BuildSampleWorkflow();
        Section carrier = workflow.Sections.First();
        carrier.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.Hidden,
            ["risk"] = SharedComponentSlotState.BrokenOut,
        };

        var restored = Workflow.FromJson(_canonicalizer.ToCanonicalJson(workflow));

        Assert.NotNull(restored);
        Section restoredCarrier = restored.Sections.Single(
            s => s.SharedComponentSlotStates is { Count: > 0 });
        Assert.Equal(SharedComponentSlotState.Hidden, restoredCarrier.SharedComponentSlotStates!["kyc"]);
        Assert.Equal(SharedComponentSlotState.BrokenOut, restoredCarrier.SharedComponentSlotStates["risk"]);
    }

    private sealed record UnrelatedCarrier(
        string SharedComponentId,
        string SharedComponentLinkState,
        string SharedComponentVersionHash,
        Dictionary<string, string> SharedComponentBindings,
        string SharedComponentSlotStates,
        string StableBlockId,
        string StableSectionId);

    private static void NullAllStableIds(Workflow workflow)
    {
        foreach (Section section in workflow.Sections)
        {
            section.StableSectionId = null;
        }

        foreach (Block block in workflow.GetAllBlocks())
        {
            block.StableBlockId = null;
        }
    }

    private static void LinkEverything(Workflow workflow)
    {
        foreach (Section section in workflow.Sections)
        {
            section.SharedComponentId = Guid.NewGuid().ToString("N");
            section.SharedComponentLinkState = SharedComponentLinkState.Linked;
            section.SharedComponentVersionHash = new string('a', 64);
            section.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "tag" };
        }

        foreach (Block block in workflow.GetAllBlocks())
        {
            block.SharedComponentId = Guid.NewGuid().ToString("N");
            block.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
            block.SharedComponentVersionHash = new string('b', 64);
            block.SharedComponentBindings = new Dictionary<string, string> { ["in"] = "out" };
        }
    }

    private static Workflow BuildSampleWorkflow()
    {
        var workflow = new Workflow("Quarterly report")
        {
            Title = "Quarterly",
            Description = "Quarterly counterparty report",
            WorkflowTypeId = "QReport",
        };

        var intro = new Section("Intro", workflow) { Title = "Introduction" };
        var aiBlock = new Block(BlockTypes.AiQuery, intro, "counterpartyName")
        {
            Name = "Counterparty name",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Who is the counterparty?",
                MaxTokens = 8000,
            },
        };
        _ = new TextInput(aiBlock, "context", "context value");
        _ = new RagSettings(aiBlock) { Type = RagTypes.WholeDocument };

        var body = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.Merge, body, "summary")
        {
            Name = "Summary",
            Properties = new MergeProperties { TextTemplate = "{counterpartyName}" },
        };

        _ = new WorkflowVariable(workflow, "reportingDate") { DisplayName = "Reporting date" };
        _ = new DocumentClass(workflow, "contract") { DisplayName = "Contract" };

        return workflow;
    }

    private static Workflow WorkflowWithAiBlock(out Block block)
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        var section = new Section("s", workflow);
        block = new Block(BlockTypes.AiQuery, section, "ai")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        return workflow;
    }

    private static Workflow WorkflowWithSwitchCase(Dictionary<string, string> cases)
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.SwitchCase, section, "sw")
        {
            Properties = new SwitchCaseProperties
            {
                Cases = cases,
                InputTemplate = "{in}",
            },
        };

        return workflow;
    }

    private static Workflow WorkflowWithSignatureFields(
        Dictionary<string, SignatureFieldDefinition> fields)
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "ai")
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "prompt",
                SignatureFields = fields,
            },
        };

        return workflow;
    }

    [Fact]
    public void Canonicalize_SettingsWhoseClassHasItsOwnConverter_AreWrittenByThatConverter()
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        _ = new Block(BlockTypes.Merge, new Section("s", workflow), "own") { Properties = new SelfWrittenSettings() };

        JObject canonical = JObject.Parse(_canonicalizer.ToCanonicalJson(workflow));

        Assert.Equal(SelfWrittenSettingsConverter.MARK, canonical.SelectToken("Sections[0].Blocks[0].Properties.Written")?.Value<string>());
    }

    private static Workflow WorkflowWithIntelligentSearch(List<string> excludeDomains)
    {
        var workflow = new Workflow("w") { WorkflowTypeId = "t" };
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.IntelligentSearch, section, "search")
        {
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "term",
                ExcludeDomains = excludeDomains,
            },
        };

        return workflow;
    }

    [JsonConverter(typeof(SelfWrittenSettingsConverter))]
    public sealed class SelfWrittenSettings : MergeProperties;

    public sealed class SelfWrittenSettingsConverter : JsonConverter<SelfWrittenSettings>
    {
        public const string MARK = "by its own converter";

        public override bool CanRead => false;

        public override SelfWrittenSettings ReadJson(
            JsonReader reader,
            Type objectType,
            SelfWrittenSettings? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        public override void WriteJson(JsonWriter writer, SelfWrittenSettings? value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Type");
            writer.WriteValue(nameof(BlockTypes.Merge));
            writer.WritePropertyName("Written");
            writer.WriteValue(MARK);
            writer.WriteEndObject();
        }
    }
}
