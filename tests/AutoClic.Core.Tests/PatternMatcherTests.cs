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

using AutoClic.Core.Vision;
using Xunit;

namespace AutoClic.Core.Tests;

public class PatternMatcherTests
{
    /// <summary>Scène bruitée mais déterministe : la recherche a de quoi discriminer.</summary>
    private static PixelBuffer Scene(int width, int height, int seed)
    {
        var random = new Random(seed);
        byte[] rgb = new byte[width * height * 3];
        random.NextBytes(rgb);

        return new PixelBuffer(width, height, rgb, Luminance(rgb));
    }

    private static byte[] Luminance(byte[] rgb)
    {
        byte[] gray = new byte[rgb.Length / 3];

        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)(((rgb[i * 3] * 299) + (rgb[(i * 3) + 1] * 587) + (rgb[(i * 3) + 2] * 114)) / 1000);
        }

        return gray;
    }

    /// <summary>Découpe une région de la scène et en fait un motif, masque plein par défaut.</summary>
    private static ReferencePattern Extract(
        PixelBuffer source, int x, int y, int width, int height, Func<int, int, bool>? mask = null)
    {
        byte[] rgb = new byte[width * height * 3];
        bool[] included = new bool[width * height];

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int from = (((y + row) * source.Width) + x + column) * 3;
                int to = ((row * width) + column) * 3;

                rgb[to] = source.Rgb[from];
                rgb[to + 1] = source.Rgb[from + 1];
                rgb[to + 2] = source.Rgb[from + 2];

                included[(row * width) + column] = mask?.Invoke(column, row) ?? true;
            }
        }

        return new ReferencePattern(new PixelBuffer(width, height, rgb, Luminance(rgb)), included);
    }

    private static PixelBuffer WithNoiseAt(PixelBuffer source, int x, int y, int width, int height, int seed)
    {
        byte[] rgb = (byte[])source.Rgb.Clone();
        var random = new Random(seed);

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int index = ((((y + row) * source.Width) + x + column) * 3);
                rgb[index] = (byte)random.Next(256);
                rgb[index + 1] = (byte)random.Next(256);
                rgb[index + 2] = (byte)random.Next(256);
            }
        }

        return new PixelBuffer(source.Width, source.Height, rgb, Luminance(rgb));
    }

    [Theory]
    [InlineData(ReferenceMatching.Pixel)]
    [InlineData(ReferenceMatching.Correlation)]
    public void Find_LocatesAnExactPatch(ReferenceMatching matching)
    {
        PixelBuffer scene = Scene(400, 300, seed: 1);
        ReferencePattern pattern = Extract(scene, 210, 160, 40, 24);

        MatchResult? match = PatternMatcher.Find(scene, pattern, expectedX: 200, expectedY: 150, radius: 40, matching);

        Assert.NotNull(match);
        Assert.Equal(210, match!.Value.X);
        Assert.Equal(160, match.Value.Y);
        Assert.True(match.Value.Score > 0.99, $"Score attendu proche de 1, obtenu {match.Value.Score}.");
    }

    [Fact]
    public void Pixel_ToleratesSmallRenderingDifferences()
    {
        PixelBuffer scene = Scene(300, 200, seed: 2);
        ReferencePattern pattern = Extract(scene, 120, 90, 32, 20);

        // Décalage de 6 niveaux sur toute la scène : l'ordre de grandeur d'un antialiasing
        // ou d'un léger changement de rendu.
        byte[] shifted = scene.Rgb.Select(v => (byte)Math.Min(255, v + 6)).ToArray();
        var altered = new PixelBuffer(scene.Width, scene.Height, shifted, Luminance(shifted));

        MatchResult? tolerant = PatternMatcher.Find(
            altered, pattern, 120, 90, radius: 16, ReferenceMatching.Pixel, tolerance: 12);

        MatchResult? strict = PatternMatcher.Find(
            altered, pattern, 120, 90, radius: 16, ReferenceMatching.Pixel, tolerance: 0);

        Assert.True(tolerant!.Value.Score > 0.99, $"Avec tolérance : {tolerant.Value.Score}.");
        Assert.True(strict!.Value.Score < 0.20, $"Sans tolérance : {strict.Value.Score}.");
    }

    [Fact]
    public void Correlation_IsUnaffectedByAUniformBrightnessShift()
    {
        PixelBuffer scene = Scene(300, 200, seed: 3);
        ReferencePattern pattern = Extract(scene, 120, 90, 32, 20);

        // La corrélation est centrée et normalisée : assombrir ne doit rien changer.
        byte[] darker = scene.Rgb.Select(v => (byte)(v / 2)).ToArray();
        var dimmed = new PixelBuffer(scene.Width, scene.Height, darker, Luminance(darker));

        MatchResult? match = PatternMatcher.Find(
            dimmed, pattern, 120, 90, radius: 16, ReferenceMatching.Correlation);

        Assert.Equal(120, match!.Value.X);
        Assert.Equal(90, match.Value.Y);
        Assert.True(match.Value.Score > 0.95, $"Score attendu élevé malgré l'assombrissement, obtenu {match.Value.Score}.");
    }

    [Fact]
    public void Outline_ExcludesTheSurroundingBackground()
    {
        PixelBuffer scene = Scene(300, 200, seed: 4);

        // Motif de 40×40 dont seul le disque central appartient à l'élément.
        static bool Disc(int x, int y)
        {
            int dx = x - 20;
            int dy = y - 20;
            return (dx * dx) + (dy * dy) <= 12 * 12;
        }

        ReferencePattern outlined = Extract(scene, 100, 80, 40, 40, Disc);
        ReferencePattern whole = Extract(scene, 100, 80, 40, 40);

        // Le fond change, l'élément non : c'est exactement ce que le détourage doit absorber.
        PixelBuffer repainted = WithNoiseAt(scene, 100, 80, 40, 8, seed: 77);

        MatchResult? masked = PatternMatcher.Find(repainted, outlined, 100, 80, radius: 8, ReferenceMatching.Pixel);
        MatchResult? unmasked = PatternMatcher.Find(repainted, whole, 100, 80, radius: 8, ReferenceMatching.Pixel);

        Assert.True(masked!.Value.Score > 0.99, $"Détouré : {masked.Value.Score}.");
        Assert.True(unmasked!.Value.Score < 0.85, $"Non détouré : {unmasked.Value.Score}.");
    }

    [Fact]
    public void Find_ScoresLow_WhenTheElementIsAbsent()
    {
        PixelBuffer scene = Scene(300, 200, seed: 5);
        PixelBuffer elsewhere = Scene(32, 20, seed: 999);
        var stranger = new ReferencePattern(elsewhere, Enumerable.Repeat(true, 32 * 20).ToArray());

        MatchResult? match = PatternMatcher.Find(scene, stranger, 100, 100, radius: 40, ReferenceMatching.Pixel);

        Assert.True(match!.Value.Score < 0.85, $"Score attendu sous le seuil, obtenu {match.Value.Score}.");
    }

    [Fact]
    public void Find_StaysWithinTheSearchRadius()
    {
        PixelBuffer scene = Scene(400, 300, seed: 6);
        ReferencePattern pattern = Extract(scene, 350, 250, 20, 20);

        // Le motif existe, mais hors du rayon : on ne doit pas aller le chercher au loin.
        MatchResult? match = PatternMatcher.Find(scene, pattern, 20, 20, radius: 30, ReferenceMatching.Pixel);

        Assert.InRange(match!.Value.X, 0, 50);
        Assert.InRange(match.Value.Y, 0, 50);
        Assert.True(match.Value.Score < 0.85);
    }

    [Fact]
    public void Find_ReturnsNull_WhenThePatternIsLargerThanTheScene()
    {
        PixelBuffer small = Scene(20, 20, seed: 7);
        PixelBuffer big = Scene(40, 40, seed: 8);
        var pattern = new ReferencePattern(big, Enumerable.Repeat(true, 40 * 40).ToArray());

        Assert.Null(PatternMatcher.Find(small, pattern, 0, 0, radius: 10));
    }

    [Fact]
    public void Find_ReturnsNull_WhenTheOutlineKeepsNothing()
    {
        PixelBuffer scene = Scene(200, 200, seed: 9);
        ReferencePattern empty = Extract(scene, 10, 10, 16, 16, (_, _) => false);

        Assert.Null(PatternMatcher.Find(scene, empty, 10, 10, radius: 20));
    }

    [Fact]
    public void Find_ReturnsNull_ForAFlatPatternInCorrelationMode()
    {
        // Un aplat uni ne porte aucune information de forme.
        byte[] flat = new byte[16 * 16 * 3];
        var pattern = new ReferencePattern(
            new PixelBuffer(16, 16, flat, new byte[16 * 16]),
            Enumerable.Repeat(true, 16 * 16).ToArray());

        Assert.Null(PatternMatcher.Find(Scene(200, 200, 10), pattern, 50, 50, radius: 20, ReferenceMatching.Correlation));
    }

    [Fact]
    public void PixelBuffer_RejectsAMismatchedBufferLength()
    {
        Assert.Throws<ArgumentException>(() => new PixelBuffer(10, 10, new byte[299], new byte[100]));
        Assert.Throws<ArgumentException>(() => new PixelBuffer(10, 10, new byte[300], new byte[99]));
    }
}
