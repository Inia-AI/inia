using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// A process-wide cap on in-flight WebApiCall requests per target host. The key is the host
/// name plus, when the request carries a credential header, a hash of that credential, so two
/// organisations calling the same API on different keys get separate gates and one cannot starve
/// the other. Each key's semaphore is sized from the limit the caller resolves when the entry is
/// created, and the entry is dropped again once the last holder or waiter has left it, so a
/// credential minted per execution does not accumulate entries for the life of the process.
/// The cap is per process: hosts running several engine processes multiply it.
/// </summary>
public sealed class WebApiCallHostGate
{
    private const int CREDENTIAL_HASH_HEX_LENGTH = 16;
    private const char KEY_SEPARATOR = ':';

    /// <summary>The headers that carry a credential, matched case-insensitively.</summary>
    public static IReadOnlyList<string> CredentialHeaderNames { get; } =
        ["Authorization", "X-API-Key", "Api-Key"];

    public static WebApiCallHostGate Shared { get; } = new();

    private readonly ConcurrentDictionary<string, Entry> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// How many keys currently have a holder or a waiter. Diagnostic only.
    /// </summary>
    public int ActiveKeys => _gates.Count;

    /// <summary>
    /// Builds the gate key for a request: the lower-cased host, suffixed with a truncated
    /// SHA-256 of the first credential header present. Headers are matched case-insensitively.
    /// The credential itself never leaves this method.
    /// </summary>
    public static string BuildKey(string host, IReadOnlyDictionary<string, string> headers)
    {
        string normalizedHost = host.ToLowerInvariant();

        foreach (string credentialHeaderName in CredentialHeaderNames)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (header.Key.Equals(credentialHeaderName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(header.Value))
                {
                    return $"{normalizedHost}{KEY_SEPARATOR}{HashCredential(header.Value)}";
                }
            }
        }

        return normalizedHost;
    }

    /// <summary>
    /// Waits for a slot under <paramref name="key"/> and returns the releaser. Dispose it in a
    /// <c>finally</c>; a cancelled wait acquires nothing and needs no release.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, int limit, CancellationToken cancellationToken)
    {
        int effectiveLimit = Math.Max(1, limit);

        while (true)
        {
            Entry entry = _gates.GetOrAdd(key, _ => new Entry(effectiveLimit));

            // A retired entry is one that was removed between our GetOrAdd and this check;
            // loop so we join (or create) the live entry rather than a dead one.
            lock (entry.Lock)
            {
                if (entry.Retired)
                {
                    continue;
                }

                entry.Members++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                Leave(key, entry);
                throw;
            }

            return new Releaser(this, key, entry);
        }
    }

    private void Leave(string key, Entry entry)
    {
        lock (entry.Lock)
        {
            entry.Members--;

            if (entry.Members == 0)
            {
                entry.Retired = true;
                _ = _gates.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }
    }

    private static string HashCredential(string credential)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(credential));
        return Convert.ToHexString(hash)[..CREDENTIAL_HASH_HEX_LENGTH];
    }

    /// <summary>
    /// One key's semaphore plus the count of holders and waiters that keep it alive.
    /// </summary>
    private sealed class Entry(int limit)
    {
        public SemaphoreSlim Semaphore { get; } = new(limit, limit);

        public object Lock { get; } = new();

        public int Members { get; set; }

        public bool Retired { get; set; }
    }

    private sealed class Releaser(WebApiCallHostGate owner, string key, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _ = entry.Semaphore.Release();
                owner.Leave(key, entry);
            }
        }
    }
}
