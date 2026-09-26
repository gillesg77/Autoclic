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

public class MacroPlayerTests
{
    /// <summary>Consigne ce que le lecteur envoie, au lieu de l'injecter dans le système.</summary>
    private sealed class RecordingSink : IInputSink
    {
        public List<string> Actions { get; } = [];

        public List<(int X, int Y)> Positions { get; } = [];

        public void MoveTo(int screenX, int screenY)
        {
            Actions.Add($"move {screenX},{screenY}");
            Positions.Add((screenX, screenY));
        }

        public void Button(MouseButtonEvent button, int screenX, int screenY) =>
            Actions.Add($"{(button.IsDown ? "down" : "up")} {screenX},{screenY}");

        public void Wheel(MouseWheelEvent wheel, int screenX, int screenY) =>
            Actions.Add($"wheel {wheel.Delta}");

        public void Key(KeyboardEvent key) =>
            Actions.Add($"key {key.VirtualKey:X2}{(key.IsKeyUp ? "-up" : "-down")}");
    }

    private static MouseMoveEvent Move(int x, int y, int delay = 10) =>
        new() { DelayMs = delay, X = x, Y = y };

    private static MouseButtonEvent Press(int x, int y, int delay = 10) =>
        new() { DelayMs = delay, X = x, Y = y, Button = MouseButton.Left, IsDown = true };

    private static MouseButtonEvent Release(int x, int y, int delay = 10) =>
        new() { DelayMs = delay, X = x, Y = y, Button = MouseButton.Left, IsDown = false };

    private static Macro MacroOf(params MacroEvent[] events) => new() { Events = [.. events] };

    /// <summary>Lecteur sans fenêtre cible et sans attente perceptible.</summary>
    private static MacroPlayer PlayerFor(RecordingSink sink) => new(sink)
    {
        SpeedFactor = 500,
        RestoreCursorPosition = false,
        ActivateTarget = false,
    };

    [Fact]
    public async Task PlayAsync_SendsEveryEventInOrder()
    {
        RecordingSink sink = new();

        await PlayerFor(sink).PlayAsync(MacroOf(
            Move(10, 10),
            Press(20, 20),
            Release(20, 20),
            new KeyboardEvent { DelayMs = 5, VirtualKey = 0x41 },
            new KeyboardEvent { DelayMs = 5, VirtualKey = 0x41, IsKeyUp = true }));

        Assert.Equal(
            ["move 10,10", "down 20,20", "up 20,20", "key 41-down", "key 41-up"],
            sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_TeleportsBetweenClicks_WhenMovesAreIgnored()
    {
        RecordingSink sink = new();

        MacroPlayer player = PlayerFor(sink);
        player.ReplayMouseMoves = false;

        await player.PlayAsync(MacroOf(
            Press(0, 0), Release(0, 0),
            Move(100, 100), Move(200, 200), Move(300, 300),
            Press(300, 300), Release(300, 300)));

        // Le curseur ne passe par aucune position intermédiaire.
        Assert.Equal(["down 0,0", "up 0,0", "down 300,300", "up 300,300"], sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_StillReplaysTheMovesOfADrag_WhenMovesAreIgnored()
    {
        RecordingSink sink = new();

        MacroPlayer player = PlayerFor(sink);
        player.ReplayMouseMoves = false;

        await player.PlayAsync(MacroOf(
            Press(0, 0),
            Move(50, 50), Move(100, 100),
            Release(100, 100),
            Move(400, 400),
            Press(500, 500),
            Release(500, 500)));

        // Réduire un glisser à un appui puis un relâchement ailleurs ne serait pas
        // reconnu comme un glisser par la plupart des applications.
        Assert.Equal(
            ["down 0,0", "move 50,50", "move 100,100", "up 100,100", "down 500,500", "up 500,500"],
            sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_RepeatsTheWholeMacro()
    {
        RecordingSink sink = new();

        MacroPlayer player = PlayerFor(sink);
        player.RepeatCount = 3;

        await player.PlayAsync(MacroOf(Press(1, 1), Release(1, 1)));

        Assert.Equal(6, sink.Actions.Count);
        Assert.All(sink.Actions, a => Assert.Contains("1,1", a));
    }

    [Fact]
    public async Task PlayAsync_RestoresTheCursor_WhenAskedTo()
    {
        RecordingSink sink = new();

        MacroPlayer player = PlayerFor(sink);
        player.RestoreCursorPosition = true;

        await player.PlayAsync(MacroOf(Press(50, 60)));

        // Le dernier geste ramène le curseur là où il était avant le rejeu.
        Assert.Equal("down 50,60", sink.Actions[0]);
        Assert.StartsWith("move ", sink.Actions[^1]);
        Assert.Equal(3, sink.Actions.Count);
    }

    [Fact]
    public async Task PlayAsync_StopsOnCancellation()
    {
        RecordingSink sink = new();
        using CancellationTokenSource cts = new();

        MacroPlayer player = new(sink)
        {
            SpeedFactor = 1,
            RestoreCursorPosition = false,
            ActivateTarget = false,
        };

        Macro macro = MacroOf(
            Press(1, 1, delay: 0),
            Press(2, 2, delay: 5000),
            Press(3, 3, delay: 0));

        Task playback = player.PlayAsync(macro, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
        Assert.DoesNotContain("down 3,3", sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_ReleasesAHeldButton_WhenCancelled()
    {
        // Sans cela, interrompre un glisser laisse le bouton gauche physiquement enfoncé
        // pour TOUT le poste, bien après la fermeture de l'application.
        RecordingSink sink = new();
        using CancellationTokenSource cts = new();

        MacroPlayer player = new(sink)
        {
            SpeedFactor = 1,
            RestoreCursorPosition = false,
            ActivateTarget = false,
        };

        Macro macro = MacroOf(
            Press(10, 10, delay: 0),
            Move(50, 50, delay: 0),
            Move(90, 90, delay: 5000),
            Release(90, 90));

        Task playback = player.PlayAsync(macro, cts.Token);
        await Task.Delay(120);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);

        Assert.Contains("down 10,10", sink.Actions);
        Assert.EndsWith("up 50,50", sink.Actions[^1]);
    }

    [Fact]
    public async Task PlayAsync_ReleasesAHeldKey_WhenCancelled()
    {
        // Une touche modificatrice restée enfoncée est pire encore qu'un bouton : tout
        // ce que l'utilisateur tape ensuite devient un raccourci.
        RecordingSink sink = new();
        using CancellationTokenSource cts = new();

        MacroPlayer player = new(sink)
        {
            SpeedFactor = 1,
            RestoreCursorPosition = false,
            ActivateTarget = false,
        };

        Macro macro = MacroOf(
            new KeyboardEvent { DelayMs = 0, VirtualKey = 0x11 },                    // Ctrl enfoncé
            new KeyboardEvent { DelayMs = 5000, VirtualKey = 0x11, IsKeyUp = true }); // relâché trop tard

        Task playback = player.PlayAsync(macro, cts.Token);
        await Task.Delay(120);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);

        Assert.Equal(["key 11-down", "key 11-up"], sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_DoesNotReleaseWhatWasAlreadyReleased()
    {
        RecordingSink sink = new();

        await PlayerFor(sink).PlayAsync(MacroOf(
            Press(10, 10),
            Release(10, 10),
            new KeyboardEvent { DelayMs = 0, VirtualKey = 0x41 },
            new KeyboardEvent { DelayMs = 0, VirtualKey = 0x41, IsKeyUp = true }));

        Assert.Equal(["down 10,10", "up 10,10", "key 41-down", "key 41-up"], sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_ReleasesAcrossRepeats()
    {
        // L'état enfoncé ne doit pas être oublié entre deux répétitions.
        RecordingSink sink = new();

        MacroPlayer player = PlayerFor(sink);
        player.RepeatCount = 2;

        await player.PlayAsync(MacroOf(Press(5, 5)));

        Assert.Equal(["down 5,5", "down 5,5", "up 5,5"], sink.Actions);
    }

    [Fact]
    public async Task PlayAsync_RejectsANonPositiveSpeed()
    {
        MacroPlayer player = new(new RecordingSink()) { SpeedFactor = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => player.PlayAsync(MacroOf(Press(0, 0))));
    }
}
