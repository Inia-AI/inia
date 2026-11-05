using IniaAi.Domain.Tools.PdfOcr;

namespace IniaAi.Domain.Tests.Tools.PdfOcr;

public class PdfOcrPromptsTests
{
    [Fact]
    public void TranscriptionPrompt_PinsFidelityDirectives()
    {
        var prompts = new PdfOcrPrompts();

        Assert.Contains("DO NOT translate", prompts.TranscriptionPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native digits", prompts.TranscriptionPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Markdown", prompts.TranscriptionPrompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptionSystemPrompt_StatesDeploymentContext()
    {
        var prompts = new PdfOcrPrompts();

        Assert.Contains("customer", prompts.TranscriptionSystemPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verbatim transcription", prompts.TranscriptionSystemPrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranscriptionSystemPrompt_PutsPagesWithPersonalInformationInScope()
    {
        var prompts = new PdfOcrPrompts();

        Assert.Contains("exactly as printed, including pages with personal information", prompts.TranscriptionSystemPrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JudgeSystemPrompt_PinsBadBiasAndEmitsNoMarkdown()
    {
        var prompts = new PdfOcrPrompts();

        Assert.Contains("choose bad", prompts.JudgeSystemPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<ocr_quality>", prompts.JudgeSystemPrompt!, StringComparison.Ordinal);
        Assert.DoesNotContain("<markdown>", prompts.JudgeSystemPrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JudgePrompt_CarriesTranscriptionPlaceholder()
    {
        var prompts = new PdfOcrPrompts();

        Assert.Contains("{TEXT_CONTENT}", prompts.JudgePrompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyTo_TransfersAllPrompts()
    {
        var source = new PdfOcrPrompts
        {
            TranscriptionSystemPrompt = "sys",
            TranscriptionPrompt = "transcribe",
            JudgeSystemPrompt = "judge-sys",
            JudgePrompt = "judge"
        };
        var target = new PdfOcrPrompts();

        source.CopyTo(target);

        Assert.Equal("sys", target.TranscriptionSystemPrompt);
        Assert.Equal("transcribe", target.TranscriptionPrompt);
        Assert.Equal("judge-sys", target.JudgeSystemPrompt);
        Assert.Equal("judge", target.JudgePrompt);
    }
}
