namespace IniaAi.WebApi.Swagger;

/// <summary>
/// Provides an example instance of <typeparamref name="T"/> for Swagger documentation.
/// </summary>
/// <typeparam name="T">The type to provide an example for.</typeparam>
public interface IExamplesProvider<out T>
{
    /// <summary>
    /// Returns a sample instance of <typeparamref name="T"/>.
    /// </summary>
    T GetExamples();
}
