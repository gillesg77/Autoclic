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

using System.Text.Json.Serialization;

namespace AutoClic.Core.Models;

public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>
/// Un pas d'une macro. Le délai est relatif à l'événement précédent, ce qui rend
/// la macro rejouable telle quelle et lisible sans état global.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyboardEvent), "key")]
[JsonDerivedType(typeof(MouseMoveEvent), "move")]
[JsonDerivedType(typeof(MouseButtonEvent), "button")]
[JsonDerivedType(typeof(MouseWheelEvent), "wheel")]
public abstract record MacroEvent
{
    /// <summary>Millisecondes écoulées depuis l'événement précédent (0 pour le premier).</summary>
    public int DelayMs { get; init; }

}

public sealed record KeyboardEvent : MacroEvent
{
    public ushort VirtualKey { get; init; }

    public ushort ScanCode { get; init; }

    public bool IsKeyUp { get; init; }

    /// <summary>Touche étendue (pavé numérique, Ctrl/Alt droits, flèches...).</summary>
    public bool IsExtended { get; init; }

}

/// <summary>Base des événements souris : position écran, et ancrage fenêtre quand il existe.</summary>
public abstract record MouseEvent : MacroEvent
{
    /// <summary>Coordonnées écran absolues. Servent de repli quand <see cref="Anchor"/> est absent.</summary>
    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>
    /// Position dans la zone cliente de la fenêtre cible. Renseignée seulement si une
    /// cible était sélectionnée et que le point tombait dedans.
    /// </summary>
    public WindowAnchor? Anchor { get; init; }

}

public sealed record MouseMoveEvent : MouseEvent
{
}

public sealed record MouseButtonEvent : MouseEvent
{
    public MouseButton Button { get; init; }

    public bool IsDown { get; init; }

}

public sealed record MouseWheelEvent : MouseEvent
{
    /// <summary>Multiple de 120 ; positif = vers le haut / la droite.</summary>
    public int Delta { get; init; }

    public bool IsHorizontal { get; init; }
}
