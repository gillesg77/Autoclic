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

using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Storage;
using Xunit;

namespace AutoClic.Core.Tests;

public class MacroStorageTests
{
    private static Macro SampleMacro() => new()
    {
        Name = "Test",
        Events =
        [
            new KeyboardEvent { DelayMs = 0, VirtualKey = 0x41, ScanCode = 0x1E, IsKeyUp = false },
            new KeyboardEvent { DelayMs = 40, VirtualKey = 0x41, ScanCode = 0x1E, IsKeyUp = true },
            new MouseMoveEvent { DelayMs = 15, X = 120, Y = 340 },
            new MouseButtonEvent { DelayMs = 60, Button = MouseButton.Right, IsDown = true, X = 120, Y = 340 },
            new MouseWheelEvent { DelayMs = 25, Delta = -120, IsHorizontal = false, X = 120, Y = 340 },
        ],
    };

    [Fact]
    public void RoundTrip_PreservesEveryEventType()
    {
        Macro original = SampleMacro();

        Macro restored = MacroStorage.Deserialize(MacroStorage.Serialize(original));

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Events, restored.Events);
    }

    [Fact]
    public void RoundTrip_KeepsDerivedTypes()
    {
        Macro restored = MacroStorage.Deserialize(MacroStorage.Serialize(SampleMacro()));

        Assert.Collection(
            restored.Events,
            e => Assert.IsType<KeyboardEvent>(e),
            e => Assert.IsType<KeyboardEvent>(e),
            e => Assert.IsType<MouseMoveEvent>(e),
            e => Assert.IsType<MouseButtonEvent>(e),
            e => Assert.IsType<MouseWheelEvent>(e));
    }

    [Fact]
    public void Duration_IsTheSumOfTheDelays()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(140), SampleMacro().Duration);
    }

    [Fact]
    public void Duration_OfAnEmptyMacro_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, new Macro().Duration);
    }

    [Fact]
    public void Deserialize_RejectsJsonNull()
    {
        // Le coeur leve desormais une erreur typee, que l'interface traduit.
        AutoClicException erreur = Assert.Throws<AutoClicException>(() => MacroStorage.Deserialize("null"));
        Assert.Equal(ErrorCode.MacroFileInvalid, erreur.Code);
    }

    [Fact]
    public async Task SaveAsync_CreatesTheTargetFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"macros-test-{Guid.NewGuid():N}");
        string path = Path.Combine(folder, "sous-dossier", "macro.json");

        try
        {
            await MacroStorage.SaveAsync(SampleMacro(), path);

            Macro reloaded = await MacroStorage.LoadAsync(path);
            Assert.Equal(5, reloaded.Events.Count);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
