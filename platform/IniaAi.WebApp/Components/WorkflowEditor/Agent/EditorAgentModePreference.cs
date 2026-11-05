using Microsoft.JSInterop;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>Whether Agent mode was on when the user last left the editor, kept in the browser.</summary>
public static class EditorAgentModePreference
{
    private const string ON = "on";
    private const string OFF = "off";
    private const string STORAGE_KEY_PREFIX = "inia.editorAgentMode.";

    public static async Task<bool> ReadAsync(IJSRuntime jsRuntime, string userId)
    {
        return await BrowserStorage.ReadAsync(jsRuntime, STORAGE_KEY_PREFIX + userId) == ON;
    }

    /// <summary>Whether the user has ever opened or closed Agent mode in this browser.</summary>
    public static async Task<bool> WasEverChosenAsync(IJSRuntime jsRuntime, string userId)
    {
        return await BrowserStorage.ReadAsync(jsRuntime, STORAGE_KEY_PREFIX + userId) is not null;
    }

    public static ValueTask WriteAsync(IJSRuntime jsRuntime, string userId, bool isOn)
    {
        return BrowserStorage.WriteAsync(jsRuntime, STORAGE_KEY_PREFIX + userId, isOn ? ON : OFF);
    }
}

/// <summary>Whether the user has seen the hint that points at the agent's entry, kept in the browser.</summary>
public static class EditorAgentHintPreference
{
    public const string STORAGE_KEY_PREFIX = "inia.editorAgentHint.";
    public const string SEEN = "seen";

    /// <summary>Seen once dismissed, or once the user has used Agent mode at all.</summary>
    public static async Task<bool> WasSeenAsync(IJSRuntime jsRuntime, string userId)
    {
        return await BrowserStorage.ReadAsync(jsRuntime, STORAGE_KEY_PREFIX + userId) == SEEN
            || await EditorAgentModePreference.WasEverChosenAsync(jsRuntime, userId);
    }

    public static ValueTask MarkSeenAsync(IJSRuntime jsRuntime, string userId)
    {
        return BrowserStorage.WriteAsync(jsRuntime, STORAGE_KEY_PREFIX + userId, SEEN);
    }
}

internal static class BrowserStorage
{
    private const string GET_ITEM_JS = "localStorage.getItem";
    private const string SET_ITEM_JS = "localStorage.setItem";

    public static ValueTask<string?> ReadAsync(IJSRuntime jsRuntime, string key)
    {
        return jsRuntime.InvokeAsync<string?>(GET_ITEM_JS, key);
    }

    public static ValueTask WriteAsync(IJSRuntime jsRuntime, string key, string value)
    {
        return jsRuntime.InvokeVoidAsync(SET_ITEM_JS, key, value);
    }
}
