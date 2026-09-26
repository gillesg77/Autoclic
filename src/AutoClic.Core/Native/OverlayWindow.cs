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
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoClic.Core.Native;

/// <summary>
/// Calque transparent posé au-dessus d'une fenêtre, pour y dessiner sans jamais la
/// perturber.
/// </summary>
/// <remarks>
/// <para>
/// Fenêtre superposée en alpha par pixel (<c>UpdateLayeredWindow</c>) plutôt qu'en
/// couleur clé : une couleur clé laisse un halo sur tout bord antialiasé, ce qui
/// ruinerait un curseur ou un rectangle au tracé lisse.
/// </para>
/// <para>
/// <c>WS_EX_TRANSPARENT</c> et <c>WS_EX_NOACTIVATE</c> la rendent inerte : clics et
/// survol la traversent, et elle ne prend jamais le focus — indispensable, puisqu'elle
/// recouvre l'application que la macro pilote.
/// </para>
/// <para>
/// Le dessin va directement dans une section DIB en alpha prémultiplié, allouée une
/// fois pour toutes. C'est ce qui rend l'animation fluide : la voie naïve — allouer un
/// bitmap par image, le prémultiplier pixel par pixel en code managé, puis le recopier
/// via <c>GetHbitmap</c> — impose trois passes sur toute la surface à chaque image.
/// </para>
/// <para>
/// Elle vit sur son propre thread avec sa pompe de messages : créer et détruire une
/// fenêtre depuis le thread qui la possède est la seule façon sûre de procéder, et le
/// thread UI reste libre.
/// </para>
/// </remarks>
public sealed class OverlayWindow : IDisposable
{
    private const int TargetFrameMs = 16; // ~60 images par seconde

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _sync = new();

    private Thread? _thread;
    private nint _handle;
    private volatile bool _stopping;

    private Rectangle _bounds;
    private Action<Graphics, Size>? _draw;

    // Recréée seulement quand la taille change.
    private readonly LayeredSurface _surface = new();

    /// <summary>Affiche le calque sur cette zone écran et lance le rendu continu.</summary>
    public void Show(Rectangle screenBounds, Action<Graphics, Size> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        lock (_sync)
        {
            _bounds = screenBounds;
            _draw = draw;
        }

        if (_thread is not null)
        {
            return;
        }

        _stopping = false;
        _ready.Reset();

        _thread = new Thread(Run) { IsBackground = true, Name = "AutoClic.Overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Déplace le calque, par exemple quand la fenêtre cible bouge.</summary>
    public void MoveTo(Rectangle screenBounds)
    {
        lock (_sync)
        {
            _bounds = screenBounds;
        }
    }

    private void Run()
    {
        Rectangle bounds;
        lock (_sync)
        {
            bounds = _bounds;
        }

        // Classe « STATIC » prédéfinie : aucune procédure de fenêtre n'est nécessaire,
        // le contenu étant poussé directement par UpdateLayeredWindow.
        _handle = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
            "STATIC",
            null,
            NativeMethods.WS_POPUP,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            0,
            0,
            NativeMethods.GetModuleHandleW(0),
            0);

        _ready.Set();

        if (_handle == 0)
        {
            return;
        }

        NativeMethods.ShowWindow(_handle, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.timeBeginPeriod(1);

        Rectangle current = bounds;
        var clock = new Stopwatch();

        try
        {
            while (!_stopping)
            {
                clock.Restart();

                // Pompe de messages : une fenêtre qui n'en traite aucun finit par être
                // considérée comme bloquée par le gestionnaire de fenêtres.
                while (NativeMethods.PeekMessage(out MSG message, 0, 0, 0, NativeMethods.PM_REMOVE))
                {
                    NativeMethods.TranslateMessage(message);
                    NativeMethods.DispatchMessage(message);
                }

                Rectangle target;
                Action<Graphics, Size>? draw;

                lock (_sync)
                {
                    target = _bounds;
                    draw = _draw;
                }

                if (target != current)
                {
                    NativeMethods.SetWindowPos(
                        _handle, NativeMethods.HWND_TOPMOST,
                        target.X, target.Y, target.Width, target.Height,
                        NativeMethods.SWP_NOACTIVATE);
                    current = target;
                }

                if (draw is not null && target.Width > 0 && target.Height > 0)
                {
                    Render(target, draw);
                }

                // Cadence régulée sur le temps réellement passé : dormir une durée fixe
                // ferait dériver l'animation dès qu'une image coûte un peu plus cher.
                int remaining = TargetFrameMs - (int)clock.ElapsedMilliseconds;

                if (remaining > 0)
                {
                    Thread.Sleep(remaining);
                }
            }
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
            _surface.Dispose();
            NativeMethods.DestroyWindow(_handle);
            _handle = 0;
        }
    }

    private void Render(Rectangle bounds, Action<Graphics, Size> draw)
    {
        var size = new Size(bounds.Width, bounds.Height);

        if (!_surface.Resize(size) || _surface.Bitmap is null)
        {
            return;
        }

        using (Graphics graphics = Graphics.FromImage(_surface.Bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // AntiAlias et non ClearType : le sous-pixel suppose un fond opaque et
            // laisse des franges colorées sur une surface transparente.
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            draw(graphics, size);
        }

        var position = new POINT { x = bounds.X, y = bounds.Y };
        var extent = new SIZE { cx = bounds.Width, cy = bounds.Height };
        var source = new POINT { x = 0, y = 0 };

        var blend = new BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };

        nint screenDc = NativeMethods.GetDC(0);

        try
        {
            NativeMethods.UpdateLayeredWindow(
                _handle, screenDc, position, extent, _surface.DeviceContext, source, 0, blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// Ferme le calque et attend sa disparition effective.
    /// </summary>
    /// <remarks>
    /// L'attente n'est pas une précaution de confort : tant que la fenêtre existe, une
    /// capture qui retomberait sur la copie d'écran photographierait le calque
    /// lui-même. Appeler depuis un thread qui peut patienter quelques millisecondes,
    /// jamais depuis un rappel de hook.
    /// </remarks>
    public void Hide()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    public void Dispose()
    {
        Hide();
        _ready.Dispose();
    }
}
