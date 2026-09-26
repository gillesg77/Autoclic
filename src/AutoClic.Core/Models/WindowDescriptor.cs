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

/// <summary>
/// Identité durable d'une fenêtre cible. Le handle change à chaque lancement de
/// l'application visée : on retient de quoi la retrouver.
/// </summary>
public sealed record WindowDescriptor
{
    /// <summary>Nom du processus, sans « .exe ». Critère obligatoire au moment de retrouver la fenêtre.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Classe Win32 : stable d'une session à l'autre, contrairement au titre.</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>Titre au moment de l'enregistrement. Simple indice — beaucoup d'applications le modifient.</summary>
    public string Title { get; init; } = string.Empty;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{Title} — {ProcessName}";
}
