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

namespace AutoClic.Core.Vision;

/// <summary>Capture le contenu de la zone cliente d'une fenêtre.</summary>
public static class WindowCapture
{
    /// <summary>
    /// Image de la zone cliente, ou null si la fenêtre n'est pas capturable.
    /// </summary>
    /// <remarks>
    /// PrintWindow demande son rendu à la fenêtre elle-même, ce qui fonctionne même
    /// lorsqu'elle est partiellement masquée. Certaines applications l'ignorent et
    /// renvoient une image noire ; on retombe alors sur une copie de l'écran, qui
    /// exige en revanche que la fenêtre soit visible.
    /// </remarks>
    public static Bitmap? CaptureClientArea(nint handle)
    {
        if (handle == 0
            || !NativeMethods.IsWindow(handle)
            || !NativeMethods.GetWindowRect(handle, out RECT windowRect)
            || !NativeMethods.GetClientRect(handle, out RECT clientRect)
            || clientRect.Width <= 0
            || clientRect.Height <= 0)
        {
            return null;
        }

        var origin = new POINT { x = 0, y = 0 };
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            return null;
        }

        using Bitmap window = new(Math.Max(1, windowRect.Width), Math.Max(1, windowRect.Height), PixelFormat.Format32bppArgb);

        bool printed;
        using (Graphics graphics = Graphics.FromImage(window))
        {
            nint hdc = graphics.GetHdc();
            try
            {
                printed = NativeMethods.PrintWindow(handle, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        Bitmap client = new(clientRect.Width, clientRect.Height, PixelFormat.Format32bppArgb);

        try
        {
            using Graphics target = Graphics.FromImage(client);

            if (printed && !IsUniform(window))
            {
                target.DrawImage(
                    window,
                    new Rectangle(0, 0, clientRect.Width, clientRect.Height),
                    new Rectangle(origin.x - windowRect.left, origin.y - windowRect.top, clientRect.Width, clientRect.Height),
                    GraphicsUnit.Pixel);
            }
            else
            {
                target.CopyFromScreen(origin.x, origin.y, 0, 0, new Size(clientRect.Width, clientRect.Height));
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Découpe une région, bornée à l'image source.</summary>
    public static Bitmap? Crop(Bitmap source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        var region = Rectangle.Intersect(
            new Rectangle(0, 0, source.Width, source.Height),
            new Rectangle(x, y, width, height));

        if (region.Width <= 0 || region.Height <= 0)
        {
            return null;
        }

        return source.Clone(region, source.PixelFormat);
    }

    /// <summary>
    /// Rend transparent tout ce qui est hors du polygone, en coordonnées de l'image.
    /// </summary>
    /// <remarks>
    /// Le masque passe par le canal alpha plutôt que par une image séparée : le repère
    /// reste un seul PNG, que l'on peut ouvrir et vérifier à l'œil.
    /// </remarks>
    public static Bitmap ApplyOutline(Bitmap source, IReadOnlyList<Point> polygon)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(polygon);

        if (polygon.Count < 3)
        {
            throw new ArgumentException("An outline needs at least three points.", nameof(polygon));
        }

        Bitmap masked = new(source.Width, source.Height, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(masked))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.None; // un bord antialiasé produirait des pixels à demi retenus

            using GraphicsPath path = new();
            path.AddPolygon(polygon.ToArray());

            using TextureBrush brush = new(source);
            graphics.FillPath(brush, path);
        }

        return masked;
    }

    public static string ToBase64Png(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    public static Bitmap FromBase64Png(string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);

        using MemoryStream stream = new(Convert.FromBase64String(base64));

        // Bitmap garde une référence au flux : on recopie pour pouvoir le libérer.
        using Bitmap decoded = new(stream);
        return new Bitmap(decoded);
    }

    /// <summary>
    /// Une zone cliente d'une seule couleur trahit un PrintWindow que l'application
    /// n'a pas honoré.
    /// </summary>
    /// <remarks>
    /// Le test porte sur l'uniformité et non sur le noir : selon la technologie de
    /// rendu, un PrintWindow inopérant renvoie du noir, du blanc ou du transparent.
    /// Une fenêtre d'application réelle n'est jamais unie sur toute sa zone cliente,
    /// le faux positif est donc théorique. Échantillonnage suffisant : il s'agit de
    /// choisir une stratégie, pas de mesurer.
    /// </remarks>
    internal static bool IsUniform(Bitmap bitmap)
    {
        int stepX = Math.Max(1, bitmap.Width / 32);
        int stepY = Math.Max(1, bitmap.Height / 32);

        Color reference = bitmap.GetPixel(0, 0);

        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != reference.ToArgb())
                {
                    return false;
                }
            }
        }

        return true;
    }
}
