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
using AutoClic.Core.Models;

namespace AutoClic.Core.Native;

/// <summary>Une fenêtre de premier plan actuellement ouverte.</summary>
public sealed record LiveWindow(nint Handle, WindowDescriptor Descriptor)
{
    public string Title => Descriptor.Title;

    public string ProcessName => Descriptor.ProcessName;

    public override string ToString() => Descriptor.ToString();
}

/// <summary>Énumère les fenêtres ouvertes et retrouve une cible d'une session à l'autre.</summary>
public static class WindowLocator
{
    /// <summary>Fenêtres de premier plan visibles et titrées, triées par titre.</summary>
    public static IReadOnlyList<LiveWindow> List(int? excludeProcessId = null)
    {
        List<LiveWindow> found = [];

        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                if (Describe(handle, excludeProcessId) is { } window)
                {
                    found.Add(window);
                }

                return true; // poursuivre l'énumération
            },
            0);

        return [.. found.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Fenêtre de premier plan contenant ce point écran, s'il y en a une.</summary>
    public static LiveWindow? FromPoint(int x, int y)
    {
        nint hit = NativeMethods.WindowFromPoint(new POINT { x = x, y = y });
        if (hit == 0)
        {
            return null;
        }

        nint root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
        return Describe(root == 0 ? hit : root, excludeProcessId: null);
    }

    /// <summary>
    /// Retrouve la fenêtre correspondant au descripteur, ou 0.
    /// </summary>
    /// <remarks>
    /// Le processus est éliminatoire : cliquer dans la mauvaise application est pire
    /// que ne rien faire. Classe et titre ne font que départager les candidats restants,
    /// le titre étant le moins fiable des trois — nombre d'applications y affichent un
    /// état changeant.
    /// </remarks>
    public static nint Find(WindowDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        LiveWindow? best = null;
        int bestScore = -1;

        foreach (LiveWindow candidate in List())
        {
            if (!string.Equals(candidate.Descriptor.ProcessName, descriptor.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int score = 0;

            if (string.Equals(candidate.Descriptor.ClassName, descriptor.ClassName, StringComparison.Ordinal))
            {
                score += 4;
            }

            if (string.Equals(candidate.Descriptor.Title, descriptor.Title, StringComparison.CurrentCulture))
            {
                score += 2;
            }
            else if (candidate.Descriptor.Title.Contains(descriptor.Title, StringComparison.CurrentCultureIgnoreCase)
                     || descriptor.Title.Contains(candidate.Descriptor.Title, StringComparison.CurrentCultureIgnoreCase))
            {
                score += 1;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best?.Handle ?? 0;
    }

    /// <summary>Restaure la fenêtre si elle est réduite et tente de la mettre au premier plan.</summary>
    public static void BringToFront(nint handle)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle))
        {
            return;
        }

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        // Windows peut refuser le passage au premier plan selon qui détient le focus ;
        // l'échec n'est pas bloquant, les clics restent correctement positionnés.
        NativeMethods.SetForegroundWindow(handle);
    }

    /// <summary>Taille de la zone cliente, ou null si la fenêtre n'existe plus.</summary>
    public static (int Width, int Height)? ClientSize(nint handle)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle) || !NativeMethods.GetClientRect(handle, out RECT rect))
        {
            return null;
        }

        return (rect.Width, rect.Height);
    }

    /// <summary>Convertit un point écran en coordonnées clientes de la fenêtre.</summary>
    public static (int X, int Y)? ToClient(nint handle, int screenX, int screenY)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle))
        {
            return null;
        }

        var point = new POINT { x = screenX, y = screenY };
        return NativeMethods.ScreenToClient(handle, ref point) ? (point.x, point.y) : null;
    }

    /// <summary>Convertit un point client en coordonnées écran.</summary>
    public static (int X, int Y)? ToScreen(nint handle, int clientX, int clientY)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle))
        {
            return null;
        }

        var point = new POINT { x = clientX, y = clientY };
        return NativeMethods.ClientToScreen(handle, ref point) ? (point.x, point.y) : null;
    }

    private static LiveWindow? Describe(nint handle, int? excludeProcessId)
    {
        if (handle == 0 || !NativeMethods.IsWindowVisible(handle) || IsCloaked(handle))
        {
            return null;
        }

        int length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return null;
        }

        char[] title = new char[length + 1];
        int written = NativeMethods.GetWindowText(handle, title, title.Length);
        if (written <= 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        if (excludeProcessId is { } excluded && processId == (uint)excluded)
        {
            return null;
        }

        char[] className = new char[256];
        int classLength = NativeMethods.GetClassName(handle, className, className.Length);

        return new LiveWindow(handle, new WindowDescriptor
        {
            Title = new string(title, 0, written),
            ClassName = classLength > 0 ? new string(className, 0, classLength) : string.Empty,
            ProcessName = ProcessNameOf(processId),
        });
    }

    private static bool IsCloaked(nint handle) =>
        NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
        && cloaked != 0;

    private static string ProcessNameOf(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Processus terminé entre l'énumération et la lecture, ou inaccessible.
            return string.Empty;
        }
    }
}
