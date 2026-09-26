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

public sealed class MouseHookEventArgs : EventArgs
{
    /// <summary>Message WM_* brut (WM_MOUSEMOVE, WM_LBUTTONDOWN, ...).</summary>
    public required uint Message { get; init; }

    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Mot haut = delta de molette signé, ou numéro du bouton X.</summary>
    public required uint MouseData { get; init; }

    public required bool IsInjected { get; init; }

    public bool Handled { get; set; }

    public short WheelDelta => (short)(MouseData >> 16);

    public ushort XButton => (ushort)(MouseData >> 16);
}

/// <summary>
/// Hook souris bas niveau (WH_MOUSE_LL). Mêmes contraintes de thread que
/// <see cref="LowLevelKeyboardHook"/>.
/// </summary>
public sealed class LowLevelMouseHook : IDisposable
{
    private readonly NativeMethods.HookProc _proc;
    private nint _handle;

    public LowLevelMouseHook() => _proc = HookCallback;

    public event EventHandler<MouseHookEventArgs>? MouseAction;

    public bool IsInstalled => _handle != 0;

    public void Install()
    {
        if (_handle != 0)
        {
            return;
        }

        _handle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandleW(0), 0);

        if (_handle == 0)
        {
            throw new AutoClicException(
                ErrorCode.HookInstallFailed,
                "SetWindowsHookEx failed for the low-level mouse hook.",
                new Dictionary<string, object?> { ["hook"] = "mouse" },
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

    private static unsafe MSLLHOOKSTRUCT Read(nint lParam) => *(MSLLHOOKSTRUCT*)lParam;

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
        }

        MSLLHOOKSTRUCT data = Read(lParam);

        var args = new MouseHookEventArgs
        {
            Message = (uint)wParam,
            X = data.pt.x,
            Y = data.pt.y,
            MouseData = data.mouseData,
            IsInjected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0,
        };

        MouseAction?.Invoke(this, args);

        return args.Handled ? 1 : NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
