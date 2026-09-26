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

namespace AutoClic.Core.Playback;

/// <summary>
/// Reporte une position enregistrée dans une fenêtre vers la taille qu'a cette
/// fenêtre au moment du rejeu.
/// </summary>
public static class AnchorSolver
{
    /// <summary>Position cliente à viser, bornée à la zone cliente actuelle.</summary>
    public static (int X, int Y) Resolve(WindowAnchor anchor, int currentWidth, int currentHeight, AnchorMode mode)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        return (Map(anchor.ClientX, anchor.ClientWidth, currentWidth, mode),
                Map(anchor.ClientY, anchor.ClientHeight, currentHeight, mode));
    }

    /// <summary>Reporte une coordonnée sur un axe.</summary>
    /// <param name="coord">Position à l'enregistrement.</param>
    /// <param name="recorded">Taille de l'axe à l'enregistrement.</param>
    /// <param name="current">Taille de l'axe au rejeu.</param>
    internal static int Map(int coord, int recorded, int current, AnchorMode mode)
    {
        if (recorded <= 0 || current <= 0)
        {
            return coord;
        }

        int mapped = mode switch
        {
            AnchorMode.TopLeft => coord,

            AnchorMode.Proportional => (int)Math.Round(coord * (double)current / recorded),

            // Auto : la moitié la plus proche l'emporte. Un bouton collé au bord droit
            // conserve sa distance à ce bord ; le mettre à l'échelle le décalerait, car
            // les interfaces ancrent leurs commandes au lieu de les étirer.
            _ => coord * 2 <= recorded
                ? coord
                : current - (recorded - coord),
        };

        // Un clic hors de la zone cliente atteindrait une autre application : on borne.
        return Math.Clamp(mapped, 0, current - 1);
    }
}
