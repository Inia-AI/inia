using IniaAi.Common;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The name this host stamps on the conversation turns it runs. It must survive a restart of the
/// same host, so startup recovery recognises the turns its previous process left in flight: the
/// configured <see cref="AgentConversationSettings.HostInstanceId"/>, or else the machine name and
/// the application name. A host that comes back under another name (a new container) never
/// recovers its old turns; the overdue sweep settles those instead.
/// </summary>
public sealed class AgentHostIdentity
{
    private const char SEPARATOR = '/';

    public AgentHostIdentity(IOptions<AgentConversationSettings> settings, IHostEnvironment? environment = null)
    {
        string instanceId = settings.Value.HostInstanceId.NullIfWhiteSpace()
            ?? Environment.MachineName + SEPARATOR + (environment?.ApplicationName ?? AppDomain.CurrentDomain.FriendlyName);

        InstanceId = instanceId.Truncate(AgentConversationTurn.HOST_INSTANCE_ID_MAX_LENGTH)!;
    }

    public string InstanceId { get; }
}
