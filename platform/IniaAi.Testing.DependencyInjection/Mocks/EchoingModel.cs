namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// The model the workflow tests run against. It answers a request that carries an image with a fixed
/// sentence and anything else with the request itself, so a test sees which prompt and pages reached the model.
/// </summary>
public static class EchoingModel
{
    public const string IMAGE_ANSWER = "An image reached the model.";

    public static string Answer(ScriptedModelCall call)
    {
        return call.CarriesAnImage ? IMAGE_ANSWER : call.Text;
    }
}
