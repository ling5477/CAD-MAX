using CadMax.Contracts;
using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class DrawingInspectionSafetyTests
{
    [Theory]
    [InlineData(@"C:\CADMAX_SECRET_DIRECTORY_SENTINEL\fixture.dwg", "fixture.dwg")]
    [InlineData(@"\\server\secret\fixture.dwg", "fixture.dwg")]
    [InlineData("file:///C:/secret/fixture.dwg", "fixture.dwg")]
    [InlineData("bad\u0001name.dwg", "bad_name.dwg")]
    public void DocumentNameIsBasenameOnly(string raw, string expected)
    {
        var result = DrawingInspectionSafety.SanitizeDocumentName(raw, isUntitled: false);

        Assert.Equal(expected, result.Value);
        Assert.DoesNotContain("secret", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\\', result.Value);
        Assert.DoesNotContain('/', result.Value);
    }

    [Fact]
    public void BoundsRejectInvalidNumbersAndRecognizeOnlyKnownEmptySentinel()
    {
        var empty = DrawingInspectionSafety.ProjectBounds(
            new DrawingPointData(1e20, 1e20, 1e20),
            new DrawingPointData(-1e20, -1e20, -1e20));
        Assert.Equal(DrawingBoundsState.Empty, empty.State);

        Assert.Throws<DrawingInspectionDataException>(() =>
            DrawingInspectionSafety.ProjectBounds(
                new DrawingPointData(double.NaN, 0, 0),
                new DrawingPointData(1, 1, 1)));
        Assert.Throws<DrawingInspectionDataException>(() =>
            DrawingInspectionSafety.ProjectBounds(
                new DrawingPointData(2, 0, 0),
                new DrawingPointData(1, 1, 1)));
        Assert.Throws<DrawingInspectionDataException>(() =>
            DrawingInspectionSafety.ProjectBounds(
                new DrawingPointData(1e16, 0, 0),
                new DrawingPointData(1e16, 1, 1)));
    }

    [Fact]
    public void UnitsAreStableAndNeverGuessed()
    {
        Assert.Equal(
            DrawingInsertionUnits.Millimeters,
            DrawingInspectionSafety.MapInsertionUnits("Millimeters"));
        Assert.Equal(
            DrawingInsertionUnits.Unitless,
            DrawingInspectionSafety.MapInsertionUnits("Undefined"));
        Assert.Equal(
            DrawingInsertionUnits.Unknown,
            DrawingInspectionSafety.MapInsertionUnits("FutureAutodeskUnit"));
        Assert.Null(DrawingInspectionSafety.MillimetersPerDrawingUnit(
            DrawingInsertionUnits.Unknown));
        Assert.Throws<DrawingInspectionDataException>(() =>
            DrawingInspectionSafety.ValidatePrecision(9));
    }

    [Theory]
    [InlineData("Sheet/1")]
    [InlineData(@"Sheet\\1")]
    [InlineData("C:Sheet")]
    [InlineData(".")]
    [InlineData("..")]
    public void LayoutNamesRejectPathLikeValues(string raw)
    {
        Assert.Throws<DrawingInspectionDataException>(() =>
            DrawingInspectionSafety.SanitizeLayoutName(raw));
    }

    [Fact]
    public void DocumentsAndLayoutsAreBoundedAndStablyOrdered()
    {
        var documents = DrawingInspectionSafety.OrderDocuments(
        [
            new("doc_BBBBBBBBBBBBBBBBBBBBBB", "b.dwg", false, false, false),
            new("doc_AAAAAAAAAAAAAAAAAAAAAA", "a.dwg", false, true, true),
        ]);
        Assert.True(documents[0].IsActive);

        var layouts = DrawingInspectionSafety.OrderLayouts(
        [
            new("Sheet 2", false, 2, false),
            new("Model", true, 0, true),
            new("Sheet 1", false, 1, false),
        ]);
        Assert.True(layouts[0].IsModel);
        Assert.Equal("Sheet 1", layouts[1].Name);

        Assert.Throws<DrawingInspectionLimitException>(() =>
            DrawingInspectionSafety.OrderDocuments(
                Enumerable.Range(0, DrawingInspectionSafety.MaximumDocuments + 1)
                    .Select(index => new DrawingDocumentData(
                        $"doc_{index:D22}",
                        "fixture.dwg",
                        false,
                        false,
                        false))));
    }
}
