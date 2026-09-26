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
using System.Runtime.InteropServices;
using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Native;

namespace AutoClic.Core.Playback;

/// <summary>Réinjecte réellement les entrées dans le système, via SendInput.</summary>
public sealed class SendInputSink : IInputSink
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public void MoveTo(int screenX, int screenY)
    {
        (int nx, int ny) = ToAbsolute(screenX, screenY);

        var input = new INPUT { type = (uint)InputType.Mouse };
        input.U.mi = new MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            dwFlags = (uint)(MouseEventFlags.Move | MouseEventFlags.Absolute | MouseEventFlags.VirtualDesk),
            dwExtraInfo = ExtraInfo(),
        };

        Dispatch(input);
    }

    public void Button(MouseButtonEvent button, int screenX, int screenY)
    {
        MouseEventFlags action = (button.Button, button.IsDown) switch
        {
            (MouseButton.Left, true) => MouseEventFlags.LeftDown,
            (MouseButton.Left, false) => MouseEventFlags.LeftUp,
            (MouseButton.Right, true) => MouseEventFlags.RightDown,
            (MouseButton.Right, false) => MouseEventFlags.RightUp,
            (MouseButton.Middle, true) => MouseEventFlags.MiddleDown,
            (MouseButton.Middle, false) => MouseEventFlags.MiddleUp,
            (_, true) => MouseEventFlags.XDown,
            (_, false) => MouseEventFlags.XUp,
        };

        uint mouseData = button.Button switch
        {
            MouseButton.X1 => NativeMethods.XBUTTON1,
            MouseButton.X2 => NativeMethods.XBUTTON2,
            _ => 0u,
        };

        // Le clic porte sa propre position : la macro reste correcte même quand les
        // déplacements n'ont pas été enregistrés.
        (int nx, int ny) = ToAbsolute(screenX, screenY);

        var input = new INPUT { type = (uint)InputType.Mouse };
        input.U.mi = new MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            mouseData = mouseData,
            dwFlags = (uint)(action | MouseEventFlags.Move | MouseEventFlags.Absolute | MouseEventFlags.VirtualDesk),
            dwExtraInfo = ExtraInfo(),
        };

        Dispatch(input);
    }

    public void Wheel(MouseWheelEvent wheel, int screenX, int screenY)
    {
        (int nx, int ny) = ToAbsolute(screenX, screenY);

        var input = new INPUT { type = (uint)InputType.Mouse };
        input.U.mi = new MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            mouseData = unchecked((uint)wheel.Delta),
            dwFlags = (uint)((wheel.IsHorizontal ? MouseEventFlags.HWheel : MouseEventFlags.Wheel)
                | MouseEventFlags.Move | MouseEventFlags.Absolute | MouseEventFlags.VirtualDesk),
            dwExtraInfo = ExtraInfo(),
        };

        Dispatch(input);
    }

    public void Key(KeyboardEvent key)
    {
        KeyEventFlags flags = KeyEventFlags.None;

        if (key.IsKeyUp)
        {
            flags |= KeyEventFlags.KeyUp;
        }

        if (key.IsExtended)
        {
            flags |= KeyEventFlags.ExtendedKey;
        }

        var input = new INPUT { type = (uint)InputType.Keyboard };
        input.U.ki = new KEYBDINPUT
        {
            wVk = key.VirtualKey,
            wScan = key.ScanCode,
            dwFlags = (uint)flags,
            dwExtraInfo = ExtraInfo(),
        };

        Dispatch(input);
    }

    /// <summary>
    /// Convertit des pixels écran en coordonnées absolues 0..65535 rapportées au
    /// bureau virtuel, seul repère correct en multi-écrans.
    /// </summary>
    private static (int X, int Y) ToAbsolute(int x, int y)
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN) - 1);
        int height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN) - 1);

        return ((int)Math.Round((x - left) * 65535.0 / width),
                (int)Math.Round((y - top) * 65535.0 / height));
    }

    private static nuint ExtraInfo() => unchecked((nuint)NativeMethods.GetMessageExtraInfo());

    private static void Dispatch(INPUT input)
    {
        INPUT[] batch = [input];
        uint sent = NativeMethods.SendInput(1, batch, InputSize);

        if (sent != 1)
        {
            throw new AutoClicException(
                ErrorCode.InputBlocked,
                "SendInput was blocked; the target is probably elevated.",
                inner: new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
}
