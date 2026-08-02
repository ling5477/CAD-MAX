using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadMax.Contracts;

/// <summary>The only document-level operations admitted by the Phase 1.4 route.</summary>
[JsonConverter(typeof(DrawingOperationJsonConverter))]
public enum DrawingOperation
{
    Status,
    ListDocuments,
    ActiveDocument,
    Units,
    Bounds,
    Layouts,
    SystemMetadata,
}

/// <summary>Strict request for the fixed read-only drawing inspection route.</summary>
public sealed record DrawingInspectRequest(
    string SchemaVersion,
    string RequestId,
    string TraceId,
    DateTimeOffset DeadlineUtc,
    string ExpectedInstanceId,
    [property: JsonConverter(typeof(DrawingOperationJsonConverter))]
    DrawingOperation Operation,
    string? ExpectedDocumentId);

/// <summary>Execution context used to obtain an inspection result.</summary>
public enum DrawingExecutionContext
{
    ApplicationContext,
    DocumentCommandContext,
}

/// <summary>Explicitly records that no mutation authority was used.</summary>
public enum DrawingReadMode
{
    ReadOnly,
}

/// <summary>Common evidence carried by every successful drawing inspection.</summary>
public abstract record DrawingInspectionData(
    string InstanceId,
    string DispatchId,
    [property: JsonConverter(typeof(DrawingOperationJsonConverter))]
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string? ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed);

/// <summary>Runtime and document availability without reading a Database.</summary>
public sealed record DrawingStatusData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string? ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    string RuntimeState,
    string DocumentState,
    int DocumentCount)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

/// <summary>Safe document-list item; the display name never contains a directory.</summary>
public sealed record DrawingDocumentData(
    string DocumentId,
    string DisplayName,
    bool IsUntitled,
    bool IsActive,
    bool ContextAvailable);

/// <summary>Bounded list of open documents.</summary>
public sealed record DocumentListData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string? ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    IReadOnlyList<DrawingDocumentData> Documents,
    int DocumentCount)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

/// <summary>Safe facts about the execution-time active document.</summary>
public sealed record ActiveDocumentData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    string DocumentId,
    string DisplayName,
    bool IsUntitled,
    bool IsQuiescent,
    string DocumentState)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

public enum DrawingInsertionUnits
{
    Unknown,
    Unitless,
    Inches,
    Feet,
    Miles,
    Millimeters,
    Centimeters,
    Meters,
    Kilometers,
    Microinches,
    Mils,
    Yards,
    Angstroms,
    Nanometers,
    Microns,
    Decimeters,
    Dekameters,
    Hectometers,
    Gigameters,
    AstronomicalUnits,
    LightYears,
    Parsecs,
    UsSurveyFeet,
    UsSurveyInches,
    UsSurveyYards,
    UsSurveyMiles,
}

[JsonConverter(typeof(DrawingLinearFormatJsonConverter))]
public enum DrawingLinearFormat
{
    Unknown,
    Scientific,
    DecimalFormat,
    Engineering,
    Architectural,
    Fractional,
}

public enum DrawingAngularFormat
{
    Unknown,
    DecimalDegrees,
    DegreesMinutesSeconds,
    Gradians,
    Radians,
    SurveyorUnits,
}

/// <summary>Stable unit metadata without raw Autodesk enum values.</summary>
public sealed record DrawingUnitsData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    DrawingInsertionUnits InsertionUnits,
    [property: JsonConverter(typeof(DrawingLinearFormatJsonConverter))]
    DrawingLinearFormat LinearFormat,
    int LinearPrecision,
    DrawingAngularFormat AngularFormat,
    int AngularPrecision,
    bool Unitless,
    double? MillimetersPerDrawingUnit)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

public enum DrawingBoundsState
{
    Available,
    Empty,
}

public enum DrawingBoundsSource
{
    DatabaseExtents,
}

public enum DrawingCoordinateSystem
{
    Wcs,
}

public sealed record DrawingPointData(double X, double Y, double Z);

/// <summary>Current Database extents; the value is never recomputed.</summary>
public sealed record DrawingBoundsData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    DrawingBoundsState BoundsState,
    DrawingBoundsSource Source,
    DrawingCoordinateSystem CoordinateSystem,
    DrawingPointData? Minimum,
    DrawingPointData? Maximum,
    DrawingPointData? Size,
    bool ExtentsMayBeStale)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

public sealed record DrawingLayoutData(
    string Name,
    bool IsModel,
    int TabOrder,
    bool IsCurrent);

/// <summary>Bounded LayoutDictionary projection with no ObjectId or plot configuration.</summary>
public sealed record DrawingLayoutsData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    IReadOnlyList<DrawingLayoutData> Layouts,
    int LayoutCount)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

public enum DrawingFileFormatVersion
{
    Unknown,
    AcadR12,
    AcadR13,
    AcadR14,
    Acad2000,
    Acad2004,
    Acad2007,
    Acad2010,
    Acad2013,
    Acad2018,
}

public enum DrawingCurrentSpace
{
    Unknown,
    ModelSpace,
    PaperSpace,
}

/// <summary>Strict allowlist of non-personal, non-path Database metadata.</summary>
public sealed record DrawingSystemMetadataData(
    string InstanceId,
    string DispatchId,
    DrawingOperation Operation,
    bool MainThreadVerified,
    DrawingExecutionContext ExecutionContext,
    string ActiveDocumentId,
    long QueueDelayMs,
    long ExecutionMs,
    DrawingReadMode ReadMode,
    bool TransactionUsed,
    bool FileBacked,
    DrawingFileFormatVersion FileFormatVersion,
    bool TileMode,
    DrawingCurrentSpace CurrentSpace,
    string CurrentLayoutName)
    : DrawingInspectionData(
        InstanceId,
        DispatchId,
        Operation,
        MainThreadVerified,
        ExecutionContext,
        ActiveDocumentId,
        QueueDelayMs,
        ExecutionMs,
        ReadMode,
        TransactionUsed);

/// <summary>Maps the request operation to its exact lowercase wire token.</summary>
public sealed class DrawingOperationJsonConverter : JsonConverter<DrawingOperation>
{
    public override DrawingOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "status" => DrawingOperation.Status,
            "list_documents" => DrawingOperation.ListDocuments,
            "active_document" => DrawingOperation.ActiveDocument,
            "units" => DrawingOperation.Units,
            "bounds" => DrawingOperation.Bounds,
            "layouts" => DrawingOperation.Layouts,
            "system_metadata" => DrawingOperation.SystemMetadata,
            _ => throw new JsonException("Unknown drawing operation."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        DrawingOperation value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            DrawingOperation.Status => "status",
            DrawingOperation.ListDocuments => "list_documents",
            DrawingOperation.ActiveDocument => "active_document",
            DrawingOperation.Units => "units",
            DrawingOperation.Bounds => "bounds",
            DrawingOperation.Layouts => "layouts",
            DrawingOperation.SystemMetadata => "system_metadata",
            _ => throw new JsonException("Unknown drawing operation."),
        });
}

public sealed class DrawingLinearFormatJsonConverter : JsonConverter<DrawingLinearFormat>
{
    public override DrawingLinearFormat Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "UNKNOWN" => DrawingLinearFormat.Unknown,
            "SCIENTIFIC" => DrawingLinearFormat.Scientific,
            "DECIMAL" => DrawingLinearFormat.DecimalFormat,
            "ENGINEERING" => DrawingLinearFormat.Engineering,
            "ARCHITECTURAL" => DrawingLinearFormat.Architectural,
            "FRACTIONAL" => DrawingLinearFormat.Fractional,
            _ => throw new JsonException("Unknown drawing linear format."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        DrawingLinearFormat value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            DrawingLinearFormat.Unknown => "UNKNOWN",
            DrawingLinearFormat.Scientific => "SCIENTIFIC",
            DrawingLinearFormat.DecimalFormat => "DECIMAL",
            DrawingLinearFormat.Engineering => "ENGINEERING",
            DrawingLinearFormat.Architectural => "ARCHITECTURAL",
            DrawingLinearFormat.Fractional => "FRACTIONAL",
            _ => throw new JsonException("Unknown drawing linear format."),
        });
}
