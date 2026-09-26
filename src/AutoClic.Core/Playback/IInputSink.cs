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
/// Destination des entrées produites par un rejeu.
/// </summary>
/// <remarks>
/// Cette indirection existe pour que la simulation et le rejeu réel empruntent le
/// même chemin : mêmes délais, même résolution des positions, même correction issue
/// du repère. Une simulation qui recalculerait les positions de son côté finirait par
/// diverger et ne prouverait plus rien.
/// </remarks>
public interface IInputSink
{
    void MoveTo(int screenX, int screenY);

    void Button(MouseButtonEvent button, int screenX, int screenY);

    void Wheel(MouseWheelEvent wheel, int screenX, int screenY);

    void Key(KeyboardEvent key);

    /// <summary>Appelé une fois avant le premier événement.</summary>
    void Begin(nint targetWindow)
    {
    }

    /// <summary>Appelé à la fin, y compris après une interruption.</summary>
    void End()
    {
    }
}
