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

using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Native;
using AutoClic.Core.Vision;

namespace AutoClic.Core.Playback;

public readonly record struct MacroProgress(int Repeat, int TotalRepeats, int EventIndex, int TotalEvents);

/// <summary>
/// Déroule une macro : retrouve la fenêtre, vérifie le repère, respecte les délais et
/// recalcule chaque position.
/// </summary>
/// <remarks>
/// Ce qui est fait des entrées dépend du <see cref="IInputSink"/> fourni : les
/// réinjecter dans le système, ou seulement les dessiner. Tout le reste — délais,
/// ancrage, correction du repère — est commun, ce qui garantit qu'une simulation
/// montre exactement ce que ferait un rejeu.
/// </remarks>
public sealed class MacroPlayer
{
    private readonly IInputSink _sink;

    public MacroPlayer()
        : this(new SendInputSink())
    {
    }

    public MacroPlayer(IInputSink sink) => _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>1.0 = vitesse d'origine, 2.0 = deux fois plus rapide.</summary>
    public double SpeedFactor { get; set; } = 1.0;

    public int RepeatCount { get; set; } = 1;

    /// <summary>Remet le curseur là où il était avant le rejeu.</summary>
    public bool RestoreCursorPosition { get; set; } = true;

    /// <summary>Restaure et met au premier plan la fenêtre cible avant de commencer.</summary>
    public bool ActivateTarget { get; set; } = true;

    /// <summary>Temps laissé à la fenêtre pour se redessiner après activation.</summary>
    public int ActivationDelayMs { get; set; } = 250;

    /// <summary>
    /// Rejoue les déplacements de souris. À faux, le curseur se téléporte d'un clic au
    /// suivant.
    /// </summary>
    /// <remarks>
    /// Le temps porté par les déplacements ignorés l'est aussi : c'était le temps du
    /// déplacement. Les déplacements effectués bouton enfoncé sont toujours rejoués —
    /// les supprimer casserait le geste de glisser lui-même.
    /// </remarks>
    public bool ReplayMouseMoves { get; set; } = true;

    /// <summary>Score obtenu au dernier rejeu, si la macro portait un repère.</summary>
    public double? LastReferenceScore { get; private set; }

    /// <summary>Levé depuis le thread de rejeu : marshaler avant de toucher à l'UI.</summary>
    public event EventHandler<MacroProgress>? Progress;

    public async Task PlayAsync(Macro macro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(macro);

        if (SpeedFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpeedFactor), "Speed must be strictly positive.");
        }

        PlaybackContext context = await PrepareTargetAsync(macro, ct).ConfigureAwait(false);

        NativeMethods.GetCursorPos(out POINT origin);
        int repeats = Math.Max(1, RepeatCount);

        _sink.Begin(context.Target);

        // Relève la résolution de l'horloge : sans cela une attente de 10 ms en dure
        // couramment 15, et une macro riche en petits pas dérive en s'allongeant.
        NativeMethods.timeBeginPeriod(1);

        // Suivis sur tout le rejeu, répétitions comprises : ce qui a été enfoncé doit
        // être relâché, quelle que soit la façon dont le rejeu se termine.
        HashSet<MouseButton> heldButtons = [];
        Dictionary<ushort, KeyboardEvent> heldKeys = [];
        (int X, int Y) lastPosition = (origin.x, origin.y);

        try
        {
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                for (int i = 0; i < macro.Events.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    MacroEvent ev = macro.Events[i];

                    // Ignorer un déplacement, c'est aussi ignorer le temps qu'il portait.
                    if (Skip(ev, heldButtons))
                    {
                        continue;
                    }

                    await PrecisionDelay.WaitAsync(ev.DelayMs / SpeedFactor, ct).ConfigureAwait(false);
                    lastPosition = Send(ev, context, macro.AnchorMode) ?? lastPosition;
                    Track(heldButtons, heldKeys, ev);

                    Progress?.Invoke(this, new MacroProgress(repeat + 1, repeats, i + 1, macro.Events.Count));
                }
            }
        }
        finally
        {
            ReleaseHeld(heldButtons, heldKeys, lastPosition);

            NativeMethods.timeEndPeriod(1);

            if (RestoreCursorPosition)
            {
                _sink.MoveTo(origin.x, origin.y);
            }

            _sink.End();
        }
    }

    /// <summary>
    /// Relâche tout ce que le rejeu a laissé enfoncé.
    /// </summary>
    /// <remarks>
    /// Indispensable, et pas seulement en fin normale : une interruption par Échap, une
    /// fenêtre cible disparue ou une exception laisseraient sinon un bouton ou une touche
    /// modificatrice physiquement enfoncés <b>pour tout le poste</b>. Un Ctrl bloqué ou un
    /// bouton gauche bloqué survivent à la fermeture de l'application et ne se corrigent
    /// qu'en appuyant à nouveau sur la touche.
    /// </remarks>
    private void ReleaseHeld(
        HashSet<MouseButton> buttons, Dictionary<ushort, KeyboardEvent> keys, (int X, int Y) position)
    {
        foreach (MouseButton button in buttons)
        {
            _sink.Button(
                new MouseButtonEvent { Button = button, IsDown = false, X = position.X, Y = position.Y },
                position.X,
                position.Y);
        }

        buttons.Clear();

        foreach (KeyboardEvent key in keys.Values)
        {
            _sink.Key(key with { IsKeyUp = true, DelayMs = 0 });
        }

        keys.Clear();
    }

    /// <summary>Fenêtre cible résolue et correction déduite du repère visuel.</summary>
    private readonly record struct PlaybackContext(nint Target, int OffsetX, int OffsetY)
    {
        public static PlaybackContext None => new(0, 0, 0);
    }

    /// <summary>Retrouve la fenêtre cible, l'active et vérifie le repère visuel.</summary>
    private async Task<PlaybackContext> PrepareTargetAsync(Macro macro, CancellationToken ct)
    {
        LastReferenceScore = null;

        if (macro.Target is null)
        {
            return PlaybackContext.None;
        }

        nint target = WindowLocator.Find(macro.Target);

        if (target == 0)
        {
            // Sans la fenêtre, les positions enregistrées ne veulent plus rien dire :
            // rejouer en coordonnées écran cliquerait à l'aveugle.
            if (macro.IsWindowRelative || macro.Reference is not null)
            {
                throw new AutoClicException(
                    ErrorCode.TargetWindowNotFound,
                    $"Target window '{macro.Target}' was not found.",
                    new Dictionary<string, object?> { ["target"] = macro.Target?.ToString() });
            }

            return PlaybackContext.None;
        }

        if (ActivateTarget)
        {
            WindowLocator.BringToFront(target);
            await PrecisionDelay.WaitAsync(ActivationDelayMs, ct).ConfigureAwait(false);
        }

        if (macro.Reference is null)
        {
            return new PlaybackContext(target, 0, 0);
        }

        ReferenceCheck check = ReferenceResolver.Verify(target, macro.Reference, macro.AnchorMode);
        LastReferenceScore = check.Score;

        if (!check.Found)
        {
            throw check.Error ?? new AutoClicException(ErrorCode.Unknown, "Landmark check failed.");
        }

        return new PlaybackContext(target, check.OffsetX, check.OffsetY);
    }

    /// <summary>
    /// Position écran à viser : recalculée depuis l'ancrage quand la fenêtre est
    /// connue, sinon repli sur les coordonnées de l'enregistrement.
    /// </summary>
    private static (int X, int Y) Resolve(MouseEvent ev, PlaybackContext context, AnchorMode mode)
    {
        if (ev.Anchor is null
            || context.Target == 0
            || WindowLocator.ClientSize(context.Target) is not { } size)
        {
            return (ev.X, ev.Y);
        }

        (int clientX, int clientY) = AnchorSolver.Resolve(ev.Anchor, size.Width, size.Height, mode);

        // L'écart mesuré sur le repère s'applique à tous les points : il capte le
        // décalage résiduel que la géométrie seule ne prédit pas.
        clientX = Math.Clamp(clientX + context.OffsetX, 0, Math.Max(0, size.Width - 1));
        clientY = Math.Clamp(clientY + context.OffsetY, 0, Math.Max(0, size.Height - 1));

        return WindowLocator.ToScreen(context.Target, clientX, clientY) ?? (ev.X, ev.Y);
    }

    /// <summary>
    /// Vrai si l'événement ne doit être ni attendu ni envoyé.
    /// </summary>
    /// <remarks>
    /// Un déplacement bouton enfoncé fait partie d'un glisser : l'ignorer réduirait le
    /// geste à un appui suivi d'un relâchement ailleurs, ce que beaucoup d'applications
    /// ne reconnaissent pas.
    /// </remarks>
    private bool Skip(MacroEvent ev, HashSet<MouseButton> held) =>
        !ReplayMouseMoves && ev is MouseMoveEvent && held.Count == 0;

    private static void Track(
        HashSet<MouseButton> buttons, Dictionary<ushort, KeyboardEvent> keys, MacroEvent ev)
    {
        switch (ev)
        {
            case MouseButtonEvent button when button.IsDown:
                buttons.Add(button.Button);
                break;

            case MouseButtonEvent button:
                buttons.Remove(button.Button);
                break;

            case KeyboardEvent key when key.IsKeyUp:
                keys.Remove(key.VirtualKey);
                break;

            case KeyboardEvent key:
                keys[key.VirtualKey] = key;
                break;
        }
    }

    /// <summary>Envoie l'événement et renvoie la position visée, s'il en avait une.</summary>
    private (int X, int Y)? Send(MacroEvent ev, PlaybackContext context, AnchorMode mode)
    {
        switch (ev)
        {
            case KeyboardEvent k:
                _sink.Key(k);
                return null;

            case MouseMoveEvent m:
            {
                (int x, int y) = Resolve(m, context, mode);
                _sink.MoveTo(x, y);
                return (x, y);
            }

            case MouseButtonEvent b:
            {
                (int x, int y) = Resolve(b, context, mode);
                _sink.Button(b, x, y);
                return (x, y);
            }

            case MouseWheelEvent w:
            {
                (int x, int y) = Resolve(w, context, mode);
                _sink.Wheel(w, x, y);
                return (x, y);
            }

            default:
                return null;
        }
    }
}
