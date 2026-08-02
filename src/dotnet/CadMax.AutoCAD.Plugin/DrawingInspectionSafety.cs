using System.Text;
using CadMax.Contracts;

namespace CadMax.AutoCAD.Plugin;

/// <summary>
/// SDK-free validation and projection helpers for Phase 1.4. These helpers accept only
/// primitive snapshots and never receive or retain Autodesk objects.
/// </summary>
public static class DrawingInspectionSafety
{
    public const int MaximumDocuments = 64;
    public const int MaximumLayouts = 256;
    public const int MaximumNameScalars = 128;
    public const double MaximumCoordinateMagnitude = 1_000_000_000_000_000d;
    private const double EmptySentinelMagnitude = 10_000_000_000_000_000d;

    public static SafeDisplayName SanitizeDocumentName(string? rawName, bool isUntitled)
    {
        if (isUntitled || string.IsNullOrWhiteSpace(rawName))
        {
            return new SafeDisplayName("UNTITLED", IsUntitled: true);
        }

        var candidate = rawName;
        if (Uri.TryCreate(rawName, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            candidate = uri.LocalPath;
        }

        var separator = Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf('\\'));
        if (separator >= 0)
        {
            candidate = candidate[(separator + 1)..];
        }

        candidate = ReplaceControls(candidate).Trim();
        if (candidate.Length == 0
            || candidate.Contains(':', StringComparison.Ordinal)
            || candidate is "." or "..")
        {
            return new SafeDisplayName("UNTITLED", IsUntitled: true);
        }

        return new SafeDisplayName(TruncateScalars(candidate), IsUntitled: false);
    }

    public static string SanitizeLayoutName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            throw new DrawingInspectionDataException();
        }

        var candidate = ReplaceControls(rawName).Trim();
        if (candidate.Length == 0
            || candidate.Contains('/')
            || candidate.Contains('\\')
            || candidate.Contains(':')
            || candidate is "." or "..")
        {
            throw new DrawingInspectionDataException();
        }

        return TruncateScalars(candidate);
    }

    public static IReadOnlyList<DrawingDocumentData> OrderDocuments(
        IEnumerable<DrawingDocumentData> documents)
    {
        var bounded = documents.Take(MaximumDocuments + 1).ToArray();
        if (bounded.Length > MaximumDocuments)
        {
            throw new DrawingInspectionLimitException();
        }

        return bounded
            .OrderByDescending(document => document.IsActive)
            .ThenBy(document => document.DocumentId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<DrawingLayoutData> OrderLayouts(
        IEnumerable<DrawingLayoutData> layouts)
    {
        var bounded = layouts.Take(MaximumLayouts + 1).ToArray();
        if (bounded.Length > MaximumLayouts)
        {
            throw new DrawingInspectionLimitException();
        }

        return bounded
            .OrderByDescending(layout => layout.IsModel)
            .ThenBy(layout => layout.TabOrder)
            .ThenBy(layout => layout.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static DrawingInsertionUnits MapInsertionUnits(string? value) =>
        value switch
        {
            "Undefined" or "Unitless" => DrawingInsertionUnits.Unitless,
            "Inches" => DrawingInsertionUnits.Inches,
            "Feet" => DrawingInsertionUnits.Feet,
            "Miles" => DrawingInsertionUnits.Miles,
            "Millimeters" => DrawingInsertionUnits.Millimeters,
            "Centimeters" => DrawingInsertionUnits.Centimeters,
            "Meters" => DrawingInsertionUnits.Meters,
            "Kilometers" => DrawingInsertionUnits.Kilometers,
            "Microinches" => DrawingInsertionUnits.Microinches,
            "Mils" => DrawingInsertionUnits.Mils,
            "Yards" => DrawingInsertionUnits.Yards,
            "Angstroms" => DrawingInsertionUnits.Angstroms,
            "Nanometers" => DrawingInsertionUnits.Nanometers,
            "Microns" => DrawingInsertionUnits.Microns,
            "Decimeters" => DrawingInsertionUnits.Decimeters,
            "Dekameters" => DrawingInsertionUnits.Dekameters,
            "Hectometers" => DrawingInsertionUnits.Hectometers,
            "Gigameters" => DrawingInsertionUnits.Gigameters,
            "Astro" or "AstronomicalUnits" => DrawingInsertionUnits.AstronomicalUnits,
            "LightYears" => DrawingInsertionUnits.LightYears,
            "Parsecs" => DrawingInsertionUnits.Parsecs,
            "USSurveyFeet" or "UsSurveyFeet" => DrawingInsertionUnits.UsSurveyFeet,
            "USSurveyInch" or "UsSurveyInch" => DrawingInsertionUnits.UsSurveyInches,
            "USSurveyYard" or "UsSurveyYard" => DrawingInsertionUnits.UsSurveyYards,
            "USSurveyMile" or "UsSurveyMile" => DrawingInsertionUnits.UsSurveyMiles,
            _ => DrawingInsertionUnits.Unknown,
        };

    public static double? MillimetersPerDrawingUnit(DrawingInsertionUnits units) =>
        units switch
        {
            DrawingInsertionUnits.Inches => 25.4d,
            DrawingInsertionUnits.Feet => 304.8d,
            DrawingInsertionUnits.Miles => 1_609_344d,
            DrawingInsertionUnits.Millimeters => 1d,
            DrawingInsertionUnits.Centimeters => 10d,
            DrawingInsertionUnits.Meters => 1_000d,
            DrawingInsertionUnits.Kilometers => 1_000_000d,
            DrawingInsertionUnits.Microinches => 0.0000254d,
            DrawingInsertionUnits.Mils => 0.0254d,
            DrawingInsertionUnits.Yards => 914.4d,
            DrawingInsertionUnits.Angstroms => 0.0000001d,
            DrawingInsertionUnits.Nanometers => 0.000001d,
            DrawingInsertionUnits.Microns => 0.001d,
            DrawingInsertionUnits.Decimeters => 100d,
            DrawingInsertionUnits.Dekameters => 10_000d,
            DrawingInsertionUnits.Hectometers => 100_000d,
            DrawingInsertionUnits.Gigameters => 1_000_000_000_000d,
            DrawingInsertionUnits.AstronomicalUnits => 149_597_870_700_000d,
            DrawingInsertionUnits.LightYears => 9_460_730_472_580_800_000d,
            DrawingInsertionUnits.Parsecs => 30_856_775_814_913_672_789.1392d,
            DrawingInsertionUnits.UsSurveyFeet => 304.8006096012192d,
            DrawingInsertionUnits.UsSurveyInches => 25.4000508001016d,
            DrawingInsertionUnits.UsSurveyYards => 914.4018288036576d,
            DrawingInsertionUnits.UsSurveyMiles => 1_609_347.2186944373d,
            _ => null,
        };

    public static DrawingLinearFormat MapLinearFormat(int value) =>
        value switch
        {
            1 => DrawingLinearFormat.Scientific,
            2 => DrawingLinearFormat.DecimalFormat,
            3 => DrawingLinearFormat.Engineering,
            4 => DrawingLinearFormat.Architectural,
            5 => DrawingLinearFormat.Fractional,
            _ => DrawingLinearFormat.Unknown,
        };

    public static DrawingAngularFormat MapAngularFormat(int value) =>
        value switch
        {
            0 => DrawingAngularFormat.DecimalDegrees,
            1 => DrawingAngularFormat.DegreesMinutesSeconds,
            2 => DrawingAngularFormat.Gradians,
            3 => DrawingAngularFormat.Radians,
            4 => DrawingAngularFormat.SurveyorUnits,
            _ => DrawingAngularFormat.Unknown,
        };

    public static int ValidatePrecision(int value)
    {
        if (value is < 0 or > 8)
        {
            throw new DrawingInspectionDataException();
        }

        return value;
    }

    public static DrawingBoundsProjection ProjectBounds(
        DrawingPointData minimum,
        DrawingPointData maximum)
    {
        if (IsEmptySentinel(minimum, maximum))
        {
            return DrawingBoundsProjection.Empty;
        }

        if (!IsValidPoint(minimum)
            || !IsValidPoint(maximum)
            || minimum.X > maximum.X
            || minimum.Y > maximum.Y
            || minimum.Z > maximum.Z)
        {
            throw new DrawingInspectionDataException();
        }

        var size = new DrawingPointData(
            maximum.X - minimum.X,
            maximum.Y - minimum.Y,
            maximum.Z - minimum.Z);
        if (!IsValidPoint(size))
        {
            throw new DrawingInspectionDataException();
        }

        return new DrawingBoundsProjection(
            DrawingBoundsState.Available,
            minimum,
            maximum,
            size);
    }

    public static DrawingFileFormatVersion MapFileFormatVersion(string? value) =>
        value switch
        {
            "AC1009" => DrawingFileFormatVersion.AcadR12,
            "AC1012" => DrawingFileFormatVersion.AcadR13,
            "AC1014" => DrawingFileFormatVersion.AcadR14,
            "AC1015" => DrawingFileFormatVersion.Acad2000,
            "AC1018" => DrawingFileFormatVersion.Acad2004,
            "AC1021" => DrawingFileFormatVersion.Acad2007,
            "AC1024" => DrawingFileFormatVersion.Acad2010,
            "AC1027" => DrawingFileFormatVersion.Acad2013,
            "AC1032" or "Current" or "Newest" => DrawingFileFormatVersion.Acad2018,
            _ => DrawingFileFormatVersion.Unknown,
        };

    private static bool IsEmptySentinel(DrawingPointData minimum, DrawingPointData maximum) =>
        minimum.X >= EmptySentinelMagnitude
        && minimum.Y >= EmptySentinelMagnitude
        && minimum.Z >= EmptySentinelMagnitude
        && maximum.X <= -EmptySentinelMagnitude
        && maximum.Y <= -EmptySentinelMagnitude
        && maximum.Z <= -EmptySentinelMagnitude;

    private static bool IsValidPoint(DrawingPointData value) =>
        IsValidCoordinate(value.X)
        && IsValidCoordinate(value.Y)
        && IsValidCoordinate(value.Z);

    private static bool IsValidCoordinate(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaximumCoordinateMagnitude;

    private static string ReplaceControls(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            builder.Append(Rune.IsControl(rune) ? '_' : rune.ToString());
        }

        return builder.ToString();
    }

    private static string TruncateScalars(string value)
    {
        var builder = new StringBuilder(value.Length);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == MaximumNameScalars)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}

public sealed record SafeDisplayName(string Value, bool IsUntitled);

public sealed record DrawingBoundsProjection(
    DrawingBoundsState State,
    DrawingPointData? Minimum,
    DrawingPointData? Maximum,
    DrawingPointData? Size)
{
    public static DrawingBoundsProjection Empty { get; } =
        new(DrawingBoundsState.Empty, null, null, null);
}

public sealed class DrawingInspectionLimitException : Exception;

public sealed class DrawingInspectionDataException : Exception;
