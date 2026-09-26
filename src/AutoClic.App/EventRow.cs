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

namespace AutoClic.App;

/// <summary>
/// Projection d'un <see cref="AutoClic.Core.Models.MacroEvent"/> pour la liste.
/// </summary>
/// <remarks>
/// Classe à propriétés inscriptibles, et non un record : le générateur XamlTypeInfo
/// produit des setters pour tout type utilisé dans un x:Bind, ce que des propriétés
/// « init » refusent (CS8852). Tout est déjà mis en forme — x:Bind ne convertit pas
/// implicitement vers string.
/// </remarks>
public sealed class EventRow
{
    public EventRow(string index, string delayText, string description)
    {
        Index = index;
        DelayText = delayText;
        Description = description;
    }

    public string Index { get; set; }

    public string DelayText { get; set; }

    public string Description { get; set; }
}
