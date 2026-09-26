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
using AutoClic.Core.Playback;
using Xunit;

namespace AutoClic.Core.Tests;

public class AnchorSolverTests
{
    // Fenêtre de 1000 px de large ramenée à 600 dans la plupart des cas ci-dessous.

    [Theory]
    [InlineData(100, 100)] // moitié gauche : distance au bord gauche conservée
    [InlineData(499, 499)]
    public void Auto_KeepsTheLeftOffset_ForPointsInTheLeftHalf(int recordedX, int expected)
    {
        Assert.Equal(expected, AnchorSolver.Map(recordedX, recorded: 1000, current: 600, AnchorMode.Auto));
    }

    [Theory]
    [InlineData(950, 550)] // 50 px du bord droit à l'enregistrement → 50 px du bord droit au rejeu
    [InlineData(900, 500)]
    public void Auto_KeepsTheRightOffset_ForPointsInTheRightHalf(int recordedX, int expected)
    {
        Assert.Equal(expected, AnchorSolver.Map(recordedX, recorded: 1000, current: 600, AnchorMode.Auto));
    }

    [Fact]
    public void Auto_LeavesEverythingUntouched_WhenTheSizeIsUnchanged()
    {
        foreach (int x in new[] { 0, 1, 499, 500, 501, 999 })
        {
            Assert.Equal(x, AnchorSolver.Map(x, recorded: 1000, current: 1000, AnchorMode.Auto));
        }
    }

    [Fact]
    public void Proportional_ScalesWithTheWindow()
    {
        Assert.Equal(300, AnchorSolver.Map(500, recorded: 1000, current: 600, AnchorMode.Proportional));
        Assert.Equal(60, AnchorSolver.Map(100, recorded: 1000, current: 600, AnchorMode.Proportional));
    }

    [Fact]
    public void TopLeft_IgnoresTheResize()
    {
        Assert.Equal(100, AnchorSolver.Map(100, recorded: 1000, current: 600, AnchorMode.TopLeft));
    }

    [Fact]
    public void Map_NeverLeavesTheClientArea()
    {
        // Fenêtre très réduite : un point ancré à droite tomberait en négatif sans bornage.
        int mapped = AnchorSolver.Map(950, recorded: 1000, current: 40, AnchorMode.Auto);

        Assert.InRange(mapped, 0, 39);
    }

    [Fact]
    public void Map_FallsBackToTheRecordedValue_WhenASizeIsUnknown()
    {
        Assert.Equal(123, AnchorSolver.Map(123, recorded: 0, current: 600, AnchorMode.Auto));
        Assert.Equal(123, AnchorSolver.Map(123, recorded: 1000, current: 0, AnchorMode.Proportional));
    }

    [Fact]
    public void Resolve_MapsBothAxesIndependently()
    {
        var anchor = new WindowAnchor { ClientX = 950, ClientY = 100, ClientWidth = 1000, ClientHeight = 800 };

        (int x, int y) = AnchorSolver.Resolve(anchor, currentWidth: 600, currentHeight: 400, AnchorMode.Auto);

        Assert.Equal(550, x); // ancré à droite
        Assert.Equal(100, y); // ancré en haut
    }
}
