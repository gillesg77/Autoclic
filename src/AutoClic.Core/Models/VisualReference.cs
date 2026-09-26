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

using AutoClic.Core.Vision;

namespace AutoClic.Core.Models;

/// <summary>
/// Élément visuel repéré dans la fenêtre cible — un bouton, une icône — qui sert à
/// la fois de preuve que l'interface attendue est bien là et de repère pour corriger
/// les positions.
/// </summary>
/// <remarks>
/// Le calcul géométrique seul ne suffit pas : sous une certaine taille, beaucoup
/// d'interfaces ne déplacent pas leurs commandes mais les masquent. La position
/// déduite reste alors plausible tout en désignant un autre élément. Retrouver un
/// repère visuel est le seul moyen de distinguer les deux cas.
/// </remarks>
public sealed record VisualReference
{
    /// <summary>Nom donné par l'utilisateur, repris dans les messages d'erreur.</summary>
    public string Label { get; init; } = "landmark";

    /// <summary>Coin haut-gauche du repère, en coordonnées clientes à l'enregistrement.</summary>
    public int ClientX { get; init; }

    public int ClientY { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Taille de la zone cliente à l'enregistrement, pour situer le repère après redimensionnement.</summary>
    public int ClientWidth { get; init; }

    public int ClientHeight { get; init; }

    /// <summary>
    /// Image du repère, PNG encodé en base64. Le canal alpha porte le détourage :
    /// les pixels transparents sont exclus de la comparaison.
    /// </summary>
    public string ImagePng { get; init; } = string.Empty;

    /// <summary>Vrai si l'utilisateur a détouré l'élément au lieu de garder tout le rectangle.</summary>
    public bool IsOutlined { get; init; }

    public ReferenceMatching Matching { get; init; } = ReferenceMatching.Pixel;

    /// <summary>
    /// Écart admis par canal en comparaison au pixel. Zéro exige une identité stricte ;
    /// une douzaine de niveaux absorbe l'antialiasing sans laisser passer un autre élément.
    /// </summary>
    public int PixelTolerance { get; init; } = PatternMatcher.DefaultTolerance;

    /// <summary>
    /// Score minimal pour considérer le repère retrouvé — proportion de pixels
    /// concordants, ou corrélation, selon <see cref="Matching"/>.
    /// </summary>
    public double MinimumScore { get; init; } = 0.95;

    /// <summary>Rayon de recherche en pixels autour de la position déduite.</summary>
    public int SearchRadius { get; init; } = 96;
}
