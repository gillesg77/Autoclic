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

namespace AutoClic.Core.Vision;

/// <summary>
/// Pixels prélevés dans une fenêtre, en attente d'être détourés puis transformés en
/// repère.
/// </summary>
public sealed class CapturedRegion : IDisposable
{
    public CapturedRegion(Bitmap patch, int clientX, int clientY, int clientWidth, int clientHeight)
    {
        Patch = patch ?? throw new ArgumentNullException(nameof(patch));
        ClientX = clientX;
        ClientY = clientY;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
    }

    public Bitmap Patch { get; }

    public int ClientX { get; }

    public int ClientY { get; }

    /// <summary>Largeur de la zone cliente au moment de la capture.</summary>
    public int ClientWidth { get; }

    public int ClientHeight { get; }

    public int Width => Patch.Width;

    public int Height => Patch.Height;

    /// <summary>PNG de la région, agrandi au plus proche voisin pour l'aperçu.</summary>
    /// <remarks>
    /// Interpolation au plus proche voisin délibérément : l'utilisateur doit voir les
    /// pixels tels qu'ils seront comparés, pas une version lissée qui masquerait les
    /// bords sur lesquels la comparaison peut échouer.
    /// </remarks>
    public byte[] ToPreviewPng(int zoom)
    {
        zoom = Math.Max(1, zoom);

        using Bitmap scaled = new(Width * zoom, Height * zoom, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.DrawImage(Patch, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }

        using MemoryStream stream = new();
        scaled.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose() => Patch.Dispose();
}
