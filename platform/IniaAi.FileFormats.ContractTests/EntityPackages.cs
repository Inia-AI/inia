using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.ContractTests;

/// <summary>
/// An Office package with one XML part rewritten to open with a document type declaration, as a stranger can write
/// it: entities that a reader must not fetch, read in or expand.
/// </summary>
public static partial class EntityPackages
{
    public const string ENTITY_TEXT = "entity-text";

    /// <summary>Text that appears only where a reader expanded the nested entities: the declaration holds the text once.</summary>
    public const string EXPANDED_TEXT = ENTITY_TEXT + ENTITY_TEXT;

    // A reader that hangs on a declaration has failed already; the cap only stops the run waiting on it.
    public static readonly TimeSpan ReadTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>The part declares an external parameter entity at <paramref name="dtd"/>, so a reader that processes it fetches it.</summary>
    public static byte[] NamingAnExternalDtd(byte[] package, string part, Uri dtd)
    {
        return WithDeclaration(package, part, $"<!ENTITY % external SYSTEM \"{dtd.AbsoluteUri}\"> %external;", reference: null);
    }

    /// <summary>The first text of the part refers to an entity that holds the file at <paramref name="path"/>.</summary>
    public static byte[] ReadingInAFile(byte[] package, string part, string path)
    {
        return WithDeclaration(package, part, $"<!ENTITY file SYSTEM \"{new Uri(path).AbsoluteUri}\">", "&file;");
    }

    /// <summary>The first text of the part refers to three levels of three entities around <see cref="ENTITY_TEXT"/>.</summary>
    public static byte[] WithNestedEntities(byte[] package, string part)
    {
        return WithDeclaration(package, part, $"<!ENTITY a \"{ENTITY_TEXT}\"><!ENTITY b \"&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;\">", "&c;");
    }

    private static byte[] WithDeclaration(byte[] package, string part, string internalSubset, string? reference)
    {
        using var stream = new MemoryStream();
        stream.Write(package);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            ZipArchiveEntry entry = zip.GetEntry(part) ?? throw new ArgumentException($"The package has no part {part}.", nameof(part));
            string xml;
            using (var reader = new StreamReader(entry.Open()))
            {
                xml = reader.ReadToEnd();
            }

            entry.Delete();
            string body = XmlDeclaration().Replace(xml, string.Empty, 1);
            string root = RootElement().Match(body).Groups[1].Value;
            if (reference is not null)
            {
                string referenced = FirstText().Replace(body, $"$1{reference}", 1);
                body = referenced != body ? referenced : throw new ArgumentException($"The part {part} has no text element.", nameof(part));
            }

            using var writer = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE {root} [{internalSubset}]>{body}");
        }

        return stream.ToArray();
    }

    [GeneratedRegex(@"^\s*<\?xml[^?]*\?>")]
    private static partial Regex XmlDeclaration();

    [GeneratedRegex(@"<([\w:]+)")]
    private static partial Regex RootElement();

    [GeneratedRegex(@"(<(?:\w+:)?t(?:\s[^>]*)?>)")]
    private static partial Regex FirstText();
}

/// <summary>
/// A listener on the loopback interface that counts the connections a reader opens to it and keeps the first line of
/// each request, then closes the connection.
/// </summary>
public sealed class LoopbackProbe : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly Task _accepting;
    private int _connections;

    public LoopbackProbe()
    {
        _listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync();
    }

    public Uri Address { get; }

    public int Connections => Volatile.Read(ref _connections);

    public IReadOnlyCollection<string> Requests => _requests;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _ = _accepting.Exception;
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Interlocked.Increment(ref _connections);
            _ = KeepRequestLineAsync(client);
        }
    }

    private async Task KeepRequestLineAsync(TcpClient client)
    {
        using (client)
        {
            byte[] buffer = new byte[1024];
            int read = await client.GetStream().ReadAsync(buffer, _stop.Token).AsTask().WaitAsync(RequestLineTimeLimit, _stop.Token);
            _requests.Enqueue(Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0].TrimEnd('\r'));
        }
    }

    // A client that connects and sends nothing still counts; the cap only stops the probe waiting on it.
    private static readonly TimeSpan RequestLineTimeLimit = TimeSpan.FromSeconds(5);
}
