namespace IniaAi.Common.Interfaces.Services;

/// <summary>
/// Whether the current request has started a model call. A service marks it just before it calls
/// the connector, so a call that the provider bills and that then fails still counts, and anything
/// later in the request, a filter included, can ask without knowing which service made the call.
/// </summary>
public interface IRequestModelUse
{
    void MarkModelCalled();

    bool ModelCalled { get; }
}
