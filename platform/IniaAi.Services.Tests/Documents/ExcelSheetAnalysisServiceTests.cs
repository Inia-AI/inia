using ClosedXML.Excel;
using IniaAi.Common.Enums;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Excel;
using IniaAi.Services.Documents;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using ServiceProvider = IniaAi.Testing.DependencyInjection.ServiceProvider;

namespace IniaAi.Services.Tests.Documents;

public class ExcelSheetAnalysisServiceTests : IClassFixture<DocumentsFixture>, IDisposable
{
    private const string MODEL_READS_THE_SHEET_LAYOUT = "The model reads the sheet layout";

    private readonly CultureInfo _originalCulture;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly DocumentsFixture _fixture;
    private readonly ExcelSheetAnalysisService _excelSheetAnalyzer;

    public ExcelSheetAnalysisServiceTests(DocumentsFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
        _excelSheetAnalyzer = new ExcelSheetAnalysisService(
            _fixture.GetChatConnector(_testOutputHelper),
            TestsLogger.CreateLogger<ExcelSheetAnalysisService>(_testOutputHelper),
            new ServiceScopeFactory(),
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [Fact]
    public async Task ChunkSheetsAsync_ReadsTheLayout_WithThePromptEditedInToolsSettingsAsync()
    {
        var serviceProvider = new ServiceProvider();
        using (IServiceScope scope = new ServiceScopeFactory(serviceProvider).CreateScope())
        {
            ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
            ExcelAnalysisPrompts edited = ExcelAnalysisPromptsDefaults.Create();
            edited.CreatedAt = DateTime.UtcNow;
            edited.LayoutAnalysisSystemPrompt = "Edited layout system prompt";
            coreUnitOfWork.Repository<ExcelAnalysisPrompts>().Add(edited);
            _ = await coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        }

        ScriptedChatConnector model = ScriptedChatConnector.Answering("<answer>{}</answer>");
        var analyzer = new ExcelSheetAnalysisService(
            model,
            TestsLogger.CreateLogger<ExcelSheetAnalysisService>(_testOutputHelper),
            new ServiceScopeFactory(serviceProvider),
            _fixture.GetTenantModelFamilyResolver(_testOutputHelper));
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(Path.Combine("TestResources", "Horizontal.xlsx"));

        await analyzer.ChunkSheetsAsync(document, workbook, chunkSize: 3, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Edited layout system prompt", model.Calls.First().SystemPrompt);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_MultipleTablesNoSharedColumns_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        Sheet1
        |Revenue| | |
        |Q1 2023| | |
        | | | |
        | | | |
        |BlackBook Private Equity| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |234500|145200|87300|
        |245600|152300|89100|
        |256700|158400|91200|
        """;
        string lastPageText =
        """
        Sheet1
        | | | |
        | | | |
        | | | |
        | | | |
        |BlackBook Global Fund| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |167188|100009|52332|
        |174685|103488|53606|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(12, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [IgnoreInCiCdFact]
    public async Task ChunkSheetsAsync_Horizontal_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "Horizontal.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        Sheet1
        |Some Item|234500|
        |Other Item|Some Data|
        |Branch 3|Some Other data|
        """;
        string lastPageText =
        """
        Sheet1
        |More items|268900|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_MultipleTablesSharedColumns_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        |Revenue| | | | |
        |Q1 2023| | | | |
        | | | | | |
        | |BlackBook Private Equity| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |May-24|234500|98700|145200|87300|
        |Jun-24|245600|102400|152300|89100|
        |Jul-24|256700|105600|158400|91200|
        """;
        string lastPageText =
        """
        |Revenue| | | | |
        |Q1 2023| | | | |
        | | | | | |
        | |BlackBook Hedge Fund| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |Feb-25|238840|87010|142870|74760|
        |Mar-25|249550|89040|147840|76580|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(8, document.Pages.Count);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_Database_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "Database.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |234500|98700|145200|87300|167400|89600|32450|45000|12.3|79800|
        |245600|102400|152300|89100|172500|92300|33100|44000|11.8|83400|
        |256700|105600|158400|91200|178600|94100|34200|43000|11.5|87600|
        """;
        string lastPageText =
        """
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |341200|124300|204100|106800|220900|111400|41900|36000|9.5|115700|
        |356500|127200|211200|109400|227800|114200|43200|35000|9.2|120400|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(4, document.Pages.Count);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessDatabaseLayout_ValidHeaders_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "Database.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.Database,
            HeaderRows = "3-8"
        };
        string page1Text =
        """
        Sheet1
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |234500|98700|145200|87300|167400|89600|32450|45000|12.3|79800|
        |245600|102400|152300|89100|172500|92300|33100|44000|11.8|83400|
        |256700|105600|158400|91200|178600|94100|34200|43000|11.5|87600|
        """;
        string lastPageText =
        """
        Sheet1
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |341200|124300|204100|106800|220900|111400|41900|36000|9.5|115700|
        |356500|127200|211200|109400|227800|114200|43200|35000|9.2|120400|
        """;

        // Act
        _excelSheetAnalyzer.ProcessDatabaseLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(4, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessDatabaseLayout_ValidHeadersEmptyPages_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "DatabaseEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.Database,
            HeaderRows = "3-8"
        };
        string page1Text =
        """
        Sheet1
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |298400|115900|184200|99600|201400|103600|38400|39000|10.4|102600|
        |312700|118600|190600|101900|207800|106100|39500|38000|10.1|106900|
        |326900|121400|197300|104300|214200|108700|40700|37000|9.8|111200|
        """;
        string lastPageText =
        """
        Sheet1
        |Revenue| | | | | | | | | |
        |Q1 2023| | | | | | | | | |
        | | | | | | | | | | |
        | | | | | | | | | | |
        |Assets|Liabilities|Income/Expenses| |Cash Flow| |Debt| |ROI|Total|
        |Revenue|Costs|Inflow|Outflow|Federal|Principal|%|Net| | |
        |---|---|---|---|---|---|---|---|---|---|
        |341200|124300|204100|106800|220900|111400|41900|36000|9.5|115700|
        |356500|127200|211200|109400|227800|114200|43200|35000|9.2|120400|
        """;

        // Act
        _excelSheetAnalyzer.ProcessDatabaseLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultipleTablesLayout_NoSharedColumns_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "7-9"
        };
        sheetAnalysisResult.TableColumns.AddRange(["A-C", "E-G", "I-K"]);
        string page1Text =
        """
        Sheet1
        |BlackBook Private Equity| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |234500|145200|87300|
        |245600|152300|89100|
        |256700|158400|91200|
        """;
        string lastPageText =
        """
        Sheet1
        |BlackBook Global Fund| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |167188|100009|52332|
        |174685|103488|53606|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(12, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultipleTablesLayout_NoSharedColumnsWithEmptyPages_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumnsEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "7-9"
        };
        sheetAnalysisResult.TableColumns.AddRange(["A-C", "E-G", "I-K"]);
        string page1Text =
        """
        Sheet1
        |BlackBook Private Equity| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |234500|145200|87300|
        |245600|152300|89100|
        |256700|158400|91200|
        """;
        string lastPageText =
        """
        Sheet1
        |BlackBook Global Fund| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |167188|100009|52332|
        |174685|103488|53606|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(9, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultipleTablesLayout_SharedColumns_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "6-8",
        };
        sheetAnalysisResult.TableColumns.AddRange(["B-E", "G-J"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);
        string page1Text =
        """
        Sheet1
        | |BlackBook Private Equity| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |May-24|234500|98700|145200|87300|
        |Jun-24|245600|102400|152300|89100|
        |Jul-24|256700|105600|158400|91200|
        """;
        string lastPageText =
        """
        Sheet1
        | |BlackBook Hedge Fund| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |Feb-25|238840|87010|142870|74760|
        |Mar-25|249550|89040|147840|76580|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(8, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultipleTablesLayout_SharedColumnsEmptyPages_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumnsEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "6-8",
        };
        sheetAnalysisResult.TableColumns.AddRange(["B-E", "G-J"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);
        string page1Text =
        """
        | |BlackBook Private Equity| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |May-24|234500|98700|145200|87300|
        |Jun-24|245600|102400|152300|89100|
        |Jul-24|256700|105600|158400|91200|
        """;
        string lastPageText =
        """
        | |BlackBook Hedge Fund| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |Feb-25|238840|87010|142870|74760|
        |Mar-25|249550|89040|147840|76580|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(6, document.Pages.Count);
        Assert.StartsWith("Sheet1", document[1]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Sheet1", document[document.Pages.Count]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_DatabaseWithEmptyRows_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "DatabaseEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        |298400|115900|184200|99600|201400|103600|38400|39000|10.4|102600|
        |312700|118600|190600|101900|207800|106100|39500|38000|10.1|106900|
        |326900|121400|197300|104300|214200|108700|40700|37000|9.8|111200|
        """;
        string lastPageText =
        """
        |341200|124300|204100|106800|220900|111400|41900|36000|9.5|115700|
        |356500|127200|211200|109400|227800|114200|43200|35000|9.2|120400|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(2, document.Pages.Count);
        Assert.StartsWith("Sheet1", document[1]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Sheet1", document[document.Pages.Count]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_MultipleTablesNoSharedColumnsWithEmptyRows_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumnsEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        |Revenue| | |
        |Q1 2023| | |
        | | | |
        | | | |
        |BlackBook Private Equity| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |234500|145200|87300|
        |245600|152300|89100|
        |256700|158400|91200|
        """;
        string lastPageText =
        """
        |BlackBook Global Fund| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |167188|100009|52332|
        |174685|103488|53606|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(9, document.Pages.Count);
        Assert.StartsWith("Sheet1", document[1]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Sheet1", document[document.Pages.Count]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [PaidApiFact(MODEL_READS_THE_SHEET_LAYOUT)]
    public async Task ChunkSheetsAsync_MultipleTablesSharedColumnsWithEmptyRows_AnalyzesSheetsAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumnsEmptyRows.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        string page1Text =
        """
        |Revenue| | | | |
        |Q1 2023| | | | |
        | | | | | |
        | |BlackBook Private Equity| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |May-24|234500|98700|145200|87300|
        |Jun-24|245600|102400|152300|89100|
        |Jul-24|256700|105600|158400|91200|
        """;
        string lastPageText =
        """
        |Revenue| | | | |
        |Q1 2023| | | | |
        | | | | | |
        | |BlackBook Hedge Fund| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |Feb-25|238840|87010|142870|74760|
        |Mar-25|249550|89040|147840|76580|
        """;

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(6, document.Pages.Count);
        Assert.StartsWith("Sheet1", document[1]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Sheet1", document[document.Pages.Count]?.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(page1Text.ReplaceLineEndings(), document[1]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(lastPageText.ReplaceLineEndings(), document[document.Pages.Count]?.RawText?.ReplaceLineEndings(), StringComparison.OrdinalIgnoreCase);
    }

    [IgnoreInCiCdFact]
    public async Task ChunkSheetsAsync_HiddenSheet_SkipsHiddenSheet()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        using IWorkbookDocument workbook = OpenWorkbook(built =>
        {
            IXLWorksheet visibleSheet1 = built.AddWorksheet("VisibleSheet1");
            visibleSheet1.Cell("A1").Value = "Data1";
            visibleSheet1.Cell("B1").Value = "Data2";
            visibleSheet1.Cell("A2").Value = "Value1";
            visibleSheet1.Cell("B2").Value = "Value2";

            IXLWorksheet hiddenSheet = built.AddWorksheet("HiddenSheet");
            hiddenSheet.Cell("A1").Value = "HiddenData";
            hiddenSheet.Cell("B1").Value = "HiddenValue";
            hiddenSheet.Visibility = XLWorksheetVisibility.Hidden;

            IXLWorksheet visibleSheet2 = built.AddWorksheet("VisibleSheet2");
            visibleSheet2.Cell("A1").Value = "Data1";
            visibleSheet2.Cell("B1").Value = "Data2";
            visibleSheet2.Cell("A2").Value = "Value3";
            visibleSheet2.Cell("B2").Value = "Value4";
        });

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 10);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(2, document.Pages.Count); // Only 2 visible sheets
        Assert.Contains("VisibleSheet1", document[1]?.RawText);
        Assert.Contains("Data1", document[1]?.RawText);
        Assert.Contains("Data2", document[1]?.RawText);
        Assert.Contains("Value1", document[1]?.RawText);
        Assert.Contains("Value2", document[1]?.RawText);
        Assert.Contains("VisibleSheet2", document[2]?.RawText);
        Assert.Contains("Data1", document[2]?.RawText);
        Assert.Contains("Data2", document[2]?.RawText);
        Assert.Contains("Value3", document[2]?.RawText);
        Assert.Contains("Value4", document[2]?.RawText);

        // Verify hidden sheet is not in any page
        foreach (Page page in document.Pages)
        {
            Assert.DoesNotContain("HiddenData", page.RawText ?? string.Empty);
            Assert.DoesNotContain("HiddenSheet", page.RawText ?? string.Empty);
        }
    }

    [IgnoreInCiCdFact]
    public async Task ChunkSheetsAsync_SmallTable_ProducesAtLeastOnePageAsync()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "smallTableTest.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "smallTableTest.xlsx");
        _testOutputHelper.WriteLine($"Loading workbook from: {fileName}");

        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        _testOutputHelper.WriteLine($"Workbook loaded. NumberOfSheets: {workbook.Sheets.Count}");
        for (int i = 0; i < workbook.Sheets.Count; i++)
        {
            IWorkbookSheet sheet = workbook.Sheets[i];
            _testOutputHelper.WriteLine($"Sheet[{i}] Name='{sheet.Name}' LastRow={sheet.MaxDataRow} LastColumn={sheet.MaxDataColumn}");
        }

        // Act
        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            chunkSize: 3);

        // Assert
        Assert.NotNull(document);
        _testOutputHelper.WriteLine($"Resulting document.Pages.Count: {document.Pages.Count}");
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            _testOutputHelper.WriteLine($"--- Page {i} ---");
            _testOutputHelper.WriteLine(document[i]?.RawText ?? "<null>");
        }

        Assert.True(
            document.Pages.Count >= 1,
            $"Expected at least 1 page, but got {document.Pages.Count}");
    }

    [Fact]
    public void ProcessDatabaseLayout_HeaderRowsCoverAllData_ProducesAtLeastOnePage()
    {
        // Arrange: small sheet (rows 2-5, cols A-B) where the AI classified
        // every row as a header row. CreatePages currently loops over data rows
        // BELOW the header and produces zero pages — the header content is lost.
        var document = new Document(new FileMetadata("1234", "smallTableTest.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "smallTableTest.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.Database,
            HeaderRows = "2-5"
        };

        // Act
        _excelSheetAnalyzer.ProcessDatabaseLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        _testOutputHelper.WriteLine($"Database layout resulting document.Pages.Count: {document.Pages.Count}");
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            _testOutputHelper.WriteLine($"--- Page {i} ---");
            _testOutputHelper.WriteLine(document[i]?.RawText ?? "<null>");
        }

        Assert.True(
            document.Pages.Count >= 1,
            $"Expected at least 1 page, but got {document.Pages.Count}");
        Assert.Contains("Larkspur", document[1]?.RawText ?? string.Empty);
    }

    [Fact]
    public void ProcessMultiNoSharedColumnsLayout_HeaderRowsCoverAllData_ProducesAtLeastOnePage()
    {
        // Arrange: same small sheet, but classified as MultiNoSharedColumns
        // with a single table A-B where headers cover every row.
        var document = new Document(new FileMetadata("1234", "smallTableTest.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "smallTableTest.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "2-5"
        };
        sheetAnalysisResult.TableColumns.AddRange(["A-B"]);

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        _testOutputHelper.WriteLine($"MultiNoSharedColumns layout resulting document.Pages.Count: {document.Pages.Count}");
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            _testOutputHelper.WriteLine($"--- Page {i} ---");
            _testOutputHelper.WriteLine(document[i]?.RawText ?? "<null>");
        }

        Assert.True(
            document.Pages.Count >= 1,
            $"Expected at least 1 page, but got {document.Pages.Count}");
        Assert.Contains("Larkspur", document[1]?.RawText ?? string.Empty);
    }

    [Fact]
    public void ProcessMultiSharedColumnsLayout_HeaderRowsCoverAllData_ProducesAtLeastOnePage()
    {
        // Arrange: same small sheet, classified as MultiSharedColumns with A
        // as the shared column and B as the single data column, headers cover
        // every row.
        var document = new Document(new FileMetadata("1234", "smallTableTest.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "smallTableTest.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "2-5"
        };
        sheetAnalysisResult.TableColumns.AddRange(["B-B"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        _testOutputHelper.WriteLine($"MultiSharedColumns layout resulting document.Pages.Count: {document.Pages.Count}");
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            _testOutputHelper.WriteLine($"--- Page {i} ---");
            _testOutputHelper.WriteLine(document[i]?.RawText ?? "<null>");
        }

        Assert.True(
            document.Pages.Count >= 1,
            $"Expected at least 1 page, but got {document.Pages.Count}");
        Assert.Contains("Larkspur", document[1]?.RawText ?? string.Empty);
    }

    [Fact]
    public void ProcessDatabaseLayout_HeaderRangeHasNoContent_DoesNotEmitEmptyPage()
    {
        // Arrange: real data lives in row 1 (0-indexed 0) but analysis declares
        // HeaderRows="3-5" — rows that are all blank. The fallback path must not
        // emit a visually empty page containing only pipes/separator rows.
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        using IWorkbookDocument workbook = OpenWorkbook(built =>
        {
            IXLWorksheet sheet = built.AddWorksheet("Sheet1");
            sheet.Cell("A1").Value = "RealData";
            sheet.Cell("B1").Value = "42";
        });

        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.Database,
            HeaderRows = "3-5"
        };

        // Act
        _excelSheetAnalyzer.ProcessDatabaseLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        _testOutputHelper.WriteLine($"Pages.Count: {document.Pages.Count}");
        for (int i = 1; i <= document.Pages.Count; i++)
        {
            _testOutputHelper.WriteLine($"--- Page {i} ---");
            _testOutputHelper.WriteLine(document[i]?.RawText ?? "<null>");
        }

        Assert.Empty(document.Pages);
    }

    [Fact]
    public void ProcessMultiNoSharedColumnsLayout_SingleColumnFormat_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "7-9"
        };
        // Test single column format - should extract only column A from first table
        sheetAnalysisResult.TableColumns.AddRange(["A"]);
        string expectedText =
        """
        Sheet1
        |BlackBook Private Equity|
        |Assets|
        |Revenue|
        |---|
        |234500|
        |245600|
        |256700|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.Equal(4, document.Pages.Count);
        Assert.Equal(expectedText, document[1]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultiNoSharedColumnsLayout_MixedSingleAndRangeFormat_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "7-9"
        };
        // Test mixed format: single column "A" and range "B-C"
        // Each column specification creates separate pages
        sheetAnalysisResult.TableColumns.AddRange(["A", "B-C"]);
        string expectedPage1Text =
        """
        Sheet1
        |BlackBook Private Equity|
        |Assets|
        |Revenue|
        |---|
        |234500|
        |245600|
        |256700|
        """;
        string expectedPage5Text =
        """
        Sheet1
        | | |
        |Income/Expenses| |
        |Inflow|Outflow|
        |---|---|
        |145200|87300|
        |152300|89100|
        |158400|91200|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        // Column A creates 4 pages, Column B-C creates 4 pages = 8 total
        Assert.True(document.Pages.Count >= 5, $"Expected at least 5 pages, got {document.Pages.Count}");
        Assert.Equal(expectedPage1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(expectedPage5Text, document[5]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultiSharedColumnsLayout_SingleColumnFormat_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "6-8",
        };
        // Test single column format with shared column
        sheetAnalysisResult.TableColumns.AddRange(["B"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);
        string expectedText =
        """
        Sheet1
        | |BlackBook Private Equity|
        | |Assets|
        |Month|Revenue|
        |---|---|
        |May-24|234500|
        |Jun-24|245600|
        |Jul-24|256700|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.True(document.Pages.Count >= 1, $"Expected at least 1 page, got {document.Pages.Count}");
        Assert.Equal(expectedText, document[1]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultiSharedColumnsLayout_MixedSingleAndRangeFormat_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "6-8",
        };
        // Test mixed format: single column "B" and range "C-D"
        // Each column specification creates separate pages
        sheetAnalysisResult.TableColumns.AddRange(["B", "C-D"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        Assert.True(document.Pages.Count >= 2, $"Expected at least 2 pages, got {document.Pages.Count}");

        // Verify that column B data (Revenue) is present in at least one page
        bool hasRevenueColumn = document.Pages.Any(p =>
            p.RawText?.Contains("Revenue") == true &&
            p.RawText?.Contains("234500") == true &&
            !p.RawText?.Contains("Costs") == true); // Costs is in column C
        Assert.True(hasRevenueColumn, "Expected to find pages with Revenue column (B) data");

        // Verify that columns C-D data (Costs and Inflow) are present in at least one page
        bool hasCostsInflowColumns = document.Pages.Any(p =>
            p.RawText?.Contains("Costs") == true &&
            p.RawText?.Contains("Inflow") == true &&
            p.RawText?.Contains("98700") == true); // Value from Costs column
        Assert.True(hasCostsInflowColumns, "Expected to find pages with Costs and Inflow columns (C-D) data");
    }

    [Fact]
    public void ProcessMultiNoSharedColumnsLayout_InvalidColumnFormat_SkipsInvalidColumns()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesNoSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiNoSharedColumns,
            HeaderRows = "7-9"
        };
        // Test invalid formats: empty string, too many dashes
        // Valid column "A-C" should still be processed
        sheetAnalysisResult.TableColumns.AddRange(["", "A-B-C", "A-C"]);
        string expectedText =
        """
        Sheet1
        |BlackBook Private Equity| | |
        |Assets|Income/Expenses| |
        |Revenue|Inflow|Outflow|
        |---|---|---|
        |234500|145200|87300|
        |245600|152300|89100|
        |256700|158400|91200|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiNoSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        // Should only process the valid "A-C" column range, invalid formats are skipped
        Assert.True(document.Pages.Count >= 1, $"Expected at least 1 page, got {document.Pages.Count}");
        Assert.Equal(expectedText, document[1]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultiSharedColumnsLayout_InvalidColumnFormat_SkipsInvalidColumns()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "TestWorkbook.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultipleTablesSharedColumns.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "6-8",
        };
        // Test invalid formats: empty string, too many dashes
        // Valid columns "B-E" should still be processed
        sheetAnalysisResult.TableColumns.AddRange(["", "B-C-D", "B-E"]);
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);
        string expectedText =
        """
        Sheet1
        | |BlackBook Private Equity| | | |
        | |Assets|Liabilities|Income/Expenses| |
        |Month|Revenue|Costs|Inflow|Outflow|
        |---|---|---|---|---|
        |May-24|234500|98700|145200|87300|
        |Jun-24|245600|102400|152300|89100|
        |Jul-24|256700|105600|158400|91200|
        """;

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        // Should only process the valid "B-E" column range, invalid formats are skipped
        Assert.True(document.Pages.Count >= 1, $"Expected at least 1 page, got {document.Pages.Count}");
        Assert.Equal(expectedText, document[1]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessDatabaseLayout_SheetWithCustomFormats_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "CustomFormats.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "CustomFormats.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.Database,
            HeaderRows = "2-4"
        };
        string page1Text =
        """
        _History
        | | | | | | | | |
        |SuperMega Fund| | | | | | | |
        |Pricing Date|NAV calculation date|NAV|Reinvestment factor|Adjusted NAV|Dividend|Fund size (class)|Fund size (compartment)|
        |---|---|---|---|---|---|---|---|
        |16/12/2025|17/12/2025|183.65| |183.65| |41.697M|1.615B|
        """;

        // Act
        _excelSheetAnalyzer.ProcessDatabaseLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 3,
            operationId: "1234");

        // Assert
        Assert.NotNull(document);
        _ = Assert.Single(document.Pages);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ProcessMultipleTablesLayout_WideMultiTable_SharedColumns_CreatesValidPages()
    {
        // Arrange
        var document = new Document(new FileMetadata("funds", "MultiTableFunds.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        string fileName = Path.Combine("TestResources", "MultiTableFunds.xlsx");
        using IWorkbookDocument workbook = FileFormatsUnderTest.OpenWorkbook(fileName);
        SheetAnalysisResult sheetAnalysisResult = new()
        {
            WorksheetLayoutType = WorksheetLayoutTypes.MultiSharedColumns,
            HeaderRows = "1-4",
        };
        sheetAnalysisResult.SharedColumns.AddRange(["A"]);

        // 52 fund groups: each has 4 data columns separated by an empty column
        // Fund 1: B-E, Fund 2: G-J, Fund 3: L-O, ...
        for (int i = 0; i < 52; i++)
        {
            int startCol = 2 + (i * 5);
            int endCol = startCol + 3;
            string startLetter = Common.Utils.ConvertIntToExcelColumnLetters(startCol);
            string endLetter = Common.Utils.ConvertIntToExcelColumnLetters(endCol);
            sheetAnalysisResult.TableColumns.Add($"{startLetter}-{endLetter}");
        }

        // Act
        _excelSheetAnalyzer.ProcessMultiSharedColumnsLayout(
            document,
            workbook.Sheets[0],
            sheetAnalysisResult,
            chunkSize: 50,
            operationId: "multi-table-funds-test");

        // Expected outputs come from the data the synthetic-fixtures generator wrote (tools/synthetic-fixtures):
        // Fund 1 (cols B-E): Fund ID 238, Corvane Global Macro Fund Ltd.
        // Fund 52 (cols IW-IZ): Fund ID 10666, Corvane Real Assets Portfolio LLC
        // 46 data rows each (rows 5-50), dates in col A formatted as M/D/YY
        // NAV: #,##0 format, Percentages: 0.00% format
        string page1Text =
        """
        Sheet1
        | |Fund ID 238| | | |
        | |NAV|MonthlyGross|YTDGross|CUMGross|
        | | | | | |
        | |Corvane Global Macro Fund Ltd.|Corvane Global Macro Fund Ltd.|Corvane Global Macro Fund Ltd.|Corvane Global Macro Fund Ltd.|
        |---|---|---|---|---|
        |01/31/2020|517,243,623|1.98%|1.98%|141.80%|
        |02/28/2020|524,870,077|-1.07%|0.89%|139.21%|
        |03/31/2020|524,565,562|-1.69%|-0.82%|135.17%|
        |04/30/2020|534,771,182|-0.05%|-0.87%|135.05%|
        |05/29/2020|534,509,269|2.03%|1.14%|139.82%|
        |06/30/2020|553,372,584|1.98%|3.14%|144.57%|
        |07/31/2020|573,042,131|1.32%|4.50%|147.80%|
        |08/31/2020|547,901,411|-2.36%|2.03%|141.95%|
        |09/30/2020|566,997,615|2.55%|4.63%|148.12%|
        |10/30/2020|568,492,049|-1.93%|2.61%|143.33%|
        |11/30/2020|592,076,231|2.11%|4.78%|148.46%|
        |12/31/2020|609,026,975|0.44%|5.24%|149.55%|
        |01/29/2021|617,090,059|-1.20%|-1.20%|146.56%|
        |02/26/2021|604,759,184|-2.46%|-3.63%|140.49%|
        |03/31/2021|620,840,105|1.98%|-1.72%|145.25%|
        |04/30/2021|618,242,946|-1.15%|-2.85%|142.43%|
        |05/31/2021|652,503,574|2.95%|0.02%|149.58%|
        |06/30/2021|660,597,519|0.23%|0.25%|150.15%|
        |07/30/2021|658,541,541|-2.11%|-1.87%|144.87%|
        |08/31/2021|652,102,543|-0.32%|-2.18%|144.09%|
        |09/30/2021|658,864,195|1.46%|-0.75%|147.65%|
        |10/29/2021|678,492,060|2.63%|1.86%|154.16%|
        |11/30/2021|686,420,535|-0.37%|1.48%|153.22%|
        |12/31/2021|684,124,996|1.59%|3.09%|157.25%|
        |01/31/2022|686,816,361|2.98%|2.98%|164.92%|
        |02/28/2022|669,291,770|-0.77%|2.19%|162.88%|
        |03/31/2022|667,134,537|-1.29%|0.87%|159.49%|
        |04/29/2022|684,834,311|0.07%|0.94%|159.67%|
        |05/31/2022|685,361,642|0.97%|1.92%|162.19%|
        |06/30/2022|684,653,712|1.57%|3.52%|166.31%|
        |07/29/2022|712,312,434|1.92%|5.51%|171.42%|
        |08/31/2022|682,504,970|-2.19%|3.20%|165.48%|
        |09/30/2022|687,202,570|2.91%|6.20%|173.21%|
        |10/31/2022|713,362,167|2.37%|8.72%|179.69%|
        |11/30/2022|706,287,987|-0.23%|8.47%|179.05%|
        |12/30/2022|672,371,069|-2.17%|6.12%|172.99%|
        |01/31/2023|674,779,452|-0.96%|-0.96%|170.37%|
        |02/28/2023|659,370,412|-1.42%|-2.37%|166.53%|
        |03/31/2023|698,538,505|3.16%|0.72%|174.95%|
        |04/28/2023|701,585,599|0.58%|1.30%|176.54%|
        |05/31/2023|706,987,371|-1.24%|0.04%|173.11%|
        |06/30/2023|723,949,633|1.29%|1.33%|176.63%|
        |07/31/2023|698,688,599|-0.76%|0.56%|174.53%|
        |08/31/2023|687,506,536|-0.17%|0.39%|174.06%|
        |09/29/2023|690,973,759|0.25%|0.64%|174.75%|
        |10/31/2023|685,358,896|-2.16%|-1.53%|168.82%|
        """;
        string lastPageText =
        """
        Sheet1
        | |Fund ID 10666| | | |
        | |NAV|MonthlyGross|YTDGross|CUMGross|
        | | | | | |
        | |Corvane Real Assets Portfolio LLC|Corvane Real Assets Portfolio LLC|Corvane Real Assets Portfolio LLC|Corvane Real Assets Portfolio LLC|
        |---|---|---|---|---|
        |01/31/2020|691,327,940|1.67%|1.67%|532.75%|
        |02/28/2020|718,895,683|1.07%|2.76%|539.52%|
        |03/31/2020|732,469,231|2.97%|5.81%|558.51%|
        |04/30/2020|722,745,812|1.54%|7.44%|568.65%|
        |05/29/2020|724,864,211|-1.81%|5.50%|556.55%|
        |06/30/2020|714,860,005|0.96%|6.51%|562.85%|
        |07/31/2020|711,325,995|-0.34%|6.15%|560.60%|
        |08/31/2020|706,888,524|0.58%|6.77%|564.43%|
        |09/30/2020|683,084,964|-0.50%|6.24%|561.11%|
        |10/30/2020|724,887,112|3.32%|9.77%|583.06%|
        |11/30/2020|724,961,886|2.13%|12.11%|597.61%|
        |12/31/2020|696,063,202|-2.07%|9.79%|583.17%|
        |01/29/2021|679,450,404|-1.15%|-1.15%|575.31%|
        |02/26/2021|706,032,377|2.20%|1.02%|590.17%|
        |03/31/2021|679,105,121|-1.17%|-0.16%|582.10%|
        |04/30/2021|705,070,525|1.88%|1.72%|594.92%|
        |05/31/2021|709,394,551|-1.87%|-0.18%|581.92%|
        |06/30/2021|711,572,415|-0.40%|-0.58%|579.19%|
        |07/30/2021|710,281,874|-1.60%|-2.17%|568.32%|
        |08/31/2021|744,733,279|2.39%|0.17%|584.29%|
        |09/30/2021|723,868,572|-1.58%|-1.41%|573.48%|
        |10/29/2021|731,173,452|0.82%|-0.60%|579.00%|
        |11/30/2021|766,337,796|2.15%|1.54%|593.60%|
        |12/31/2021|755,340,474|0.44%|1.99%|596.65%|
        |01/31/2022|753,284,353|0.56%|0.56%|600.55%|
        |02/28/2022|766,959,284|1.66%|2.23%|612.18%|
        |03/31/2022|767,816,631|-1.64%|0.55%|600.50%|
        |04/29/2022|795,216,556|3.21%|3.78%|622.99%|
        |05/31/2022|775,565,168|-2.11%|1.59%|607.73%|
        |06/30/2022|796,356,527|1.84%|3.46%|620.75%|
        |07/29/2022|820,484,316|0.55%|4.03%|624.71%|
        |08/31/2022|816,851,865|2.15%|6.27%|640.29%|
        |09/30/2022|814,224,848|-1.94%|4.21%|625.93%|
        |10/31/2022|839,241,915|3.35%|7.70%|650.25%|
        |11/30/2022|843,165,861|-1.95%|5.60%|635.62%|
        |12/30/2022|833,526,891|-1.79%|3.71%|622.45%|
        |01/31/2023|800,853,609|-1.47%|-1.47%|611.83%|
        |02/28/2023|817,332,393|2.09%|0.59%|626.71%|
        |03/31/2023|827,471,703|0.90%|1.50%|633.25%|
        |04/28/2023|815,396,319|-0.31%|1.19%|630.98%|
        |05/31/2023|838,059,943|2.10%|3.31%|646.33%|
        |06/30/2023|847,722,771|-1.08%|2.19%|638.27%|
        |07/31/2023|829,059,744|-0.54%|1.64%|634.28%|
        |08/31/2023|831,413,678|1.35%|3.01%|644.19%|
        |09/29/2023|873,206,771|3.12%|6.22%|667.41%|
        |10/31/2023|890,607,616|0.62%|6.88%|672.17%|
        """;

        // Assert
        Assert.NotNull(document);
        Assert.Equal(52, document.Pages.Count);
        Assert.Equal(page1Text, document[1]?.RawText, ignoreLineEndingDifferences: true);
        Assert.Equal(lastPageText, document[document.Pages.Count]?.RawText, ignoreLineEndingDifferences: true);

        // Every page should have exactly 5 columns (1 shared + 4 data)
        foreach (Page page in document.Pages)
        {
            string? text = page.RawText;
            Assert.NotNull(text);

            string[] lines = text.Split('\n');
            string? separatorLine = lines.FirstOrDefault(l => l.Contains("---|"));
            Assert.NotNull(separatorLine);

            int columnCount = separatorLine.Count(c => c == '|') - 1;
            Assert.Equal(5, columnCount);
        }
    }

    [Fact]
    public async Task ChunkSheetsAsync_StampedWorkbook_UsesDefaultRuleOnePagePerVisibleSheet()
    {
        // When the workbook carries the analysis-config stamp (block-generated files),
        // the service must bypass LLM layout analysis and emit one page per visible sheet
        // with the full markdown of its used area. The very-hidden config sheet itself
        // must not appear in the output.
        byte[] workbookBytes = OoxmlFixtures.BuiltWorkbook(built =>
        {
            IXLWorksheet summary = built.AddWorksheet("Summary");
            summary.Cell("A1").Value = "Company";
            summary.Cell("B1").Value = "Revenue";
            summary.Cell("A2").Value = "Acme";
            summary.Cell("B2").Value = 1000;

            IXLWorksheet detail = built.AddWorksheet("Detail");
            detail.Cell("A1").Value = "Item";
            detail.Cell("B1").Value = "Amount";
            detail.Cell("A2").Value = "Widget";
            detail.Cell("B2").Value = 42;
        });

        byte[] stampedBytes = ExcelAnalysisConfigSheet.Stamp(
            FileFormatsUnderTest.EditableWorkbooks,
            workbookBytes,
            new ExcelAnalysisConfig(
                ExcelAnalysisConfigSheet.CURRENT_SCHEMA_VERSION,
                ExcelAnalysisSource.BlockGenerated));

        using IWorkbookDocument workbook = FileFormatsUnderTest.Workbooks.Open(new MemoryStream(stampedBytes), "block-generated.xlsx");
        var document = new Document(new FileMetadata("org", "block-generated.xlsx", []), MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);

        _excelSheetAnalyzer.ResetTokenAccumulator();

        await _excelSheetAnalyzer.ChunkSheetsAsync(
            document,
            workbook,
            cancellationToken: TestContext.Current.CancellationToken);

        // Most important: no LLM tokens consumed. This is what distinguishes the
        // rule-based branch from the default LLM chunking path — the LLM never runs.
        Assert.Equal(0, _excelSheetAnalyzer.AccumulatedInputTokens);
        Assert.Equal(0, _excelSheetAnalyzer.AccumulatedOutputTokens);

        Assert.Equal(2, document.Pages.Count);
        Assert.Contains("Summary", document[1]?.RawText);
        Assert.Contains("Acme", document[1]?.RawText);
        Assert.Contains("1000", document[1]?.RawText);
        Assert.Contains("Detail", document[2]?.RawText);
        Assert.Contains("Widget", document[2]?.RawText);
        Assert.Contains("42", document[2]?.RawText);

        foreach (Page page in document.Pages)
        {
            Assert.DoesNotContain(ExcelAnalysisConfigSheet.SHEET_NAME, page.RawText ?? string.Empty);
            Assert.DoesNotContain("SchemaVersion", page.RawText ?? string.Empty);
            Assert.DoesNotContain("ConfigJson", page.RawText ?? string.Empty);
        }
    }

    private static IWorkbookDocument OpenWorkbook(Action<XLWorkbook> build)
    {
        return FileFormatsUnderTest.Workbooks.Open(new MemoryStream(OoxmlFixtures.BuiltWorkbook(build)), "TestWorkbook.xlsx");
    }
}
