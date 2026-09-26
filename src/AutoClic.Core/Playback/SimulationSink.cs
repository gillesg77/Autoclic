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

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using AutoClic.Core.Models;
using AutoClic.Core.Native;

namespace AutoClic.Core.Playback;

/// <summary>
/// Rejeu à blanc : rien n'est envoyé au système, tout est dessiné sur un calque
/// posé au-dessus de la fenêtre cible.
/// </summary>
/// <remarks>
/// Comme il s'agit d'un <see cref="IInputSink"/>, les positions affichées sont celles
/// que <see cref="MacroPlayer"/> aurait réellement visées — ancrage, correction du
/// repère et délais compris. Une simulation qui referait ces calculs de son côté
/// finirait par diverger et ne prouverait plus rien.
/// </remarks>
public sealed class SimulationSink : IInputSink, IDisposable
{
    private static readonly Color CursorColor = Color.FromArgb(255, 57, 255, 20);      // vert fluo
    private static readonly Color GlowColor = Color.FromArgb(110, 57, 255, 20);
    private static readonly Color TrailColor = Color.FromArgb(120, 0, 229, 255);

    private const int MarkerLifetimeMs = 900;
    private const int TrailLength = 48;

    private readonly OverlayWindow _overlay = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _sync = new();

    private readonly Queue<PointF> _trail = new();
    private readonly List<Marker> _markers = [];

    private nint _target;
    private Point _cursor;
    private bool _hasCursor;

    private readonly record struct Marker(PointF Position, Color Color, string Label, long BornMs);

    public void Begin(nint targetWindow)
    {
        _target = targetWindow;

        if (Bounds() is not { } bounds)
        {
            return;
        }

        _overlay.Show(bounds, Draw);
    }

    public void MoveTo(int screenX, int screenY)
    {
        lock (_sync)
        {
            _cursor = new Point(screenX, screenY);
            _hasCursor = true;

            _trail.Enqueue(ToOverlay(_cursor));

            while (_trail.Count > TrailLength)
            {
                _trail.Dequeue();
            }
        }

        Reposition();
    }

    public void Button(MouseButtonEvent button, int screenX, int screenY)
    {
        MoveTo(screenX, screenY);

        // Seul l'enfoncement est marqué : marquer aussi le relâchement doublerait
        // chaque clic à l'écran sans rien apprendre.
        if (!button.IsDown)
        {
            return;
        }

        Color color = button.Button switch
        {
            MouseButton.Left => Color.FromArgb(255, 57, 255, 20),
            MouseButton.Right => Color.FromArgb(255, 255, 0, 229),
            MouseButton.Middle => Color.FromArgb(255, 0, 229, 255),
            _ => Color.FromArgb(255, 255, 196, 0),
        };

        Add(new Point(screenX, screenY), color, button.Button.ToString());
    }

    public void Wheel(MouseWheelEvent wheel, int screenX, int screenY)
    {
        MoveTo(screenX, screenY);
        Add(new Point(screenX, screenY), Color.FromArgb(255, 255, 196, 0), wheel.Delta > 0 ? "▲" : "▼");
    }

    public void Key(KeyboardEvent key)
    {
        if (key.IsKeyUp || !_hasCursor)
        {
            return;
        }

        lock (_sync)
        {
            _markers.Add(new Marker(
                ToOverlay(_cursor), Color.FromArgb(255, 255, 255, 255), $"VK {key.VirtualKey:X2}", _clock.ElapsedMilliseconds));
        }
    }

    public void End()
    {
        // Le calque reste visible un instant : disparaître à la milliseconde où le
        // dernier clic est dessiné empêcherait de le voir.
        Thread.Sleep(700);
        _overlay.Dispose();
    }

    private void Add(Point screen, Color color, string label)
    {
        lock (_sync)
        {
            _markers.Add(new Marker(ToOverlay(screen), color, label, _clock.ElapsedMilliseconds));
        }
    }

    private void Reposition()
    {
        if (Bounds() is { } bounds)
        {
            _overlay.MoveTo(bounds);
        }
    }

    private Rectangle? Bounds()
    {
        if (_target == 0 || WindowLocator.ClientSize(_target) is not { } size)
        {
            return null;
        }

        return WindowLocator.ToScreen(_target, 0, 0) is not { } origin
            ? null
            : new Rectangle(origin.X, origin.Y, size.Width, size.Height);
    }

    /// <summary>Convertit une position écran en coordonnées du calque.</summary>
    private PointF ToOverlay(Point screen)
    {
        if (Bounds() is not { } bounds)
        {
            return new PointF(screen.X, screen.Y);
        }

        return new PointF(screen.X - bounds.X, screen.Y - bounds.Y);
    }

    private void Draw(Graphics graphics, Size size)
    {
        Marker[] markers;
        PointF[] trail;
        PointF cursor;
        bool hasCursor;
        long now;

        lock (_sync)
        {
            now = _clock.ElapsedMilliseconds;
            _markers.RemoveAll(m => now - m.BornMs > MarkerLifetimeMs);
            markers = [.. _markers];
            trail = [.. _trail];
            cursor = _trail.Count > 0 ? _trail.Last() : default;
            hasCursor = _hasCursor;
        }

        // Cadre : rappelle sur quelle zone cliente la macro raisonne.
        using (Pen frame = new(Color.FromArgb(90, 0, 229, 255), 2))
        {
            graphics.DrawRectangle(frame, 1, 1, size.Width - 3, size.Height - 3);
        }

        DrawTrail(graphics, trail);

        foreach (Marker marker in markers)
        {
            DrawMarker(graphics, marker, now);
        }

        if (hasCursor)
        {
            DrawCursor(graphics, cursor);
        }
    }

    private static void DrawTrail(Graphics graphics, PointF[] trail)
    {
        if (trail.Length < 2)
        {
            return;
        }

        // Opacité croissante vers la position courante : le sens du déplacement se lit
        // d'un coup d'œil.
        for (int i = 1; i < trail.Length; i++)
        {
            int alpha = (int)(TrailColor.A * (i / (double)trail.Length));
            using Pen pen = new(Color.FromArgb(alpha, TrailColor), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(pen, trail[i - 1], trail[i]);
        }
    }

    private static void DrawMarker(Graphics graphics, Marker marker, long now)
    {
        double age = (now - marker.BornMs) / (double)MarkerLifetimeMs;
        float radius = (float)(10 + (34 * age));
        int alpha = (int)(230 * (1 - age));

        if (alpha <= 0)
        {
            return;
        }

        using Pen ring = new(Color.FromArgb(alpha, marker.Color), 3);
        graphics.DrawEllipse(ring, marker.Position.X - radius, marker.Position.Y - radius, radius * 2, radius * 2);

        using SolidBrush core = new(Color.FromArgb(Math.Min(255, alpha + 25), marker.Color));
        graphics.FillEllipse(core, marker.Position.X - 5, marker.Position.Y - 5, 10, 10);

        using SolidBrush text = new(Color.FromArgb(alpha, Color.White));
        using Font font = new("Segoe UI", 9, FontStyle.Bold);
        graphics.DrawString(marker.Label, font, text, marker.Position.X + radius + 4, marker.Position.Y - 8);
    }

    private static void DrawCursor(Graphics graphics, PointF position)
    {
        // Halo : sur un fond clair, un tracé fin se perdrait.
        using (SolidBrush glow = new(GlowColor))
        {
            graphics.FillEllipse(glow, position.X - 16, position.Y - 16, 32, 32);
        }

        PointF[] arrow =
        [
            new(position.X, position.Y),
            new(position.X, position.Y + 20),
            new(position.X + 5, position.Y + 15),
            new(position.X + 9, position.Y + 24),
            new(position.X + 13, position.Y + 22),
            new(position.X + 9, position.Y + 14),
            new(position.X + 16, position.Y + 13),
        ];

        using (SolidBrush shadow = new(Color.FromArgb(120, 0, 0, 0)))
        {
            graphics.TranslateTransform(1.5f, 2f);
            graphics.FillPolygon(shadow, arrow);
            graphics.ResetTransform();
        }

        using SolidBrush body = new(CursorColor);
        using Pen outline = new(Color.FromArgb(220, 12, 40, 12), 1.2f);
        graphics.FillPolygon(body, arrow);
        graphics.DrawPolygon(outline, arrow);
    }

    public void Dispose() => _overlay.Dispose();
}
