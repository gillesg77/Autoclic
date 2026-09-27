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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoClic.App;

/// <summary>
/// Un bouton à menu tenant lieu de liste déroulante.
/// </summary>
/// <remarks>
/// Une ComboBox qui a le focus change de sélection au premier coup de molette : on
/// croit faire défiler le panneau, et la fenêtre cible change sans rien dire. La
/// macro part alors cliquer ailleurs. Un bouton n'a aucun comportement de molette,
/// donc le défaut disparaît par construction plutôt que par contournement.
/// </remarks>
/// <typeparam name="T">Ce que vaut une entrée, indépendamment de son libellé.</typeparam>
internal sealed class MenuSelector<T>
{
    private readonly DropDownButton _button;
    private readonly MenuFlyout _flyout;
    private readonly string _groupe;
    private readonly List<T> _valeurs = [];

    public MenuSelector(DropDownButton button, MenuFlyout flyout, string groupe)
    {
        _button = button;
        _flyout = flyout;
        _groupe = groupe;
    }

    /// <summary>Levé quand l'utilisateur choisit une entrée, jamais quand le code en impose une.</summary>
    public event EventHandler? SelectionChanged;

    public int SelectedIndex { get; private set; } = -1;

    public T? SelectedValue =>
        SelectedIndex >= 0 && SelectedIndex < _valeurs.Count ? _valeurs[SelectedIndex] : default;

    public bool IsEnabled
    {
        get => _button.IsEnabled;
        set => _button.IsEnabled = value;
    }

    /// <summary>
    /// Remplace les entrées et rétablit la sélection demandée.
    /// </summary>
    /// <remarks>
    /// Reconstruire plutôt que mettre à jour : les libellés changent de langue, et la
    /// liste des fenêtres ouvertes change à chaque rafraîchissement.
    /// </remarks>
    public void SetItems(IReadOnlyList<(string Label, T Value)> entrees, int selection)
    {
        _valeurs.Clear();
        _flyout.Items.Clear();

        for (int i = 0; i < entrees.Count; i++)
        {
            (string libelle, T valeur) = entrees[i];
            _valeurs.Add(valeur);

            RadioMenuFlyoutItem item = new()
            {
                Text = libelle,
                GroupName = _groupe,
                Tag = i,
            };

            item.Click += OnItemClick;
            _flyout.Items.Add(item);
        }

        Select(entrees.Count == 0 ? -1 : Math.Clamp(selection, 0, entrees.Count - 1), notify: false);
    }

    /// <summary>Impose une sélection sans prévenir personne.</summary>
    public void Select(int index) => Select(index, notify: false);

    private void Select(int index, bool notify)
    {
        SelectedIndex = index;

        for (int i = 0; i < _flyout.Items.Count; i++)
        {
            if (_flyout.Items[i] is RadioMenuFlyoutItem item)
            {
                item.IsChecked = i == index;
            }
        }

        _button.Content = index >= 0 && _flyout.Items[index] is RadioMenuFlyoutItem coche
            ? coche.Text
            : string.Empty;

        if (notify)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: int index })
        {
            Select(index, notify: true);
        }
    }
}
