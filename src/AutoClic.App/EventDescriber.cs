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

using AutoClic.Core.Models;
using AutoClic.Localization;

namespace AutoClic.App;

/// <summary>
/// Met un événement de macro en mots, dans la langue courante.
/// </summary>
/// <remarks>
/// Cette mise en forme vit dans l'application et non dans le cœur : un libellé
/// destiné à être lu est du ressort de l'interface, et le cœur resterait sinon
/// intraduisible sans le modifier.
/// </remarks>
internal static class EventDescriber
{
    public static string Describe(MacroEvent ev)
    {
        Localizer loc = Localizer.Current;

        return ev switch
        {
            KeyboardEvent k => loc.Format(
                k.IsKeyUp ? "event.key.up" : "event.key.down",
                $"0x{k.VirtualKey:X2}"),

            MouseMoveEvent m => loc.Format("event.move", Position(loc, m)),

            MouseButtonEvent b => loc.Format(
                b.IsDown ? "event.button.down" : "event.button.up",
                b.Button,
                Position(loc, b)),

            MouseWheelEvent w => loc.Format(
                w.IsHorizontal ? "event.wheel.horizontal" : "event.wheel.vertical",
                w.Delta.ToString("+#;-#;0", System.Globalization.CultureInfo.CurrentCulture),
                Position(loc, w)),

            _ => string.Empty,
        };
    }

    /// <summary>
    /// Position telle qu'elle a du sens pour le lecteur.
    /// </summary>
    /// <remarks>
    /// Les coordonnées clientes priment quand elles existent : ce sont elles qui
    /// serviront au rejeu, et les annoncer évite de faire croire que la macro dépend
    /// de la position de la fenêtre.
    /// </remarks>
    private static string Position(Localizer loc, MouseEvent ev) =>
        ev.Anchor is { } anchor
            ? loc.Format("event.position.window", anchor.ClientX, anchor.ClientY)
            : loc.Format("event.position.screen", ev.X, ev.Y);
}
