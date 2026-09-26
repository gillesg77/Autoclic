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
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoClic.Core.Vision;

/// <summary>
/// Image décodée, prête pour la comparaison : trois octets RVB par pixel, plus la
/// luminance précalculée.
/// </summary>
/// <remarks>
/// Les deux représentations coexistent parce que les deux modes de comparaison n'ont
/// pas les mêmes besoins : la comparaison au pixel travaille en couleur, plus
/// discriminante, la corrélation sur la luminance seule. Les précalculer une fois
/// évite de reconvertir à chaque position testée.
/// Le type ne dépend d'aucune API Windows, ce qui rend la mise en correspondance
/// testable sur des images synthétiques.
/// </remarks>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height, byte[] rgb, byte[] gray)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions strictement positives attendues.");
        }

        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException(
                $"{rgb.Length} octets RVB fournis pour {width}×{height} pixels.", nameof(rgb));
        }

        if (gray.Length != width * height)
        {
            throw new ArgumentException(
                $"{gray.Length} octets de luminance fournis pour {width}×{height} pixels.", nameof(gray));
        }

        Width = width;
        Height = height;
        Rgb = rgb;
        Gray = gray;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Trois octets par pixel, dans l'ordre R, V, B.</summary>
    public byte[] Rgb { get; }

    public byte[] Gray { get; }

    public static PixelBuffer FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        (byte[] rgb, byte[] gray, _) = Decode(bitmap, alphaThreshold: null);
        return new PixelBuffer(bitmap.Width, bitmap.Height, rgb, gray);
    }

    /// <summary>
    /// Décode un bitmap en RVB, luminance et — si un seuil est fourni — masque
    /// d'appartenance déduit du canal alpha.
    /// </summary>
    internal static (byte[] Rgb, byte[] Gray, bool[]? Mask) Decode(Bitmap bitmap, byte? alphaThreshold)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int count = width * height;

        byte[] rgb = new byte[count * 3];
        byte[] gray = new byte[count];
        bool[]? mask = alphaThreshold is null ? null : new bool[count];

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte[] row = new byte[data.Stride];

            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, data.Stride);

                for (int x = 0; x < width; x++)
                {
                    int source = x * 4;

                    // Ordre mémoire d'un Format32bppArgb : B, V, R, A.
                    byte b = row[source];
                    byte g = row[source + 1];
                    byte r = row[source + 2];

                    int index = (y * width) + x;
                    int target = index * 3;

                    rgb[target] = r;
                    rgb[target + 1] = g;
                    rgb[target + 2] = b;

                    // Coefficients de luminance ITU-R BT.601.
                    gray[index] = (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000);

                    if (mask is not null)
                    {
                        mask[index] = row[source + 3] >= alphaThreshold!.Value;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return (rgb, gray, mask);
    }
}
