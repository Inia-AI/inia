namespace IniaAi.Configuration.Platform;

/// <summary>
/// Decides which cloud the process runs in. Off EC2 the AWS SDK's metadata lookup fails after three
/// tries and 750 ms of sleeps, and the SDK caches no failure, so the lookup runs at most once per
/// process and never when a mode is named.
/// </summary>
public sealed class CloudHost
{
    public const string FORCE_AWS_MODE = "FORCE_AWS_MODE";
    public const string FORCE_AZURE_MODE = "FORCE_AZURE_MODE";

    private const string TRUE = "true";
    private const string FALSE = "false";

    private readonly Func<string, string?> _readVariable;
    private readonly Lazy<bool> _isOnEc2;

    public CloudHost(Func<string, string?> readVariable, Func<bool> isOnEc2)
    {
        _readVariable = readVariable;
        _isOnEc2 = new Lazy<bool>(isOnEc2);
    }

    public bool IsAzure => _readVariable(FORCE_AZURE_MODE) == TRUE;

    public bool IsAws => _readVariable(FORCE_AWS_MODE) switch
    {
        TRUE => true,
        FALSE => false,
        _ => !IsAzure && _isOnEc2.Value,
    };

    /// <summary>
    /// The deployment mode: a model gateway first, as the gateway serves the models wherever the host
    /// runs, then Azure, then AWS, else the direct APIs. For a gateway host it never asks which cloud
    /// the host runs in.
    /// </summary>
    public DeploymentMode ModeFor(bool usesModelGateway)
    {
        if (usesModelGateway)
        {
            return DeploymentMode.Gateway;
        }

        if (IsAzure)
        {
            return DeploymentMode.Azure;
        }

        return IsAws ? DeploymentMode.Aws : DeploymentMode.DirectApi;
    }
}
