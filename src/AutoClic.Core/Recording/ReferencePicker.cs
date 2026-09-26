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
using AutoClic.Core.Diagnostics;
using AutoClic.Core.Native;

namespace AutoClic.Core.Recording;

/// <summary>Région tracée par l'utilisateur, en coordonnées clientes de la fenêtre cible.</summary>
public readonly record struct PickedRegion(int ClientX, int ClientY, int Width, int Height);

/// <summary>
/// Textes dessinés sur le calque de sélection.
/// </summary>
/// <remarks>
/// Fournis par l'appelant : le cœur dessine, il ne rédige pas. C'est ce qui rend le
/// calque traduisible sans le modifier.
/// </remarks>
public sealed record PickerLabels(string Instruction, string Size, string TooSmall)
{
    /// <summary>Culture servant à mettre en forme les nombres de ces libellés.</summary>
    public System.Globalization.CultureInfo Culture { get; init; }
        = System.Globalization.CultureInfo.CurrentCulture;
}

/// <summary>
/// Laisse l'utilisateur entourer un élément dans la fenêtre cible en traçant un
/// rectangle à la souris, matérialisé par un calque de sélection.
/// </summary>
/// <remarks>
/// Le glisser est intercepté et ne parvient jamais à l'application visée : entourer
/// un bouton ne doit pas revenir à l'actionner. Aucune capture n'est faite ici — le
/// rappel de hook doit rendre la main sans tarder, la capture appartient à l'appelant,
/// qui doit d'abord appeler <see cref="HideOverlay"/>.
/// </remarks>
public sealed class ReferencePicker : IDisposable
{
    private const ushort VkEscape = 0x1B;
    private const int MinimumSize = 6;

    private static readonly Color AccentColor = Color.FromArgb(255, 57, 255, 20);
    private static readonly Color RejectColor = Color.FromArgb(255, 255, 92, 92);

    private readonly LowLevelMouseHook _mouse = new();
    private readonly LowLevelKeyboardHook _keyboard = new();
    private readonly OverlayWindow _overlay = new();
    private readonly object _sync = new();

    private nint _target;
    private Rectangle _bounds;
    private PickerLabels _labels = new("", "{0} × {1}", "{0} × {1}");

    private Point _start;
    private Point _cursor;
    private bool _dragging;
    private bool _hasCursor;

    public bool IsPicking { get; private set; }

    /// <summary>Région retenue, ou null si l'utilisateur a annulé ou tracé trop petit.</summary>
    public event EventHandler<PickedRegion?>? Completed;

    public void Start(nint targetHandle, PickerLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        if (IsPicking)
        {
            return;
        }

        if (targetHandle == 0)
        {
            throw new AutoClicException(ErrorCode.NoTargetWindow, "No target window was given.");
        }

        if (ClientBounds(targetHandle) is not { } bounds)
        {
            throw new AutoClicException(
                ErrorCode.WindowCaptureFailed, "The target window has no usable client area.");
        }

        lock (_sync)
        {
            _target = targetHandle;
            _bounds = bounds;
            _labels = labels;
            _dragging = false;
            _hasCursor = false;
        }

        _mouse.MouseAction += OnMouse;
        _keyboard.KeyAction += OnKey;

        try
        {
            _mouse.Install();
            _keyboard.Install();
        }
        catch
        {
            Detach();
            throw;
        }

        _overlay.Show(bounds, Draw);
        IsPicking = true;
    }

    /// <summary>
    /// Referme le calque de sélection. À appeler avant toute capture de la fenêtre.
    /// </summary>
    public void HideOverlay() => _overlay.Hide();

    public void Cancel()
    {
        if (IsPicking)
        {
            Finish(null);
        }
    }

    private void OnKey(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsInjected || e.VirtualKey != VkEscape)
        {
            return;
        }

        e.Handled = true;

        if (e.IsKeyUp)
        {
            Finish(null);
        }
    }

    private void OnMouse(object? sender, MouseHookEventArgs e)
    {
        if (e.IsInjected)
        {
            return;
        }

        switch (e.Message)
        {
            case NativeMethods.WM_MOUSEMOVE:
                lock (_sync)
                {
                    _cursor = new Point(e.X, e.Y);
                    _hasCursor = true;
                }

                break;

            case NativeMethods.WM_LBUTTONDOWN:
                e.Handled = true; // le clic ne doit pas atteindre l'application
                lock (_sync)
                {
                    _start = new Point(e.X, e.Y);
                    _cursor = _start;
                    _hasCursor = true;
                    _dragging = true;
                }

                break;

            case NativeMethods.WM_LBUTTONUP when _dragging:
                e.Handled = true;

                Point origin;
                lock (_sync)
                {
                    origin = _start;
                    _dragging = false;
                }

                Finish(RegionBetween(origin.X, origin.Y, e.X, e.Y));
                break;

            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_RBUTTONDOWN:
            case NativeMethods.WM_RBUTTONUP:
                e.Handled = true;
                break;
        }
    }

    /// <summary>Convertit deux points écran en région cliente, ou null si trop petite.</summary>
    private PickedRegion? RegionBetween(int x1, int y1, int x2, int y2)
    {
        if (WindowLocator.ToClient(_target, Math.Min(x1, x2), Math.Min(y1, y2)) is not { } topLeft
            || WindowLocator.ClientSize(_target) is not { } size)
        {
            return null;
        }

        int width = Math.Abs(x2 - x1);
        int height = Math.Abs(y2 - y1);

        if (width < MinimumSize || height < MinimumSize)
        {
            return null;
        }

        int clientX = Math.Clamp(topLeft.X, 0, Math.Max(0, size.Width - 1));
        int clientY = Math.Clamp(topLeft.Y, 0, Math.Max(0, size.Height - 1));

        return new PickedRegion(
            clientX,
            clientY,
            Math.Min(width, size.Width - clientX),
            Math.Min(height, size.Height - clientY));
    }

    private static Rectangle? ClientBounds(nint handle)
    {
        if (WindowLocator.ClientSize(handle) is not { } size
            || WindowLocator.ToScreen(handle, 0, 0) is not { } origin)
        {
            return null;
        }

        return new Rectangle(origin.X, origin.Y, size.Width, size.Height);
    }

    // --- Calque de sélection ----------------------------------------------

    private void Draw(Graphics graphics, Size size)
    {
        Rectangle bounds;
        Point start;
        Point cursor;
        bool dragging;
        bool hasCursor;

        lock (_sync)
        {
            bounds = _bounds;
            start = _start;
            cursor = _cursor;
            dragging = _dragging;
            hasCursor = _hasCursor;
        }

        // Coordonnées locales au calque : son origine est celle de la zone cliente.
        Point localCursor = new(cursor.X - bounds.X, cursor.Y - bounds.Y);
        Point localStart = new(start.X - bounds.X, start.Y - bounds.Y);

        if (!dragging)
        {
            DrawIdle(graphics, size, localCursor, hasCursor);
            return;
        }

        Rectangle selection = Rectangle.FromLTRB(
            Math.Min(localStart.X, localCursor.X),
            Math.Min(localStart.Y, localCursor.Y),
            Math.Max(localStart.X, localCursor.X),
            Math.Max(localStart.Y, localCursor.Y));

        DrawSelection(graphics, size, selection);
    }

    private void DrawIdle(Graphics graphics, Size size, Point cursor, bool hasCursor)
    {
        using (SolidBrush veil = new(Color.FromArgb(60, 0, 0, 0)))
        {
            graphics.FillRectangle(veil, 0, 0, size.Width, size.Height);
        }

        if (hasCursor)
        {
            // Réticule pleine largeur : viser le bord d'un bouton devient précis.
            using Pen guide = new(Color.FromArgb(110, AccentColor)) { DashStyle = DashStyle.Dash };
            graphics.DrawLine(guide, 0, cursor.Y, size.Width, cursor.Y);
            graphics.DrawLine(guide, cursor.X, 0, cursor.X, size.Height);
        }

        DrawBanner(graphics, size, _labels.Instruction);
    }

    private void DrawSelection(Graphics graphics, Size size, Rectangle selection)
    {
        bool valid = selection.Width >= MinimumSize && selection.Height >= MinimumSize;
        Color accent = valid ? AccentColor : RejectColor;

        // Voile percé : la zone retenue reste à sa luminosité d'origine, tout le reste
        // s'assombrit. On juge ainsi le contenu réel de la sélection, pas une teinte.
        using (GraphicsPath veil = new() { FillMode = FillMode.Alternate })
        {
            veil.AddRectangle(new Rectangle(0, 0, size.Width, size.Height));

            if (valid)
            {
                veil.AddRectangle(selection);
            }

            using SolidBrush brush = new(Color.FromArgb(120, 0, 0, 0));
            graphics.FillPath(brush, veil);
        }

        using (Pen border = new(accent, 2))
        {
            graphics.DrawRectangle(border, selection);
        }

        // Poignées aux angles : matérialisent l'emprise exacte au pixel près.
        using (SolidBrush handle = new(accent))
        {
            foreach (Point corner in new[]
            {
                new Point(selection.Left, selection.Top),
                new Point(selection.Right, selection.Top),
                new Point(selection.Left, selection.Bottom),
                new Point(selection.Right, selection.Bottom),
            })
            {
                graphics.FillRectangle(handle, corner.X - 3, corner.Y - 3, 6, 6);
            }
        }

        string label = string.Format(
            _labels.Culture,
            valid ? _labels.Size : _labels.TooSmall,
            selection.Width,
            selection.Height);

        DrawLabel(graphics, size, selection, label, accent);
    }

    private static void DrawLabel(Graphics graphics, Size size, Rectangle selection, string text, Color accent)
    {
        using Font font = new("Segoe UI", 10, FontStyle.Bold);
        SizeF measured = graphics.MeasureString(text, font);

        float width = measured.Width + 16;
        float height = measured.Height + 8;

        // Sous la sélection par défaut, au-dessus si l'on déborderait du calque.
        float x = Math.Clamp(selection.Left, 0, Math.Max(0, size.Width - width));
        float y = selection.Bottom + 8 + height <= size.Height
            ? selection.Bottom + 8
            : Math.Max(0, selection.Top - height - 8);

        using (SolidBrush background = new(Color.FromArgb(225, 18, 18, 22)))
        {
            graphics.FillRectangle(background, x, y, width, height);
        }

        using (Pen edge = new(Color.FromArgb(180, accent)))
        {
            graphics.DrawRectangle(edge, x, y, width, height);
        }

        using SolidBrush foreground = new(Color.White);
        graphics.DrawString(text, font, foreground, x + 8, y + 4);
    }

    private static void DrawBanner(Graphics graphics, Size size, string text)
    {
        using Font font = new("Segoe UI", 11, FontStyle.Bold);
        SizeF measured = graphics.MeasureString(text, font);

        float width = measured.Width + 28;
        float height = measured.Height + 14;
        float x = (size.Width - width) / 2;

        using (SolidBrush background = new(Color.FromArgb(235, 18, 18, 22)))
        {
            graphics.FillRectangle(background, x, 24, width, height);
        }

        using (Pen edge = new(Color.FromArgb(170, AccentColor)))
        {
            graphics.DrawRectangle(edge, x, 24, width, height);
        }

        using SolidBrush foreground = new(Color.White);
        graphics.DrawString(text, font, foreground, x + 14, 31);
    }

    private void Finish(PickedRegion? region)
    {
        Detach();
        IsPicking = false;

        lock (_sync)
        {
            _dragging = false;
        }

        // Le calque n'est pas fermé ici : Hide() attend la destruction de la fenêtre,
        // ce qu'un rappel de hook ne peut pas se permettre. C'est à l'appelant de le
        // faire avant de capturer.
        Completed?.Invoke(this, region);
    }

    private void Detach()
    {
        _mouse.MouseAction -= OnMouse;
        _keyboard.KeyAction -= OnKey;
        _mouse.Uninstall();
        _keyboard.Uninstall();
    }

    public void Dispose()
    {
        Detach();
        _overlay.Dispose();
        _mouse.Dispose();
        _keyboard.Dispose();
    }
}
