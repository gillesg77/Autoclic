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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AutoClic.Core.Native;
using Xunit;

namespace AutoClic.Core.Tests;

public class LayeredSurfaceTests
{
    [Fact]
    public void Resize_AllocatesADrawableSurface()
    {
        using LayeredSurface surface = new();

        Assert.True(surface.Resize(new Size(64, 48)));
        Assert.NotNull(surface.Bitmap);
        Assert.Equal(new Size(64, 48), surface.Size);
        Assert.NotEqual(0, surface.DeviceContext);
        Assert.Equal(PixelFormat.Format32bppPArgb, surface.Bitmap!.PixelFormat);
    }

    [Fact]
    public void Resize_RejectsAnEmptySize()
    {
        using LayeredSurface surface = new();

        Assert.False(surface.Resize(new Size(0, 10)));
        Assert.False(surface.Resize(new Size(10, -5)));
    }

    [Fact]
    public void Resize_ReusesTheSurface_WhenTheSizeIsUnchanged()
    {
        using LayeredSurface surface = new();

        Assert.True(surface.Resize(new Size(32, 32)));
        Bitmap first = surface.Bitmap!;

        Assert.True(surface.Resize(new Size(32, 32)));

        // Réallouer à chaque image est précisément ce que cette classe évite.
        Assert.Same(first, surface.Bitmap);
    }

    [Fact]
    public void Resize_ReallocatesOnAChangeOfSize()
    {
        using LayeredSurface surface = new();

        Assert.True(surface.Resize(new Size(32, 32)));
        Assert.True(surface.Resize(new Size(48, 24)));

        Assert.Equal(new Size(48, 24), surface.Size);
        Assert.Equal(48, surface.Bitmap!.Width);
    }

    /// <summary>
    /// Vérifie l'hypothèse sur laquelle repose tout le calque : GDI+ écrit en alpha
    /// prémultiplié, seul format qu'accepte AC_SRC_ALPHA.
    /// </summary>
    /// <remarks>
    /// Sans cela les zones semi-transparentes ressortiraient blanchies à l'écran. Le
    /// contrôle porte sur les octets réellement stockés, relus en Format32bppArgb pour
    /// que GDI+ ne « démultiplie » pas silencieusement à la lecture.
    /// </remarks>
    [Fact]
    public void Drawing_StoresPremultipliedComponents()
    {
        using LayeredSurface surface = new();
        Assert.True(surface.Resize(new Size(16, 16)));

        using (Graphics graphics = Graphics.FromImage(surface.Bitmap!))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;

            using SolidBrush brush = new(Color.FromArgb(128, 255, 0, 0));
            graphics.FillRectangle(brush, 0, 0, 16, 16);
        }

        byte[] pixel = ReadRaw(surface.Bitmap!, 8, 8);

        // Ordre mémoire B, V, R, A. Rouge pur à 50 % : la composante rouge stockée doit
        // valoir environ l'alpha, pas 255.
        Assert.InRange(pixel[3], 126, 130);          // alpha conservé
        Assert.InRange(pixel[2], 120, 136);          // rouge prémultiplié ≈ alpha
        Assert.InRange(pixel[1], 0, 4);              // vert
        Assert.InRange(pixel[0], 0, 4);              // bleu
    }

    [Fact]
    public void Clearing_LeavesFullyTransparentPixels()
    {
        using LayeredSurface surface = new();
        Assert.True(surface.Resize(new Size(8, 8)));

        using (Graphics graphics = Graphics.FromImage(surface.Bitmap!))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
        }

        byte[] pixel = ReadRaw(surface.Bitmap!, 4, 4);

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, pixel);
    }

    /// <summary>Lit les quatre octets bruts d'un pixel, sans conversion de format.</summary>
    private static byte[] ReadRaw(Bitmap bitmap, int x, int y)
    {
        BitmapData data = bitmap.LockBits(
            new Rectangle(x, y, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

        try
        {
            byte[] pixel = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixel, 0, 4);
            return pixel;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
