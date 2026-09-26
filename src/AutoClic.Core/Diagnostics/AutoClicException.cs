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

namespace AutoClic.Core.Diagnostics;

/// <summary>Motifs d'échec que l'interface doit savoir formuler.</summary>
public enum ErrorCode
{
    Unknown,

    /// <summary>La fenêtre cible n'a pas été retrouvée parmi les fenêtres ouvertes.</summary>
    TargetWindowNotFound,

    /// <summary>La fenêtre existe mais n'a pas pu être photographiée.</summary>
    WindowCaptureFailed,

    /// <summary>Le repère ne contient aucune image exploitable.</summary>
    ReferenceEmpty,

    /// <summary>Le motif ne tient pas dans la zone cliente, ou le détourage est vide.</summary>
    ReferenceUnsearchable,

    /// <summary>Le repère a été cherché mais le score reste sous le seuil.</summary>
    ReferenceNotFound,

    /// <summary>UIPI a refusé l'injection : la cible est plus privilégiée que nous.</summary>
    InputBlocked,

    /// <summary>Le système a refusé l'installation d'un hook bas niveau.</summary>
    HookInstallFailed,

    /// <summary>Le fichier ne contient pas de macro exploitable.</summary>
    MacroFileInvalid,

    /// <summary>Aucune fenêtre cible n'a été désignée.</summary>
    NoTargetWindow,
}

/// <summary>
/// Erreur destinée à être montrée à l'utilisateur.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> reste en anglais, pour les journaux et le
/// débogage ; l'interface, elle, affiche la traduction de <see cref="Code"/>. Le cœur
/// n'a ainsi aucun texte d'interface à porter, et reste traduisible sans le modifier.
/// </remarks>
public sealed class AutoClicException : Exception
{
    public AutoClicException(
        ErrorCode code,
        string debugMessage,
        IReadOnlyDictionary<string, object?>? args = null,
        Exception? inner = null)
        : base(debugMessage, inner)
    {
        Code = code;
        Args = args ?? new Dictionary<string, object?>();
    }

    public ErrorCode Code { get; }

    /// <summary>Paramètres à injecter dans le message traduit, sous la forme {nom}.</summary>
    public IReadOnlyDictionary<string, object?> Args { get; }
}
