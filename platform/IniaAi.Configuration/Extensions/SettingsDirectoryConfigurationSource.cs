using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.KeyPerFile;
using Microsoft.Extensions.FileProviders;

namespace IniaAi.Configuration.Extensions;

/// <summary>
/// Reads one settings directory the way <c>AddKeyPerFile</c> does, and also strips the trailing
/// newline of a file written on another system: the framework strips only
/// <see cref="Environment.NewLine"/>, so on Windows a file ending in <c>\n</c> kept it, and on Linux
/// a file ending in <c>\r\n</c> kept its <c>\r</c>.
/// </summary>
internal sealed class SettingsDirectoryConfigurationSource(string directory) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SettingsKeyPerFileConfigurationProvider(new KeyPerFileConfigurationSource
        {
            FileProvider = new PhysicalFileProvider(directory),
        });
    }
}

// The name keeps "KeyPerFile": the startup summary labels a setting's source by provider name.
internal sealed class SettingsKeyPerFileConfigurationProvider(KeyPerFileConfigurationSource source)
    : KeyPerFileConfigurationProvider(source)
{
    public override void Load()
    {
        base.Load();

        foreach (string key in Data.Keys.ToList())
        {
            Data[key] = WithoutTheNewlineTheFrameworkLeft(Data[key]);
        }
    }

    private static string? WithoutTheNewlineTheFrameworkLeft(string? value)
    {
        string leftover = Environment.NewLine == "\r\n" ? "\n" : "\r";

        return value is not null && value.EndsWith(leftover, StringComparison.Ordinal)
            ? value[..^1]
            : value;
    }
}
