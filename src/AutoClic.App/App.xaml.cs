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

using AutoClic.Localization;
using Microsoft.UI.Xaml;

namespace AutoClic.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Avant toute fenêtre : les vues lisent leurs libellés dès leur construction,
        // et resteraient sinon figées sur le nom de leurs clés.
        Localizer.Current.Load(Path.Combine(AppContext.BaseDirectory, "Strings"));

        _window = new MainWindow();
        _window.Activate();
    }
}
