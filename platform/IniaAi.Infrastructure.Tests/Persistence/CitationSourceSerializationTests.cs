using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Infrastructure.Persistence.Serialization.Citations;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class CitationSourceSerializationTests
{
    private static Citation RoundTrip(Citation citation)
    {
        string json = JsonSerializer.Serialize(citation, CitationsJsonDefaults.Options);
        return JsonSerializer.Deserialize<Citation>(json, CitationsJsonDefaults.Options)
            ?? throw new InvalidOperationException("Round trip produced null");
    }

    [Fact]
    public void RoundTrip_UnverifiedDocumentSource_KeepsTheFlagAndTheQuote()
    {
        var citation = new Citation
        {
            Id = "c0",
            Sources =
            [
                new DocumentSource
                {
                    CitedText = "Impairment of right-of-use assets 19,800,000",
                    DocumentId = "doc-1",
                    DocumentName = "ar.pdf",
                    FilePageNumber = 5,
                    Unverified = true,
                },
            ],
        };

        Citation restored = RoundTrip(citation);

        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(restored.Sources));
        Assert.True(source.Unverified);
        Assert.Equal(string.Empty, source.PageId);
        Assert.Equal("Impairment of right-of-use assets 19,800,000", source.CitedText);
        Assert.Equal(5, source.FilePageNumber);
    }

    [Fact]
    public void RoundTrip_ResolvedDocumentSource_StaysVerifiedByDefault()
    {
        var citation = new Citation
        {
            Id = "c1",
            Sources =
            [
                new DocumentSource
                {
                    CitedText = "Total assets 4,027,553,656",
                    DocumentId = "doc-1",
                    PageId = "page-1",
                    PageNumber = 2,
                    FilePageNumber = 2,
                },
            ],
        };

        Citation restored = RoundTrip(citation);

        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(restored.Sources));
        Assert.False(source.Unverified);
        Assert.Equal("page-1", source.PageId);
    }

    [Fact]
    public void Deserialize_StoredJsonWithoutTheUnverifiedProperty_ReadsAsVerified()
    {
        const string storedJson = """
            {"id":"c0","sources":[{"type":"Document","citedText":"Total assets 4,027",
             "documentId":"doc-1","pageId":"page-1","pageNumber":2,"filePageNumber":2}],"isNumerical":true}
            """;

        Citation? restored = JsonSerializer.Deserialize<Citation>(storedJson, CitationsJsonDefaults.Options);

        Assert.NotNull(restored);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(restored.Sources));
        Assert.False(source.Unverified);
    }

    [Fact]
    public void RoundTrip_BlockSourceWithMatchingSources_KeepsThem()
    {
        var citation = new Citation
        {
            Id = "c0",
            Sources =
            [
                new BlockSource
                {
                    BlockId = "block-synthesis",
                    BlockTitle = "Key Principals",
                    CitedText = "a paraphrase",
                    MatchingSources =
                    [
                        new UrlSource { Url = "https://example.org/people/alex-rivera", CitedText = "start date: 2011-05-01" },
                        new DocumentSource { DocumentId = "doc-1", PageId = "page-12", PageNumber = 12, DocumentName = "Fund.pdf", CitedText = "since May 2011" },
                    ],
                },
            ],
        };

        Citation restored = RoundTrip(citation);

        BlockSource source = Assert.IsType<BlockSource>(Assert.Single(restored.Sources));
        Assert.Collection(
            source.MatchingSources,
            match => Assert.Equal("https://example.org/people/alex-rivera", Assert.IsType<UrlSource>(match).Url),
            match => Assert.Equal("page-12", Assert.IsType<DocumentSource>(match).PageId));
    }

    [Fact]
    public void Deserialize_StoredBlockSourceWithoutMatchingSources_ReadsAsEmpty()
    {
        const string storedJson = """
            {"id":"c0","sources":[{"type":"Block","citedText":"x","blockId":"b1","upstreamSources":[]}],"isNumerical":true}
            """;

        Citation? restored = JsonSerializer.Deserialize<Citation>(storedJson, CitationsJsonDefaults.Options);

        Assert.NotNull(restored);
        Assert.Empty(Assert.IsType<BlockSource>(Assert.Single(restored.Sources)).MatchingSources);
    }

    [Fact]
    public void Serialize_BlockSourceWithoutMatchingSources_WritesNoMatchingSourcesProperty()
    {
        var citation = new Citation { Id = "c0", Sources = [new BlockSource { BlockId = "b1", CitedText = "x" }] };

        string json = JsonSerializer.Serialize(citation, CitationsJsonDefaults.Options);

        Assert.DoesNotContain("matchingSources", json);
    }

    [Fact]
    public void RoundTrip_BlockCitedSources_KeepsTheBlockAndItsSourcesInOrder()
    {
        List<BlockCitedSources> lists =
        [
            new BlockCitedSources
            {
                BlockId = "block-synthesis",
                Sources =
                [
                    new DocumentSource { DocumentId = "doc-1", PageId = "page-12", PageNumber = 12, DocumentName = "Fund.pdf", CitedText = "Net assets" },
                    new UrlSource { Url = "https://example.org/team", CitedText = "Our team" },
                ],
            },
        ];

        string json = JsonSerializer.Serialize(lists, CitationsJsonDefaults.Options);
        List<BlockCitedSources>? restored = JsonSerializer.Deserialize<List<BlockCitedSources>>(json, CitationsJsonDefaults.Options);

        BlockCitedSources entry = Assert.Single(restored!);
        Assert.Equal("block-synthesis", entry.BlockId);
        Assert.Collection(
            entry.Sources,
            source => Assert.Equal(12, Assert.IsType<DocumentSource>(source).PageNumber),
            source => Assert.Equal("https://example.org/team", Assert.IsType<UrlSource>(source).Url));
    }
}
