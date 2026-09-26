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

namespace AutoClic.Core.Vision;

/// <summary>
/// Motif recherché : les pixels du repère, et le masque disant lesquels comptent.
/// </summary>
/// <remarks>
/// Le masque vient du canal alpha de l'image. Un détourage met à zéro l'alpha autour
/// de l'élément, si bien que les pixels de fond — ceux qui changent d'un lancement à
/// l'autre — n'entrent pas dans la comparaison. Sans détourage, le masque couvre tout
/// le rectangle.
/// </remarks>
public sealed class ReferencePattern
{
    public const byte DefaultAlphaThreshold = 128;

    public ReferencePattern(PixelBuffer pixels, bool[] mask)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        if (mask.Length != pixels.Width * pixels.Height)
        {
            throw new ArgumentException(
                $"Masque de {mask.Length} entrées pour {pixels.Width}×{pixels.Height} pixels.", nameof(mask));
        }

        Pixels = pixels;
        Mask = mask;
        MaskedCount = mask.Count(inside => inside);
    }

    public PixelBuffer Pixels { get; }

    public bool[] Mask { get; }

    /// <summary>Nombre de pixels retenus par le masque.</summary>
    public int MaskedCount { get; }

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    public static ReferencePattern FromBitmap(Bitmap bitmap, byte alphaThreshold = DefaultAlphaThreshold)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        (byte[] rgb, byte[] gray, bool[]? mask) = PixelBuffer.Decode(bitmap, alphaThreshold);

        return new ReferencePattern(
            new PixelBuffer(bitmap.Width, bitmap.Height, rgb, gray),
            mask!);
    }
}
