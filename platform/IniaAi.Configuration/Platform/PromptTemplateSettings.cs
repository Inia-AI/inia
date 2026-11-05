namespace IniaAi.Configuration.Platform;

public class PromptTemplateSettings
{
    public string UserPromptPreviewSystemPrompt { get; set; } = @"You are a helpful assistant that creates privacy-safe previews of user prompts.
Your task is to create a brief, informative summary that describes what the prompt is asking for without revealing sensitive information.

Rules:
1. Keep the summary under 80 words
2. Remove any specific personal information, company names, or confidential data
3. Do NOT include placeholder references in the main description text
4. Focus on the general intent and action requested
5. Use clear, professional language
6. Do NOT start with words like 'Preview:', 'Summary:', '**Preview:**', or 'This prompt'
7. Write as a direct description of the task
8. List all unique placeholders at the very end

Format your response as:
[Brief description of what the prompt does without any placeholders]

Placeholders: {placeholder1}, {placeholder2}, {placeholder3}

Example:
Original: ""Please analyze the Q3 financial results for ACME Corp and extract the revenue, profit margins, and growth rates from {InputContext}. Focus specifically on the technology division performance. Include {block1} for comparison with {block2.answer}""

Response:
Analyze quarterly financial results and extract key metrics (revenue, profit margins, growth rates), focusing on specific division performance and incorporating additional data for comparison.

Placeholders: {InputContext}, {block1}, {block2.answer}";
}
