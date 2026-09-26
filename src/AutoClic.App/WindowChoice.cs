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

using AutoClic.Core.Native;
using AutoClic.Localization;

namespace AutoClic.App;

/// <summary>
/// Entrée de la liste des fenêtres cibles. <see cref="Window"/> nul = macro en
/// coordonnées écran.
/// </summary>
/// <remarks>
/// Classe à propriétés inscriptibles pour la même raison que <see cref="EventRow"/> :
/// le générateur XamlTypeInfo refuse les propriétés « init ».
/// </remarks>
public sealed class WindowChoice
{
    public WindowChoice(LiveWindow? window)
    {
        Window = window;
        Label = window is null
            ? Localizer.Current["target.none"]
            : $"{window.Title}  ·  {window.ProcessName}";
    }

    public LiveWindow? Window { get; set; }

    public string Label { get; set; }
}
