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

using System.Diagnostics;

namespace AutoClic.Core.Playback;

/// <summary>
/// Attente courte plus fidèle que <see cref="Task.Delay(TimeSpan)"/> seul.
/// </summary>
/// <remarks>
/// Le quantum du planificateur Windows est d'environ 15 ms : un Task.Delay(3) dort
/// souvent 15 ms. Sur une macro de plusieurs centaines d'événements, l'écart cumulé
/// se compte en secondes. On délègue donc le gros de l'attente au planificateur puis
/// on termine à la milliseconde par une attente active — coûteuse en CPU, d'où la
/// marge volontairement étroite.
/// </remarks>
internal static class PrecisionDelay
{
    private const double SpinThresholdMs = 8;

    public static async Task WaitAsync(double milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();

        double coarse = milliseconds - SpinThresholdMs;
        if (coarse > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(coarse), ct).ConfigureAwait(false);
        }

        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < milliseconds)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(50);
        }
    }
}
