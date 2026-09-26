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

namespace AutoClic.Core.Native;

/// <summary>
/// Surface de dessin partagée entre GDI+ et <c>UpdateLayeredWindow</c>.
/// </summary>
/// <remarks>
/// <para>
/// Une section DIB enveloppée dans un <see cref="System.Drawing.Bitmap"/> au format
/// <c>Format32bppPArgb</c> : GDI+ y dessine directement en alpha prémultiplié, le seul
/// format qu'accepte <c>AC_SRC_ALPHA</c>. Il n'y a donc ni copie ni passe de
/// conversion entre le dessin et l'affichage.
/// </para>
/// <para>
/// La voie naïve — un bitmap neuf par image, prémultiplié pixel par pixel en code
/// managé, puis recopié via <c>GetHbitmap</c> — impose trois passes sur toute la
/// surface à chaque image, ce qui suffit à faire saccader une animation plein écran.
/// </para>
/// </remarks>
internal sealed class LayeredSurface : IDisposable
{
    private nint _memoryDc;
    private nint _dib;
    private nint _previousBitmap;

    /// <summary>Bitmap à peindre. Null tant qu'aucune taille n'a été allouée.</summary>
    public Bitmap? Bitmap { get; private set; }

    /// <summary>Contexte de périphérique à passer à UpdateLayeredWindow.</summary>
    public nint DeviceContext => _memoryDc;

    public Size Size { get; private set; }

    /// <summary>
    /// Alloue la surface, ou la réalloue si la taille demandée diffère.
    /// </summary>
    /// <returns>Faux si le système a refusé l'allocation.</returns>
    public bool Resize(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        if (Bitmap is not null && Size == size)
        {
            return true;
        }

        Release();

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size.Width,

            // Négatif : image de haut en bas, l'ordre qu'attend GDI+.
            biHeight = -size.Height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = NativeMethods.BI_RGB,
        };

        _dib = NativeMethods.CreateDIBSection(
            0, header, NativeMethods.DIB_RGB_COLORS, out nint bits, 0, 0);

        if (_dib == 0 || bits == 0)
        {
            return false;
        }

        _memoryDc = NativeMethods.CreateCompatibleDC(0);
        _previousBitmap = NativeMethods.SelectObject(_memoryDc, _dib);

        Bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        Size = size;

        return true;
    }

    private void Release()
    {
        Bitmap?.Dispose();
        Bitmap = null;

        if (_memoryDc != 0)
        {
            NativeMethods.SelectObject(_memoryDc, _previousBitmap);
            NativeMethods.DeleteDC(_memoryDc);
            _memoryDc = 0;
        }

        if (_dib != 0)
        {
            NativeMethods.DeleteObject(_dib);
            _dib = 0;
        }

        Size = Size.Empty;
    }

    public void Dispose() => Release();
}
