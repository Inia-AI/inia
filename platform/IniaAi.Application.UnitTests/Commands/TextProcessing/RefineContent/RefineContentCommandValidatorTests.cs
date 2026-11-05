using FluentValidation.Results;
using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.TextProcessing.RefineContent;

public class RefineContentCommandValidatorTests
{
    private const string ORG = "org-1";
    private const string USER = "user-1";
    private const string CONTENT = "Some content.";

    private readonly RefineContentCommandValidator _validator = new();

    private static RefineContentCommand Build(
        string content = CONTENT,
        string instruction = "Make it formal.",
        IReadOnlyList<RefinementTurnDto>? history = null)
    {
        return new RefineContentCommand(ORG, USER, content, instruction, history ?? []);
    }

    [Fact]
    public void Validate_WithMinimalValidCommand_Passes()
    {
        ValidationResult result = _validator.Validate(Build());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyOrganizationId_Fails()
    {
        var command = new RefineContentCommand("", USER, CONTENT, "Make formal.", []);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.OrganizationId));
    }

    [Fact]
    public void Validate_EmptyUserId_Fails()
    {
        var command = new RefineContentCommand(ORG, "", CONTENT, "Make formal.", []);

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.UserId));
    }

    [Fact]
    public void Validate_EmptyContent_Fails()
    {
        ValidationResult result = _validator.Validate(Build(content: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.Content));
    }

    [Fact]
    public void Validate_EmptyInstruction_Fails()
    {
        ValidationResult result = _validator.Validate(Build(instruction: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.Instruction));
    }

    [Fact]
    public void Validate_InstructionAtCapBoundary_Passes()
    {
        ValidationResult result = _validator.Validate(Build(instruction: new string('a', 10_000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InstructionOneCharOverCap_Fails()
    {
        ValidationResult result = _validator.Validate(Build(instruction: new string('a', 10_001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.Instruction));
    }

    [Fact]
    public void Validate_ContentAtCapBoundary_Passes()
    {
        ValidationResult result = _validator.Validate(Build(content: new string('a', 100_000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ContentOneCharOverCap_Fails()
    {
        ValidationResult result = _validator.Validate(Build(content: new string('a', 100_001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RefineContentCommand.Content));
    }

    [Fact]
    public void Validate_CombinedSizeAtBoundary_Passes()
    {
        // 20 alternating turns of 19_500 chars each = 390_000.
        // 100_000 + 10_000 + 390_000 = 500_000 (exactly at the 500k cap).
        var history = new List<RefinementTurnDto>();
        for (int i = 0; i < 20; i++)
        {
            history.Add(new RefinementTurnDto(i % 2 == 0 ? "user" : "assistant", new string('h', 19_500)));
        }

        ValidationResult result = _validator.Validate(Build(
            content: new string('c', 100_000),
            instruction: new string('i', 10_000),
            history: history));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CombinedSizeOverBoundary_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", new string('h', 100_000)),
            new("assistant", new string('h', 100_000)),
            new("user", new string('h', 100_000)),
            new("assistant", new string('h', 100_000)),
        };

        ValidationResult result = _validator.Validate(Build(
            content: new string('c', 100_000),
            instruction: new string('i', 1_000),
            history: history));

        // 100_000 + 1_000 + 400_000 = 501_000 (one over the 500k cap)
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("longer than the 500,000 characters that refine takes"));
    }

    [Fact]
    public void Validate_ConversationHistoryAtTurnCap_Passes()
    {
        var history = new List<RefinementTurnDto>();
        for (int i = 0; i < 20; i++)
        {
            history.Add(new RefinementTurnDto(i % 2 == 0 ? "user" : "assistant", "turn"));
        }

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ConversationHistoryOverTurnCap_Fails()
    {
        var history = new List<RefinementTurnDto>();
        for (int i = 0; i < 22; i++)
        {
            history.Add(new RefinementTurnDto(i % 2 == 0 ? "user" : "assistant", "turn"));
        }

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ConversationTurnWithInvalidRole_Fails()
    {
        var history = new List<RefinementTurnDto> { new("system", "noop") };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AlternatingUserAssistantPair_Passes()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", "ask"),
            new("assistant", "reply"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TurnWithEmptyContent_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", string.Empty),
            new("assistant", "reply"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_OddLengthHistory_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", "a"),
            new("assistant", "b"),
            new("user", "c"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("strict user/assistant pairs"));
    }

    [Fact]
    public void Validate_WrongOrderPair_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("assistant", "first"),
            new("user", "second"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("strict user/assistant pairs"));
    }

    [Fact]
    public void Validate_ConsecutiveSameRole_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", "a"),
            new("user", "b"),
            new("assistant", "c"),
            new("assistant", "d"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("strict user/assistant pairs"));
    }

    [Fact]
    public void Validate_TurnContentAtCapBoundary_Passes()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", new string('t', 100_000)),
            new("assistant", "ok"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TurnContentOneCharOverCap_Fails()
    {
        var history = new List<RefinementTurnDto>
        {
            new("user", new string('t', 100_001)),
            new("assistant", "ok"),
        };

        ValidationResult result = _validator.Validate(Build(history: history));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Conversation turn content"));
    }

    [Fact]
    public void Validate_ContentOverLoweredCap_FailsWithTheParagraphLengthAndTheCap()
    {
        var validator = new RefineContentCommandValidator(Options.Create(new ContentRefinementSettings
        {
            MaxContentLength = 50,
        }));

        ValidationResult result = validator.Validate(Build(content: new string('c', 62)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage
            == "The paragraph is 62 characters, more than the 50 that refine takes. Shorten the paragraph, then refine it again.");
    }

    [Fact]
    public void Validate_ContentAtLoweredCap_Passes()
    {
        var validator = new RefineContentCommandValidator(Options.Create(new ContentRefinementSettings
        {
            MaxContentLength = 50,
        }));

        ValidationResult result = validator.Validate(Build(content: new string('c', 50)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CombinedOverLoweredCap_FailsWithTheCapGroupedByThousands()
    {
        var validator = new RefineContentCommandValidator(Options.Create(new ContentRefinementSettings
        {
            MaxContentLength = 50_000,
            MaxCombinedLength = 1_500,
        }));
        var history = new List<RefinementTurnDto>
        {
            new("user", new string('h', 500)),
            new("assistant", new string('h', 500)),
        };

        ValidationResult result = validator.Validate(Build(content: new string('c', 500), instruction: "Shorter.", history: history));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage
            == "This refine conversation is longer than the 1,500 characters that refine takes. Close the panel, then start a new refine.");
    }

    [Fact]
    public void Validate_ParagraphOverBothCaps_NamesOnlyTheParagraph()
    {
        var validator = new RefineContentCommandValidator(Options.Create(new ContentRefinementSettings
        {
            MaxContentLength = 200,
            MaxCombinedLength = 1_500,
        }));

        ValidationResult result = validator.Validate(Build(content: new string('c', 2_903)));

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(
            "The paragraph is 2,903 characters, more than the 200 that refine takes. Shorten the paragraph, then refine it again.",
            failure.ErrorMessage);
    }
}
