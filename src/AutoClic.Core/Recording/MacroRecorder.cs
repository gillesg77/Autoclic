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
using AutoClic.Core.Models;
using AutoClic.Core.Native;

namespace AutoClic.Core.Recording;

/// <summary>
/// Capture les frappes et les mouvements souris du système entier et les convertit
/// en <see cref="MacroEvent"/> horodatés relativement.
/// </summary>
/// <remarks>
/// À construire et piloter depuis le thread UI : les hooks bas niveau sous-jacents
/// exigent une boucle de messages.
/// </remarks>
public sealed class MacroRecorder : IDisposable
{
    private readonly LowLevelKeyboardHook _keyboard = new();
    private readonly LowLevelMouseHook _mouse = new();
    private readonly List<MacroEvent> _events = [];
    private readonly Stopwatch _clock = new();
    private readonly object _sync = new();

    private long _lastEventMs;
    private long _lastMoveMs;
    private bool _hasMove;
    private nint _targetHandle;

    /// <summary>
    /// Fenêtre à laquelle rapporter les positions. Nulle = coordonnées écran, donc
    /// macro liée à la disposition du bureau au moment de l'enregistrement.
    /// </summary>
    public WindowDescriptor? Target { get; set; }

    /// <summary>Vrai si la fenêtre cible a bien été trouvée au démarrage de l'enregistrement.</summary>
    public bool TargetResolved => _targetHandle != 0;

    /// <summary>Touche qui interrompt l'enregistrement. F9 par défaut ; elle n'est pas enregistrée.</summary>
    public ushort StopKey { get; set; } = 0x78; // VK_F9

    public bool RecordMouseMoves { get; set; } = true;

    /// <summary>
    /// Intervalle minimal entre deux déplacements retenus. Sans ce filtre, un simple
    /// geste produit des centaines d'événements pour un résultat identique au rejeu.
    /// </summary>
    public int MouseMoveIntervalMs { get; set; } = 15;

    public bool IsRecording { get; private set; }

    public int EventCount
    {
        get
        {
            lock (_sync)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>Déclenché quand l'utilisateur appuie sur <see cref="StopKey"/>.</summary>
    public event EventHandler? StopRequested;

    public event EventHandler<MacroEvent>? EventRecorded;

    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        lock (_sync)
        {
            _events.Clear();
        }

        _lastEventMs = 0;
        _lastMoveMs = 0;
        _hasMove = false;
        _targetHandle = Target is null ? 0 : WindowLocator.Find(Target);
        _clock.Restart();

        _keyboard.KeyAction += OnKey;
        _mouse.MouseAction += OnMouse;

        try
        {
            _keyboard.Install();
            _mouse.Install();
        }
        catch
        {
            // Un hook a pu s'installer avant l'échec de l'autre : on ne laisse rien derrière.
            _keyboard.KeyAction -= OnKey;
            _mouse.MouseAction -= OnMouse;
            _keyboard.Uninstall();
            _mouse.Uninstall();
            throw;
        }

        IsRecording = true;
    }

    public Macro Stop(string name = "Nouvelle macro")
    {
        if (!IsRecording)
        {
            return new Macro { Name = name };
        }

        _keyboard.KeyAction -= OnKey;
        _mouse.MouseAction -= OnMouse;
        _keyboard.Uninstall();
        _mouse.Uninstall();
        _clock.Stop();
        IsRecording = false;

        lock (_sync)
        {
            return new Macro { Name = name, Target = Target, Events = [.. _events] };
        }
    }

    private void OnKey(object? sender, KeyboardHookEventArgs e)
    {
        // Ce que SendInput produit nous revient par le hook : sans ce filtre, rejouer
        // pendant un enregistrement bouclerait sur lui-même.
        if (e.IsInjected)
        {
            return;
        }

        if (e.VirtualKey == StopKey)
        {
            e.Handled = true; // la touche d'arrêt ne doit pas fuir vers l'application active
            if (e.IsKeyUp)
            {
                StopRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        Append(new KeyboardEvent
        {
            DelayMs = NextDelay(),
            VirtualKey = e.VirtualKey,
            ScanCode = e.ScanCode,
            IsKeyUp = e.IsKeyUp,
            IsExtended = e.IsExtended,
        });
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
                RecordMove(e);
                break;

            case NativeMethods.WM_LBUTTONDOWN:
                RecordButton(e, MouseButton.Left, isDown: true);
                break;
            case NativeMethods.WM_LBUTTONUP:
                RecordButton(e, MouseButton.Left, isDown: false);
                break;
            case NativeMethods.WM_RBUTTONDOWN:
                RecordButton(e, MouseButton.Right, isDown: true);
                break;
            case NativeMethods.WM_RBUTTONUP:
                RecordButton(e, MouseButton.Right, isDown: false);
                break;
            case NativeMethods.WM_MBUTTONDOWN:
                RecordButton(e, MouseButton.Middle, isDown: true);
                break;
            case NativeMethods.WM_MBUTTONUP:
                RecordButton(e, MouseButton.Middle, isDown: false);
                break;

            case NativeMethods.WM_XBUTTONDOWN:
                RecordButton(e, XButtonOf(e), isDown: true);
                break;
            case NativeMethods.WM_XBUTTONUP:
                RecordButton(e, XButtonOf(e), isDown: false);
                break;

            case NativeMethods.WM_MOUSEWHEEL:
                RecordWheel(e, horizontal: false);
                break;
            case NativeMethods.WM_MOUSEHWHEEL:
                RecordWheel(e, horizontal: true);
                break;
        }
    }

    private static MouseButton XButtonOf(MouseHookEventArgs e) =>
        e.XButton == NativeMethods.XBUTTON2 ? MouseButton.X2 : MouseButton.X1;

    private void RecordMove(MouseHookEventArgs e)
    {
        if (!RecordMouseMoves)
        {
            return;
        }

        long now = _clock.ElapsedMilliseconds;
        if (_hasMove && now - _lastMoveMs < MouseMoveIntervalMs)
        {
            return;
        }

        _lastMoveMs = now;
        _hasMove = true;
        Append(new MouseMoveEvent { DelayMs = NextDelay(), X = e.X, Y = e.Y, Anchor = AnchorFor(e.X, e.Y) });
    }

    private void RecordButton(MouseHookEventArgs e, MouseButton button, bool isDown) =>
        Append(new MouseButtonEvent
        {
            DelayMs = NextDelay(),
            Button = button,
            IsDown = isDown,
            X = e.X,
            Y = e.Y,
            Anchor = AnchorFor(e.X, e.Y),
        });

    private void RecordWheel(MouseHookEventArgs e, bool horizontal) =>
        Append(new MouseWheelEvent
        {
            DelayMs = NextDelay(),
            Delta = e.WheelDelta,
            IsHorizontal = horizontal,
            X = e.X,
            Y = e.Y,
            Anchor = AnchorFor(e.X, e.Y),
        });

    /// <summary>
    /// Position rapportée à la fenêtre cible, ou null si le point tombe en dehors —
    /// auquel cas seules les coordonnées écran ont un sens.
    /// </summary>
    private WindowAnchor? AnchorFor(int screenX, int screenY)
    {
        if (_targetHandle == 0
            || WindowLocator.ClientSize(_targetHandle) is not { } size
            || WindowLocator.ToClient(_targetHandle, screenX, screenY) is not { } client)
        {
            return null;
        }

        if (client.X < 0 || client.Y < 0 || client.X >= size.Width || client.Y >= size.Height)
        {
            return null;
        }

        return new WindowAnchor
        {
            ClientX = client.X,
            ClientY = client.Y,
            ClientWidth = size.Width,
            ClientHeight = size.Height,
        };
    }

    private int NextDelay()
    {
        long now = _clock.ElapsedMilliseconds;

        bool first;
        lock (_sync)
        {
            first = _events.Count == 0;
        }

        int delay = first ? 0 : (int)(now - _lastEventMs);
        _lastEventMs = now;
        return delay;
    }

    private void Append(MacroEvent ev)
    {
        lock (_sync)
        {
            _events.Add(ev);
        }

        EventRecorded?.Invoke(this, ev);
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
    }
}
