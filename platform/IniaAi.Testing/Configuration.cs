using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using IniaAi.Configuration;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Testing;

public static class Configuration
{
    /// <summary>
    /// Names the Parameter Store environment whose shared test secrets the loader reads, such as
    /// <c>unittests</c>. Unset, the tests read no Parameter Store and take every setting from
    /// environment variables, so they run with no AWS credentials.
    /// </summary>
    public const string SSM_ENVIRONMENT_VARIABLE = "INIAAI_SSM_ENVIRONMENT";

    private const string API_KEY_SUFFIX = "ApiKey";
    private const string PERSONAL_ACCESS_TOKEN = "Pat";

    private static readonly HashSet<string> _freeSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlatformSettings:ExchangeRateApi:ApiKey",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, string?>> _parameterStore = new(ReadParameterStore, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// ONLY FOR TESTING PURPOSES, to get a configuration builder with the
    /// environment variables, over the AWS SSM Parameter Store values when
    /// <see cref="SSM_ENVIRONMENT_VARIABLE"/> names an environment.
    /// </summary>
    /// <param name="args"></param>
    /// <returns>A <see cref="IConfigurationRoot"/> object. Use
    /// <code>
    /// IConfigurationRoot config = Configuration.GetConfigurationRoot(args);
    /// var key = config["PlatformSettings:Anthropic:ApiKey"];
    /// </code>
    /// to get the configuration value.
    /// </returns>
    [Obsolete("This method is only for testing purposes.")]
    public static IConfigurationRoot GetConfigurationRoot(string[] args)
    {
        IReadOnlyDictionary<string, string?> parameters = _parameterStore.Value;

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(parameters)
            .AddEnvironmentVariables()
            .Build();

        if (PaidApi.IsAllowed())
        {
            return configuration;
        }

        return new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(WithheldKeys(parameters.Keys, configuration.AsEnumerable().Select(setting => setting.Key))
                .ToDictionary(key => key, _ => (string?)PaidApi.WITHHELD_KEY))
            .Build();
    }

    /// <summary>
    /// ONLY FOR TESTING PURPOSES, to get a service provider with the
    /// configuration settings.
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns>A <see cref="IServiceProvider"/> object. Use
    /// <code>
    /// IServiceProvider serviceProvider = Configuration.GetServiceProvider(config);
    /// var settings = serviceProvider.GetRequiredService<IOptions<PlatformSettings>>().Value;
    /// </code>
    /// to get the configuration settings.
    /// </returns>
    [Obsolete("This method is only for testing purposes.")]
    public static IServiceProvider GetServiceProvider(IConfigurationRoot configuration)
    {
        IServiceCollection serviceCollection = new ServiceCollection();
        _ = serviceCollection.Configure<PlatformSettings>(configuration.GetSection("PlatformSettings"));
        return serviceCollection.BuildServiceProvider();
    }

    public static bool ReadsParameterStore()
    {
        return ParameterStoreEnvironment() is not null;
    }

    /// <summary>
    /// The settings that a run without <see cref="PaidApi.OPT_IN_VARIABLE"/> never hands out: every
    /// stored setting, and every configured setting named <c>…ApiKey</c> or <c>Pat</c> whatever its
    /// source, so a key exported in the shell is withheld as the stored one is. A free setting is
    /// handed out.
    /// </summary>
    internal static IEnumerable<string> WithheldKeys(IEnumerable<string> storedKeys, IEnumerable<string> configuredKeys)
    {
        return storedKeys
            .Concat(configuredKeys.Where(IsCredential))
            .Where(key => !_freeSettings.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCredential(string key)
    {
        string name = key[(key.LastIndexOf(':') + 1)..];

        return name.EndsWith(API_KEY_SUFFIX, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PERSONAL_ACCESS_TOKEN, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParameterStoreEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable(SSM_ENVIRONMENT_VARIABLE);

        return string.IsNullOrEmpty(environment) ? null : environment;
    }

    private static IReadOnlyDictionary<string, string?> ReadParameterStore()
    {
        if (ParameterStoreEnvironment() is not string environment)
        {
            return new Dictionary<string, string?>();
        }

        string[] ssmPaths =
        [
            $"/iniaai/{environment}/shared/secrets",
        ];

        var parameters = new Dictionary<string, string?>();

        using var client = new AmazonSimpleSystemsManagementClient();

        foreach (string ssmPath in ssmPaths)
        {
            var request = new GetParametersByPathRequest
            {
                Path = ssmPath,
                Recursive = true,
                WithDecryption = true,
            };

            do
            {
                GetParametersByPathResponse response = client.GetParametersByPathAsync(request)
                    .GetAwaiter().GetResult();

                foreach (Parameter parameter in response.Parameters)
                {
                    if (!string.IsNullOrEmpty(parameter.Value))
                    {
                        // Strip path prefix and convert / to : for .NET config key
                        // e.g. /iniaai/dev/shared/secrets/PlatformSettings/Anthropic/ApiKey
                        //   → PlatformSettings:Anthropic:ApiKey
                        string configKey = parameter.Name[(ssmPath.Length + 1)..].Replace("/", ":");
                        parameters[configKey] = parameter.Value;
                    }
                }

                request.NextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(request.NextToken));
        }

        return parameters;
    }
}
