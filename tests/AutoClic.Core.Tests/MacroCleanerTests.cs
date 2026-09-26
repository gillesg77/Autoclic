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
using AutoClic.Core.Recording;
using Xunit;

namespace AutoClic.Core.Tests;

public class MacroCleanerTests
{
    private static MouseMoveEvent Move(int x, int y, int delay = 15) =>
        new() { DelayMs = delay, X = x, Y = y };

    private static MouseButtonEvent Press(int x, int y, int delay = 20) =>
        new() { DelayMs = delay, X = x, Y = y, Button = MouseButton.Left, IsDown = true };

    private static MouseButtonEvent Release(int x, int y, int delay = 10) =>
        new() { DelayMs = delay, X = x, Y = y, Button = MouseButton.Left, IsDown = false };

    private static Macro MacroOf(params MacroEvent[] events) => new() { Events = [.. events] };

    private static WindowAnchor Anchor(int x, int y, int width = 800, int height = 600) =>
        new() { ClientX = x, ClientY = y, ClientWidth = width, ClientHeight = height };

    /// <summary>Recherche de trois secondes en zigzag, puis clic — le cas qui posait problème.</summary>
    private static Macro SearchThenClick()
    {
        List<MacroEvent> events = [Press(0, 0), Release(0, 0)];

        // 200 déplacements de 15 ms : trois secondes d'hésitation.
        for (int i = 1; i <= 200; i++)
        {
            events.Add(Move(i * 4, (i % 20) * 15, delay: 15));
        }

        events.Add(Press(800, 0, delay: 50));
        events.Add(Release(800, 0));

        return new Macro { Events = events };
    }

    [Fact]
    public void Clean_RemovesTheMovesBetweenClicks()
    {
        Macro macro = MacroOf(
            Press(10, 10), Release(10, 10),
            Move(120, 40), Move(240, 90), Move(360, 150), Move(400, 200),
            Press(400, 200), Release(400, 200));

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(
            macro, new CleanupOptions { StraightLineMoves = false });

        Assert.Equal(4, report.RemovedMoves);
        Assert.DoesNotContain(cleaned.Events, e => e is MouseMoveEvent);
        Assert.Equal(4, cleaned.Events.Count);
    }

    [Fact]
    public void Clean_ProducesAContinuousTrajectory()
    {
        // Le défaut d'origine : le temps d'hésitation finançait le trajet, ce qui
        // donnait des sauts de 40 px espacés de 140 ms — environ 7 images par seconde.
        (Macro cleaned, _) = MacroCleaner.Clean(SearchThenClick(), new CleanupOptions());

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.True(moves.Length >= 20, $"Trajet trop grossier : {moves.Length} points.");

        // Aucun intervalle ne doit dépasser 20 ms, sans quoi le déplacement se hache.
        Assert.All(moves, m => Assert.InRange(m.DelayMs, 0, 20));

        // Et aucun saut ne doit dépasser une cinquantaine de pixels.
        for (int i = 1; i < moves.Length; i++)
        {
            double jump = Math.Sqrt(
                Math.Pow(moves[i].X - moves[i - 1].X, 2) + Math.Pow(moves[i].Y - moves[i - 1].Y, 2));

            Assert.True(jump <= 60, $"Saut de {jump:F0} px entre deux points.");
        }
    }

    [Fact]
    public void Clean_DoesNotSpendTheSearchTimeOnTheTrajectory()
    {
        // Trois secondes de recherche ne doivent pas devenir trois secondes de trajet.
        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(SearchThenClick(), new CleanupOptions());

        Assert.True(
            report.DurationAfter < report.DurationBefore,
            $"La macro devrait raccourcir : {report.DurationBefore} → {report.DurationAfter}.");

        int travel = cleaned.Events.OfType<MouseMoveEvent>().Sum(m => m.DelayMs);

        Assert.InRange(travel, 1, 500);
    }

    [Fact]
    public void Clean_KeepsTheTotalDuration_WhenTheSearchTimeIsPreserved()
    {
        Macro macro = MacroOf(
            Press(10, 10, delay: 0), Release(10, 10),
            Move(100, 100, delay: 30), Move(200, 200, delay: 30), Move(300, 300, delay: 30),
            Press(300, 300), Release(300, 300));

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(
            macro, new CleanupOptions { KeepIdleTime = true });

        // Le temps supprimé est rendu : en pause avant le départ, puis en trajet.
        Assert.Equal(report.DurationBefore, report.DurationAfter);
        Assert.Equal(macro.Duration, cleaned.Duration);
    }

    [Fact]
    public void Clean_PreservesTheWaitsCarriedByClicks()
    {
        // Deux secondes d'attente après le clic : c'est l'application qui répond, pas
        // de l'hésitation. Ce délai doit survivre intact.
        Macro macro = MacroOf(
            Press(0, 0), Release(0, 0),
            Move(200, 200),
            Press(400, 0, delay: 2000));

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions());

        MouseButtonEvent arrival = cleaned.Events.OfType<MouseButtonEvent>().Last();

        Assert.Equal(2000, arrival.DelayMs);
    }

    [Fact]
    public void Clean_ReplacesTheDetourWithAStraightLine()
    {
        Macro macro = MacroOf(
            Press(0, 0), Release(0, 0),
            Move(0, 300), Move(200, 300), Move(400, 300), Move(400, 0),
            Press(400, 0), Release(400, 0));

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions());

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.Equal(0, m.Y));
        Assert.All(moves, m => Assert.InRange(m.X, 0, 400));

        // Progression monotone vers la cible, malgré l'accélération puis la décélération.
        Assert.True(moves.Zip(moves.Skip(1)).All(pair => pair.Second.X >= pair.First.X));
    }

    [Fact]
    public void Clean_EasesInAndOut()
    {
        // Une progression à vitesse constante se repère comme mécanique : les pas du
        // milieu doivent être plus longs que ceux des extrémités.
        List<MacroEvent> events = [Press(0, 0), Release(0, 0)];

        for (int i = 1; i <= 20; i++)
        {
            events.Add(Move(i * 50, 0, delay: 15));
        }

        events.Add(Press(1000, 0));

        (Macro cleaned, _) = MacroCleaner.Clean(new Macro { Events = events }, new CleanupOptions());

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.True(moves.Length >= 8, $"Trop peu de points pour juger : {moves.Length}.");

        int first = moves[1].X - moves[0].X;
        int middle = moves[moves.Length / 2].X - moves[(moves.Length / 2) - 1].X;

        Assert.True(middle > first, $"Pas du milieu {middle} px, pas du début {first} px.");
    }

    [Fact]
    public void Clean_KeepsTheMovesOfADrag()
    {
        Macro macro = MacroOf(
            Press(10, 10),
            Move(50, 50), Move(90, 90), Move(130, 130),
            Release(130, 130),
            Move(400, 400), Move(500, 500),
            Press(500, 500), Release(500, 500));

        (Macro cleaned, _) = MacroCleaner.Clean(
            macro, new CleanupOptions { StraightLineMoves = false });

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.Equal(3, moves.Length);
        Assert.Equal([50, 90, 130], moves.Select(m => m.X));
    }

    [Fact]
    public void Clean_SimplifiesADrag_WhenAskedTo()
    {
        Macro macro = MacroOf(
            Press(0, 0),
            Move(30, 120), Move(60, 250), Move(120, 180), Move(300, 0),
            Release(300, 0));

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions { SimplifyDrags = true });

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.All(moves, m => Assert.Equal(0, m.Y)); // le crochet est redressé
        Assert.True(moves.Length >= 2, "Un glisser simplifié garde des positions intermédiaires.");
    }

    [Fact]
    public void Clean_KeepsIntermediatePositions_EvenForAShortDrag()
    {
        // Sans positions intermédiaires, ni le seuil de déclenchement du glisser ni la
        // cible de dépôt ne verraient passer le curseur.
        Macro macro = MacroOf(
            Press(100, 100),
            Move(105, 102),
            Release(110, 104));

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(
            macro, new CleanupOptions { SimplifyDrags = true });

        Assert.True(report.InsertedMoves >= 2, $"{report.InsertedMoves} point(s) inséré(s).");
        Assert.Equal(report.InsertedMoves, cleaned.Events.OfType<MouseMoveEvent>().Count());
    }

    [Fact]
    public void Clean_OnlyInterpolatesWhereMovesWereActuallyRemoved()
    {
        // Les trois derniers clics n'ont aucun déplacement entre eux : rien à relier.
        // Un compteur cumulatif au lieu d'un drapeau par intervalle insérerait ici des
        // points de passage fantômes.
        Macro macro = MacroOf(
            Press(0, 0), Release(0, 0),
            Move(300, 300),
            Press(300, 300), Release(300, 300),
            Press(300, 300), Release(300, 300));

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(macro, new CleanupOptions());

        int firstArrival = cleaned.Events.FindIndex(e => e is MouseButtonEvent { X: 300 });

        Assert.Equal(report.InsertedMoves, cleaned.Events.OfType<MouseMoveEvent>().Count());
        Assert.All(cleaned.Events.Skip(firstArrival), e => Assert.IsNotType<MouseMoveEvent>(e));
    }

    [Fact]
    public void Clean_InterpolatesTheAnchorAlongsideTheScreenPosition()
    {
        Macro macro = MacroOf(
            new MouseButtonEvent { DelayMs = 0, X = 100, Y = 100, Anchor = Anchor(0, 0), Button = MouseButton.Left, IsDown = true },
            new MouseButtonEvent { DelayMs = 10, X = 100, Y = 100, Anchor = Anchor(0, 0), Button = MouseButton.Left, IsDown = false },
            new MouseMoveEvent { DelayMs = 20, X = 150, Y = 300, Anchor = Anchor(50, 200) },
            new MouseButtonEvent { DelayMs = 20, X = 500, Y = 100, Anchor = Anchor(400, 0), Button = MouseButton.Left, IsDown = true });

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions());

        MouseMoveEvent[] moves = [.. cleaned.Events.OfType<MouseMoveEvent>()];

        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.NotNull(m.Anchor));

        // L'ancrage suit la même droite que les coordonnées écran : (0,0) → (400,0).
        Assert.All(moves, m => Assert.Equal(0, m.Anchor!.ClientY));
        Assert.All(moves, m => Assert.Equal(m.X - 100, m.Anchor!.ClientX));
    }

    [Fact]
    public void Clean_DoesNotInterpolateAcrossDifferentWindowSizes()
    {
        // Deux tailles clientes : les coordonnées ne sont pas comparables, on ne
        // fabrique pas d'ancrage douteux.
        Macro macro = MacroOf(
            new MouseButtonEvent { DelayMs = 0, X = 0, Y = 0, Anchor = Anchor(0, 0), Button = MouseButton.Left, IsDown = true },
            new MouseButtonEvent { DelayMs = 10, X = 0, Y = 0, Anchor = Anchor(0, 0), Button = MouseButton.Left, IsDown = false },
            Move(200, 0),
            new MouseButtonEvent
            {
                DelayMs = 20, X = 400, Y = 0, Button = MouseButton.Left, IsDown = true,
                Anchor = Anchor(400, 0, width: 1024, height: 768),
            });

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions());

        Assert.NotEmpty(cleaned.Events.OfType<MouseMoveEvent>());
        Assert.All(cleaned.Events.OfType<MouseMoveEvent>(), m => Assert.Null(m.Anchor));
    }

    [Fact]
    public void Clean_CapsOnlyTheRealWaits()
    {
        Macro macro = MacroOf(
            Press(0, 0), Release(0, 0),
            Move(10, 10, delay: 4000),
            Press(10, 10, delay: 3000));

        (Macro cleaned, _) = MacroCleaner.Clean(
            macro, new CleanupOptions { MaxDelayMs = 500, StraightLineMoves = false });

        Assert.All(cleaned.Events, e => Assert.InRange(e.DelayMs, 0, 500));
    }

    [Fact]
    public void Clean_LeavesShortHopsAlone()
    {
        // Déplacement négligeable : y insérer des points n'apporterait rien.
        Macro macro = MacroOf(
            Press(100, 100), Release(100, 100),
            Move(102, 101),
            Press(103, 102));

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(macro, new CleanupOptions());

        Assert.Equal(0, report.InsertedMoves);
        Assert.Equal(3, cleaned.Events.Count);
    }

    [Fact]
    public void Clean_KeepsTheMacroIdentity()
    {
        Macro macro = new()
        {
            Name = "Ma macro",
            Target = new WindowDescriptor { ProcessName = "Battle.net", Title = "Battle.net" },
            AnchorMode = AnchorMode.Proportional,
            Events = [Press(0, 0), Release(0, 0), Move(200, 200), Press(200, 200)],
        };

        (Macro cleaned, _) = MacroCleaner.Clean(macro, new CleanupOptions());

        Assert.Equal("Ma macro", cleaned.Name);
        Assert.Equal(macro.Target, cleaned.Target);
        Assert.Equal(AnchorMode.Proportional, cleaned.AnchorMode);
    }

    [Fact]
    public void Clean_DoesNotTouchTheOriginal()
    {
        Macro macro = MacroOf(Press(0, 0), Release(0, 0), Move(300, 300), Press(300, 300));

        MacroCleaner.Clean(macro, new CleanupOptions());

        Assert.Equal(4, macro.Events.Count);
        Assert.Contains(macro.Events, e => e is MouseMoveEvent);
    }
}
