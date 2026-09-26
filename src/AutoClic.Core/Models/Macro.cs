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

/// <summary>Une séquence enregistrée, telle qu'elle est sérialisée sur disque.</summary>
public sealed class Macro
{
    public string Name { get; set; } = "Nouvelle macro";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Fenêtre à laquelle les positions se rapportent. Nulle = macro en coordonnées
    /// écran, donc dépendante de la disposition du bureau.
    /// </summary>
    public WindowDescriptor? Target { get; set; }

    public AnchorMode AnchorMode { get; set; } = AnchorMode.Auto;

    /// <summary>
    /// Repère visuel facultatif. Présent, il conditionne le rejeu : introuvable, la
    /// macro s'interrompt au lieu de cliquer sur ce qui se trouve à la place.
    /// </summary>
    public VisualReference? Reference { get; set; }

    public List<MacroEvent> Events { get; set; } = [];

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Events.Sum(e => (long)e.DelayMs));

    /// <summary>Vrai si au moins un événement est repérable par rapport à la fenêtre cible.</summary>
    [JsonIgnore]
    public bool IsWindowRelative => Target is not null && Events.OfType<MouseEvent>().Any(e => e.Anchor is not null);
}
