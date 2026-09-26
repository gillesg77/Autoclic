// AutoClic — enregistreur de macros clavier et souris pour Windows.
// Copyright (C) 2026 gillesg77
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Drawing;
using AutoClic.Core.Vision;
using Xunit;

namespace AutoClic.Core.Tests;

public class WindowCaptureTests
{
    private static Bitmap Filled(Color color, int width = 64, int height = 48)
    {
        Bitmap bitmap = new(width, height);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);

        return bitmap;
    }

    [Theory]
    [InlineData(0, 0, 0)]         // noir : PrintWindow refusé par une application GDI
    [InlineData(255, 255, 255)]   // blanc : PrintWindow inopérant sur un rendu par compositeur
    [InlineData(30, 30, 46)]      // toute autre teinte unie
    public void IsUniform_DetectsAFailedCapture_WhateverItsColour(int r, int g, int b)
    {
        using Bitmap flat = Filled(Color.FromArgb(r, g, b));

        Assert.True(WindowCapture.IsUniform(flat));
    }

    [Fact]
    public void IsUniform_RejectsAnImageWithContent()
    {
        using Bitmap bitmap = Filled(Color.White);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.SteelBlue, 10, 10, 30, 20);
        }

        Assert.False(WindowCapture.IsUniform(bitmap));
    }

    [Fact]
    public void IsUniform_SeesADifferenceEvenAwayFromTheSamplingGrid()
    {
        // L'échantillonnage saute des pixels ; la grille doit rester assez fine pour
        // qu'un contenu réel ne passe pas pour une capture ratée.
        using Bitmap bitmap = Filled(Color.Black, width: 320, height: 240);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.White, 0, 0, 160, 120);
        }

        Assert.False(WindowCapture.IsUniform(bitmap));
    }

    [Fact]
    public void ApplyOutline_MakesEverythingOutsideThePolygonTransparent()
    {
        using Bitmap source = Filled(Color.FromArgb(255, 200, 100, 50), width: 40, height: 40);

        using Bitmap masked = WindowCapture.ApplyOutline(
            source,
            [new Point(5, 5), new Point(35, 5), new Point(35, 35), new Point(5, 35)]);

        Assert.Equal(0, masked.GetPixel(1, 1).A);            // dehors
        Assert.Equal(255, masked.GetPixel(20, 20).A);        // dedans
        Assert.Equal(source.GetPixel(20, 20).ToArgb(), masked.GetPixel(20, 20).ToArgb());
    }

    [Fact]
    public void ApplyOutline_RefusesADegeneratePolygon()
    {
        using Bitmap source = Filled(Color.Red);

        Assert.Throws<ArgumentException>(() =>
            WindowCapture.ApplyOutline(source, [new Point(0, 0), new Point(10, 10)]));
    }

    [Fact]
    public void Base64Png_RoundTripsThroughTheAlphaChannel()
    {
        using Bitmap source = Filled(Color.FromArgb(255, 12, 34, 56), width: 20, height: 16);

        using Bitmap masked = WindowCapture.ApplyOutline(
            source,
            [new Point(2, 2), new Point(18, 2), new Point(18, 14), new Point(2, 14)]);

        using Bitmap restored = WindowCapture.FromBase64Png(WindowCapture.ToBase64Png(masked));

        Assert.Equal(masked.Width, restored.Width);
        Assert.Equal(masked.Height, restored.Height);
        Assert.Equal(0, restored.GetPixel(0, 0).A);
        Assert.Equal(masked.GetPixel(10, 8).ToArgb(), restored.GetPixel(10, 8).ToArgb());
    }
}
