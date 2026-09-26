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

namespace AutoClic.Core.Vision;

/// <summary>Comment décider que le repère est bien là.</summary>
public enum ReferenceMatching
{
    /// <summary>
    /// Proportion de pixels identiques à la tolérance près, en couleur. Strict : ne
    /// laisse pas passer un élément voisin, mais refuse de démarrer sur une différence
    /// de rendu que l'œil ne voit pas.
    /// </summary>
    Pixel,

    /// <summary>
    /// Corrélation croisée normalisée centrée sur la luminance. Insensible aux
    /// variations uniformes de luminosité et de contraste, donc tolérante à un
    /// changement de thème ou à un survol.
    /// </summary>
    Correlation,
}

/// <summary>Meilleure correspondance trouvée : coin haut-gauche et score dans [0, 1].</summary>
public readonly record struct MatchResult(int X, int Y, double Score);

/// <summary>Retrouve un motif détouré dans une image, autour d'une position attendue.</summary>
public static class PatternMatcher
{
    /// <summary>Écart admis par canal, en comparaison au pixel.</summary>
    public const int DefaultTolerance = 12;

    /// <summary>
    /// Cherche <paramref name="pattern"/> dans <paramref name="scene"/>.
    /// </summary>
    /// <param name="radius">
    /// Rayon de recherche en pixels. Le borner évite le coût d'un balayage complet et,
    /// surtout, empêche de reconnaître un élément identique ailleurs dans l'interface.
    /// </param>
    /// <returns>La meilleure correspondance, ou null si la recherche n'a pas de sens.</returns>
    public static MatchResult? Find(
        PixelBuffer scene,
        ReferencePattern pattern,
        int expectedX,
        int expectedY,
        int radius,
        ReferenceMatching matching = ReferenceMatching.Pixel,
        int tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(pattern);

        int maxX = scene.Width - pattern.Width;
        int maxY = scene.Height - pattern.Height;

        if (maxX < 0 || maxY < 0 || pattern.MaskedCount == 0)
        {
            return null; // le motif ne tient pas dans l'image, ou le détourage est vide
        }

        int fromX = Math.Clamp(expectedX - radius, 0, maxX);
        int toX = Math.Clamp(expectedX + radius, 0, maxX);
        int fromY = Math.Clamp(expectedY - radius, 0, maxY);
        int toY = Math.Clamp(expectedY + radius, 0, maxY);

        if (matching == ReferenceMatching.Correlation)
        {
            (double mean, double deviation) = MaskedStatistics(pattern);

            if (deviation < 1e-6)
            {
                return null; // motif uni : rien à reconnaître
            }

            return Scan(fromX, toX, fromY, toY, (x, y, _) => Correlation(scene, pattern, mean, deviation, x, y));
        }

        return Scan(fromX, toX, fromY, toY, (x, y, best) => PixelRatio(scene, pattern, tolerance, x, y, best));
    }

    /// <summary>Balayage exhaustif, une ligne par tâche.</summary>
    /// <remarks>
    /// Pas d'échantillonnage grossier suivi d'un raffinement : le pic de correspondance
    /// est étroit — souvent un seul pixel — et une grille lâche le manque, laissant la
    /// seconde passe se recentrer sur un maximum parasite.
    /// </remarks>
    private static MatchResult Scan(int fromX, int toX, int fromY, int toY, Func<int, int, double, double> score)
    {
        int rows = toY - fromY + 1;
        MatchResult[] rowBest = new MatchResult[rows];

        Parallel.For(0, rows, row =>
        {
            int y = fromY + row;
            MatchResult best = new(fromX, y, double.NegativeInfinity);

            for (int x = fromX; x <= toX; x++)
            {
                double value = score(x, y, best.Score);

                if (value > best.Score)
                {
                    best = new MatchResult(x, y, value);
                }
            }

            rowBest[row] = best;
        });

        // Réduction séquentielle : le résultat ne dépend pas de l'ordonnancement.
        MatchResult overall = rowBest[0];

        foreach (MatchResult candidate in rowBest)
        {
            if (candidate.Score > overall.Score)
            {
                overall = candidate;
            }
        }

        return overall;
    }

    /// <summary>
    /// Proportion de pixels du masque dont les trois canaux tiennent dans la tolérance.
    /// </summary>
    /// <param name="best">
    /// Meilleur score déjà obtenu sur la ligne. Dès qu'il devient hors d'atteinte,
    /// la position est abandonnée : la grande majorité des positions testées sont
    /// mauvaises, et l'abandon anticipé rend ce mode plus rapide que la corrélation.
    /// </param>
    private static double PixelRatio(
        PixelBuffer scene, ReferencePattern pattern, int tolerance, int originX, int originY, double best)
    {
        int total = pattern.MaskedCount;
        int matched = 0;
        int remaining = total;

        byte[] scenePixels = scene.Rgb;
        byte[] patternPixels = pattern.Pixels.Rgb;
        bool[] mask = pattern.Mask;

        for (int y = 0; y < pattern.Height; y++)
        {
            int sceneRow = ((originY + y) * scene.Width) + originX;
            int patternRow = y * pattern.Width;

            for (int x = 0; x < pattern.Width; x++)
            {
                if (!mask[patternRow + x])
                {
                    continue;
                }

                int s = (sceneRow + x) * 3;
                int p = (patternRow + x) * 3;

                if (Math.Abs(scenePixels[s] - patternPixels[p]) <= tolerance
                    && Math.Abs(scenePixels[s + 1] - patternPixels[p + 1]) <= tolerance
                    && Math.Abs(scenePixels[s + 2] - patternPixels[p + 2]) <= tolerance)
                {
                    matched++;
                }

                remaining--;

                // Même en réussissant tout le reste, ce point ne peut plus gagner.
                if (matched + remaining <= best * total)
                {
                    return (double)matched / total;
                }
            }
        }

        return (double)matched / total;
    }

    private static double Correlation(
        PixelBuffer scene, ReferencePattern pattern, double patternMean, double patternDeviation, int originX, int originY)
    {
        long count = pattern.MaskedCount;
        long sum = 0;
        long sumOfSquares = 0;
        long dot = 0;

        byte[] sceneGray = scene.Gray;
        byte[] patternGray = pattern.Pixels.Gray;
        bool[] mask = pattern.Mask;

        for (int y = 0; y < pattern.Height; y++)
        {
            int sceneRow = ((originY + y) * scene.Width) + originX;
            int patternRow = y * pattern.Width;

            for (int x = 0; x < pattern.Width; x++)
            {
                if (!mask[patternRow + x])
                {
                    continue;
                }

                int value = sceneGray[sceneRow + x];
                sum += value;
                sumOfSquares += (long)value * value;
                dot += (long)value * patternGray[patternRow + x];
            }
        }

        double mean = (double)sum / count;
        double variance = ((double)sumOfSquares / count) - (mean * mean);

        if (variance <= 1e-6)
        {
            return 0; // zone unie : aucune corrélation exploitable
        }

        double covariance = ((double)dot / count) - (mean * patternMean);
        return covariance / (Math.Sqrt(variance) * patternDeviation);
    }

    private static (double Mean, double Deviation) MaskedStatistics(ReferencePattern pattern)
    {
        long sum = 0;
        long sumOfSquares = 0;

        byte[] gray = pattern.Pixels.Gray;

        for (int i = 0; i < gray.Length; i++)
        {
            if (!pattern.Mask[i])
            {
                continue;
            }

            sum += gray[i];
            sumOfSquares += (long)gray[i] * gray[i];
        }

        double mean = (double)sum / pattern.MaskedCount;
        double variance = ((double)sumOfSquares / pattern.MaskedCount) - (mean * mean);

        return (mean, Math.Sqrt(Math.Max(0, variance)));
    }
}
