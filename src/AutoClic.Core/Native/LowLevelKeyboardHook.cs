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

using System.ComponentModel;
using AutoClic.Core.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoClic.Core.Native;

public sealed class KeyboardHookEventArgs : EventArgs
{
    public required ushort VirtualKey { get; init; }
    public required ushort ScanCode { get; init; }
    public required bool IsKeyUp { get; init; }
    public required bool IsExtended { get; init; }

    /// <summary>Vrai si l'événement provient de SendInput et non d'une frappe physique.</summary>
    public required bool IsInjected { get; init; }

    /// <summary>Passer à <c>true</c> pour que la touche n'atteigne pas l'application au premier plan.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Hook clavier bas niveau (WH_KEYBOARD_LL) : voit toutes les frappes du système,
/// y compris hors de notre fenêtre.
/// </summary>
/// <remarks>
/// <see cref="Install"/> doit être appelé depuis un thread qui possède une boucle de
/// messages — le thread UI dans une application WinUI. Windows dépose les rappels dans
/// la file de ce thread ; sans pompe de messages, aucun événement n'arrive.
/// </remarks>
public sealed class LowLevelKeyboardHook : IDisposable
{
    // Le délégué doit rester référencé pendant toute la durée du hook : Windows n'en
    // garde qu'un pointeur natif, invisible du GC. Sans ce champ, l'application
    // planterait au premier appui de touche après une collecte.
    private readonly NativeMethods.HookProc _proc;
    private nint _handle;

    public LowLevelKeyboardHook() => _proc = HookCallback;

    public event EventHandler<KeyboardHookEventArgs>? KeyAction;

    public bool IsInstalled => _handle != 0;

    public void Install()
    {
        if (_handle != 0)
        {
            return;
        }

        _handle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandleW(0), 0);

        if (_handle == 0)
        {
            throw new AutoClicException(
                ErrorCode.HookInstallFailed,
                "SetWindowsHookEx failed for the low-level keyboard hook.",
                new Dictionary<string, object?> { ["hook"] = "keyboard" },
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public void Uninstall()
    {
        if (_handle == 0)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_handle);
        _handle = 0;
    }

    private static unsafe KBDLLHOOKSTRUCT Read(nint lParam) => *(KBDLLHOOKSTRUCT*)lParam;

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
        }

        KBDLLHOOKSTRUCT data = Read(lParam);
        uint message = (uint)wParam;

        var args = new KeyboardHookEventArgs
        {
            VirtualKey = (ushort)data.vkCode,
            ScanCode = (ushort)data.scanCode,
            IsKeyUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP,
            IsExtended = (data.flags & NativeMethods.LLKHF_EXTENDED) != 0,
            IsInjected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0,
        };

        KeyAction?.Invoke(this, args);

        return args.Handled ? 1 : NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
