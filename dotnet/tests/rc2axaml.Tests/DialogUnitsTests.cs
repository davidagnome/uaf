using Rc2Axaml;

namespace Rc2Axaml.Tests;

/// <summary>The DLU-to-pixel arithmetic, checked against what <c>MapDialogRect</c> would do.</summary>
public sealed class DialogUnitsTests
{
    [Fact]
    public void MsSansSerif8IsSixByThirteen()
    {
        Assert.Equal(new DialogUnits(6, 13), DialogUnits.ForFont(RcFont.MsSansSerif8));
        Assert.Equal(new DialogUnits(6, 13), DialogUnits.ForFont(new RcFont(8, "MS Shell Dlg")));
        Assert.Equal(new DialogUnits(8, 16), DialogUnits.ForFont(new RcFont(10, "MS Sans Serif")));
    }

    [Fact]
    public void UnknownFontFallsBackAndSaysSo()
    {
        var diagnostics = new List<string>();
        Assert.Equal(DialogUnits.MsSansSerif8, DialogUnits.ForFont(new RcFont(9, "Courier New"), diagnostics));
        Assert.Single(diagnostics);
    }

    [Theory]
    // x, y, cx, cy -> left, top, width, height, for base units 6 x 13.
    [InlineData(0, 0, 155, 109, 0, 0, 232, 177)]  // IDD_ABOUTBOX's own extent.
    [InlineData(52, 91, 50, 14, 78, 147, 75, 23)] // Its OK button: the standard 50 x 14 DLU.
    [InlineData(7, 3, 7, 3, 10, 4, 11, 5)]        // Truncation biting on both axes: 7x3 DLU is 11x5 px here.
    public void MapsRectanglesEdgeByEdge(
        int x, int y, int cx, int cy, int left, int top, int width, int height)
    {
        Assert.Equal((left, top, width, height), DialogUnits.MsSansSerif8.MapRect(x, y, cx, cy));
    }

    /// <summary>
    /// Sizes come from mapped edges, so they are not a function of <c>cx</c> alone.
    /// </summary>
    /// <remarks>
    /// Two controls of the same 7-DLU width come out 10 and 11 pixels wide depending on where they
    /// start, and that is correct: it is what Windows drew. A transpiler that mapped the width on
    /// its own would be off by a pixel on roughly half the controls in the file.
    /// </remarks>
    [Fact]
    public void SameWidthCanMapToDifferentPixelWidths()
    {
        Assert.Equal(10, DialogUnits.MsSansSerif8.MapRect(0, 0, 7, 0).Width);
        Assert.Equal(11, DialogUnits.MsSansSerif8.MapRect(7, 0, 7, 0).Width);
    }
}
