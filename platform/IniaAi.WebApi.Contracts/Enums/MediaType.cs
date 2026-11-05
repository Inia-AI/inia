using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Represents supported media (MIME) types for handling content in the application.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// JPEG image format (image/jpeg).
    /// </summary>
    [EnumMember(Value = "image/jpeg")]
    ImageJpeg,

    /// <summary>
    /// PNG image format (image/png).
    /// </summary>
    [EnumMember(Value = "image/png")]
    ImagePng,

    /// <summary>
    /// GIF image format (image/gif).
    /// </summary>
    [EnumMember(Value = "image/gif")]
    ImageGif,

    /// <summary>
    /// WebP image format (image/webp).
    /// </summary>
    [EnumMember(Value = "image/webp")]
    ImageWebp,

    /// <summary>
    /// SVG vector image format (image/svg+xml).
    /// </summary>
    [EnumMember(Value = "image/svg+xml")]
    ImageSvg,

    /// <summary>
    /// TIFF image format (image/tiff).
    /// </summary>
    [EnumMember(Value = "image/tiff")]
    ImageTiff,

    /// <summary>
    /// Bitmap image format (image/bmp).
    /// </summary>
    [EnumMember(Value = "image/bmp")]
    ImageBmp,

    /// <summary>
    /// Plain text format (text/plain).
    /// </summary>
    [EnumMember(Value = "text/plain")]
    TextPlain,

    /// <summary>
    /// CSV (Comma-Separated Values) text format (text/csv).
    /// </summary>
    [EnumMember(Value = "text/csv")]
    TextCsv,

    /// <summary>
    /// AAC audio format (audio/aac).
    /// </summary>
    [EnumMember(Value = "audio/aac")]
    AudioAac,

    /// <summary>
    /// MP3 audio format (audio/mpeg).
    /// </summary>
    [EnumMember(Value = "audio/mpeg")]
    AudioMp3,

    /// <summary>
    /// Ogg audio format (audio/ogg).
    /// </summary>
    [EnumMember(Value = "audio/ogg")]
    AudioOgg,

    /// <summary>
    /// WAV audio format (audio/wav).
    /// </summary>
    [EnumMember(Value = "audio/wav")]
    AudioWav,

    /// <summary>
    /// PDF document format (application/pdf).
    /// </summary>
    [EnumMember(Value = "application/pdf")]
    ApplicationPdf,

    /// <summary>
    /// JSON data format (application/json).
    /// </summary>
    [EnumMember(Value = "application/json")]
    ApplicationJson,

    /// <summary>
    /// XML data format (application/xml).
    /// </summary>
    [EnumMember(Value = "application/xml")]
    ApplicationXml,

    /// <summary>
    /// Rich Text Format (application/rtf).
    /// </summary>
    [EnumMember(Value = "application/rtf")]
    ApplicationRtf,

    /// <summary>
    /// ZIP archive format (application/zip).
    /// </summary>
    [EnumMember(Value = "application/zip")]
    ApplicationZip,

    /// <summary>
    /// Microsoft Excel (legacy) format (application/vnd.ms-excel).
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-excel")]
    ApplicationVndMsExcel,

    /// <summary>
    /// Microsoft Excel (OpenXML) format (application/vnd.openxmlformats-officedocument.spreadsheetml.sheet).
    /// </summary>
    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet,

    /// <summary>
    /// Microsoft Word (OpenXML) format (application/vnd.openxmlformats-officedocument.wordprocessingml.document).
    /// </summary>
    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument,

    /// <summary>
    /// Unknown or unsupported media type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Markdown text (text/markdown).
    /// </summary>
    [EnumMember(Value = "text/markdown")]
    TextMarkdown,

    /// <summary>
    /// Represents email messages in RFC 822 format.
    /// </summary>
    [EnumMember(Value = "message/rfc822")]
    MessageRfc822,

    /// <summary>
    /// Represents Microsoft Outlook email files.
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-outlook")]
    ApplicationVndMsOutlook,

    /// <summary>
    /// Macro-enabled Excel workbook format (application/vnd.ms-excel.sheet.macroEnabled.12).
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-excel.sheet.macroEnabled.12")]
    ApplicationVndMsExcelSheetMacroEnabled12,

    /// <summary>
    /// Legacy binary Word document format (application/msword).
    /// </summary>
    [EnumMember(Value = "application/msword")]
    ApplicationMsWord,

    /// <summary>
    /// Legacy binary PowerPoint presentation format (application/vnd.ms-powerpoint).
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-powerpoint")]
    ApplicationVndMsPowerpoint,

    /// <summary>
    /// PowerPoint presentation format (application/vnd.openxmlformats-officedocument.presentationml.presentation).
    /// </summary>
    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    ApplicationVndOpenXmlFormatsOfficeDocumentPresentationMlPresentation,

    /// <summary>
    /// OpenDocument text format (application/vnd.oasis.opendocument.text).
    /// </summary>
    [EnumMember(Value = "application/vnd.oasis.opendocument.text")]
    ApplicationVndOasisOpendocumentText,

    /// <summary>
    /// OpenDocument spreadsheet format (application/vnd.oasis.opendocument.spreadsheet).
    /// </summary>
    [EnumMember(Value = "application/vnd.oasis.opendocument.spreadsheet")]
    ApplicationVndOasisOpendocumentSpreadsheet,

    /// <summary>
    /// OpenDocument presentation format (application/vnd.oasis.opendocument.presentation).
    /// </summary>
    [EnumMember(Value = "application/vnd.oasis.opendocument.presentation")]
    ApplicationVndOasisOpendocumentPresentation,

    /// <summary>
    /// HTML format (text/html).
    /// </summary>
    [EnumMember(Value = "text/html")]
    TextHtml,

    /// <summary>
    /// CSS stylesheet format (text/css).
    /// </summary>
    [EnumMember(Value = "text/css")]
    TextCss,

    /// <summary>
    /// JavaScript source format (application/javascript).
    /// </summary>
    [EnumMember(Value = "application/javascript")]
    ApplicationJavascript,

    /// <summary>
    /// TypeScript source format (text/typescript).
    /// </summary>
    [EnumMember(Value = "text/typescript")]
    TextTypescript,

    /// <summary>
    /// YAML format (application/x-yaml).
    /// </summary>
    [EnumMember(Value = "application/x-yaml")]
    ApplicationXYaml,

    /// <summary>
    /// Icon image format (image/x-icon).
    /// </summary>
    [EnumMember(Value = "image/x-icon")]
    ImageIcon,

    /// <summary>
    /// MP4 audio format (audio/mp4).
    /// </summary>
    [EnumMember(Value = "audio/mp4")]
    AudioMp4,

    /// <summary>
    /// MP4 video format (video/mp4).
    /// </summary>
    [EnumMember(Value = "video/mp4")]
    VideoMp4,

    /// <summary>
    /// AVI video format (video/x-msvideo).
    /// </summary>
    [EnumMember(Value = "video/x-msvideo")]
    VideoAvi,

    /// <summary>
    /// Matroska video format (video/x-matroska).
    /// </summary>
    [EnumMember(Value = "video/x-matroska")]
    VideoMkv,

    /// <summary>
    /// QuickTime video format (video/quicktime).
    /// </summary>
    [EnumMember(Value = "video/quicktime")]
    VideoQuicktime,

    /// <summary>
    /// WebM video format (video/webm).
    /// </summary>
    [EnumMember(Value = "video/webm")]
    VideoWebm,

    /// <summary>
    /// RAR archive format (application/x-rar-compressed).
    /// </summary>
    [EnumMember(Value = "application/x-rar-compressed")]
    ApplicationRar,

    /// <summary>
    /// 7-Zip archive format (application/x-7z-compressed).
    /// </summary>
    [EnumMember(Value = "application/x-7z-compressed")]
    Application7z,

    /// <summary>
    /// TAR archive format (application/x-tar).
    /// </summary>
    [EnumMember(Value = "application/x-tar")]
    ApplicationTar,

    /// <summary>
    /// Gzip archive format (application/gzip).
    /// </summary>
    [EnumMember(Value = "application/gzip")]
    ApplicationGzip,

    /// <summary>
    /// C# source format (text/x-csharp).
    /// </summary>
    [EnumMember(Value = "text/x-csharp")]
    TextCSharp,

    /// <summary>
    /// Python source format (text/x-python).
    /// </summary>
    [EnumMember(Value = "text/x-python")]
    TextPython,

    /// <summary>
    /// Java source format (text/x-java-source).
    /// </summary>
    [EnumMember(Value = "text/x-java-source")]
    TextJava,

    /// <summary>
    /// C++ source format (text/x-c++src).
    /// </summary>
    [EnumMember(Value = "text/x-c++src")]
    TextCpp,

    /// <summary>
    /// C source format (text/x-csrc).
    /// </summary>
    [EnumMember(Value = "text/x-csrc")]
    TextC,

    /// <summary>
    /// C header format (text/x-chdr).
    /// </summary>
    [EnumMember(Value = "text/x-chdr")]
    TextCHeader,

    /// <summary>
    /// Arbitrary binary data (application/octet-stream).
    /// </summary>
    [EnumMember(Value = "application/octet-stream")]
    ApplicationOctetStream,

    /// <summary>
    /// TSV (Tab-Separated Values) text format (text/tab-separated-values).
    /// </summary>
    [EnumMember(Value = "text/tab-separated-values")]
    TextTabSeparatedValues
}
