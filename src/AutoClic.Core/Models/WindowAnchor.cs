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

namespace AutoClic.Core.Models;

/// <summary>Comment reporter une position quand la fenêtre cible a changé de taille.</summary>
public enum AnchorMode
{
    /// <summary>
    /// Chaque axe garde sa distance au bord le plus proche au moment de
    /// l'enregistrement. C'est le comportement des interfaces réelles, où les
    /// commandes sont ancrées à un bord et non étirées.
    /// </summary>
    Auto,

    /// <summary>Distance au coin haut-gauche conservée telle quelle.</summary>
    TopLeft,

    /// <summary>Position mise à l'échelle de la fenêtre. Adapté à un contenu qui s'étire vraiment.</summary>
    Proportional,
}

/// <summary>
/// Position d'un événement souris exprimée dans la zone cliente de la fenêtre cible,
/// accompagnée de la taille de cette zone à l'enregistrement — sans quoi aucune règle
/// de redimensionnement n'est calculable.
/// </summary>
public sealed record WindowAnchor
{
    public int ClientX { get; init; }

    public int ClientY { get; init; }

    public int ClientWidth { get; init; }

    public int ClientHeight { get; init; }
}
