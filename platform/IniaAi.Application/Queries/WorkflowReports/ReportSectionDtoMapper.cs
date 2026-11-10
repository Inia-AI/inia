using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Dto.Citations;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using System.Text.Json;
using ContractStackingMode = IniaAi.WebApi.Contracts.Enums.ChartStackingMode;

namespace IniaAi.Application.Queries.WorkflowReports;

public static class ReportSectionDtoMapper
{
    public static ReportSectionDto ToDto(ReportSection section)
    {
        return new ReportSectionDto
        {
            Id = section.Id,
            Order = section.Order,
            Title = section.Title,
            Layout = section.ShownLayout() is { } layout ? ToDto(layout) : null,
            ExcludeFromWordExport = section.ExcludeFromWordExport,
            Paragraphs = [.. section.Paragraphs.OrderBy(paragraph => paragraph.Order).Select(ToDto)],
        };
    }

    public static ReportDataOutputDto ToDto(ReportDataOutput output)
    {
        return new ReportDataOutputDto
        {
            Name = output.Name,
            Data = JsonElement.Parse(output.Json),
            Citations = [.. output.Citations.Select(citation => new DataOutputCitationDto
            {
                Id = citation.Id,
                Sources = [.. citation.Sources.Select(ToDto)],
                IsNumerical = citation.IsNumerical,
                Path = citation.Path,
                Value = citation.Value,
            })],
            CitedSourcesByBlock = [.. output.CitedSourcesByBlock.Select(ToDto)],
        };
    }

    private static ReportParagraphDto ToDto(ReportParagraph paragraph)
    {
        return new ReportParagraphDto
        {
            Id = paragraph.Id,
            Kind = (ReportParagraphKindDto)paragraph.Kind,
            Order = paragraph.Order,
            Title = paragraph.Title,
            Subtitle = paragraph.Subtitle,
            Confidence = paragraph.Confidence,
            Content = paragraph.Content,
            ReplacementTag = paragraph.ReplacementTag,
            ChartPayload = paragraph.ChartPayload is null ? null : ToDto(paragraph.ChartPayload),
            TablePayload = paragraph.TablePayload is null ? null : ToDto(paragraph.TablePayload),
            Sources = [.. paragraph.Sources],
            Citations = [.. paragraph.Citations.Select(ToDto)],
            CitedSourcesByBlock = [.. paragraph.CitedSourcesByBlock.Select(ToDto)],
        };
    }

    private static BlockCitedSourcesDto ToDto(BlockCitedSources cited)
    {
        return new BlockCitedSourcesDto
        {
            BlockId = cited.BlockId,
            Sources = [.. cited.Sources.Select(ToDto)],
        };
    }

    private static CitationDto ToDto(Citation citation)
    {
        return new CitationDto
        {
            Id = citation.Id,
            Sources = [.. citation.Sources.Select(ToDto)],
            IsNumerical = citation.IsNumerical,
        };
    }

    private static CitationSourceDto ToDto(BaseSource source)
    {
        return source switch
        {
            DocumentSource document => new DocumentCitationSourceDto
            {
                CitedText = document.CitedText,
                PageId = document.PageId,
                PageNumber = document.PageNumber,
                DocumentName = document.DocumentName,
                FilePageNumber = document.FilePageNumber,
                Unverified = document.Unverified,
            },
            UrlSource url => new UrlCitationSourceDto { CitedText = url.CitedText, Url = url.Url },
            BlockSource block => new BlockCitationSourceDto
            {
                CitedText = block.CitedText,
                BlockId = block.BlockId,
                BlockTitle = block.BlockTitle,
                UpstreamSources = [.. block.UpstreamSources.Select(ToDto)],
                MatchingSources = [.. block.MatchingSources.Select(ToDto)],
            },
            _ => throw new InvalidOperationException($"No citation source contract exists for {source.GetType().Name}."),
        };
    }

    private static GridLayoutOutputDto ToDto(GridLayoutOutput layout)
    {
        return new GridLayoutOutputDto
        {
            Name = layout.Name,
            Mode = layout.Mode,
            Columns = layout.Columns,
            Elements = [.. layout.Elements.Select(element => new GridElementPositionDto
            {
                BlockOrder = element.BlockOrder,
                Row = element.Row,
                Column = element.Column,
                RowSpan = element.RowSpan,
                ColumnSpan = element.ColumnSpan,
            })],
        };
    }

    private static TableOutputDto ToDto(TableOutput table)
    {
        return new TableOutputDto
        {
            Name = table.Name,
            TableConfig = [.. table.TableConfig.Select(config => new TableConfigDto
            {
                Header = config.Header,
                Format = config.Format,
                GroupName = config.GroupName,
            })],
            TableData = [.. table.TableData.Select(row => row.ToList())],
        };
    }

    private static ChartOutputDto ToDto(ChartOutput chart)
    {
        ChartConfiguration config = chart.ChartConfig;
        return new ChartOutputDto
        {
            Name = chart.Name,
            ChartData = [.. chart.ChartData.Select(ToDto)],
            ChartConfig = new ChartConfigurationDto
            {
                Type = config.Type,
                Title = config.Title,
                Data = [.. config.Data.Select(ToDto)],
                XAxis = ToDto(config.XAxis),
                YAxis = ToDto(config.YAxis),
                Series = [.. config.Series.Select(series => new ChartSeriesDto
                {
                    Name = series.Name,
                    Data = [.. series.Data.Select(point => new ChartSeriesDataPointDto { X = point.X, Y = point.Y })],
                    Color = series.Color,
                })],
                Options = new ChartOptionsDto
                {
                    Responsive = config.Options.Responsive,
                    ShowLegend = config.Options.ShowLegend,
                    ColorScheme = config.Options.ColorScheme,
                    ShowGridLines = config.Options.ShowGridLines,
                    EnableAnimations = config.Options.EnableAnimations,
                    MaintainAspectRatio = config.Options.MaintainAspectRatio,
                    EnableExport = config.Options.EnableExport,
                    SeriesGroupField = config.Options.SeriesGroupField,
                    Stacking = (ContractStackingMode?)config.Options.Stacking,
                },
                Layout = new ChartLayoutDto { Width = config.Layout.Width, Height = config.Layout.Height },
                Metadata = new ChartMetadataDto
                {
                    Fields = new Dictionary<string, string>(config.Metadata.Fields),
                    DataPoints = config.Metadata.DataPoints,
                    GeneratedAt = config.Metadata.GeneratedAt,
                    Currency = config.Metadata.Currency,
                    Unit = config.Metadata.Unit,
                },
            },
        };
    }

    private static ChartDataPointDto ToDto(ChartDataPoint point)
    {
        return new ChartDataPointDto
        {
            X = point.X,
            Y = point.Y,
            Series = point.Series,
            RawData = new Dictionary<string, object>(point.RawData ?? []),
        };
    }

    private static ChartAxisDto ToDto(ChartAxis axis)
    {
        return new ChartAxisDto { Type = axis.Type, Field = axis.Field, Label = axis.Label };
    }
}
