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

using System.Globalization;
using AutoClic.Core.Models;
using AutoClic.Core.Recording;
using AutoClic.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace AutoClic.App;

/// <summary>
/// Règle le nettoyage d'une macro et en montre l'effet chiffré avant de l'appliquer.
/// </summary>
/// <remarks>
/// L'aperçu est recalculé à chaque changement : le nettoyage étant sans retour en
/// arrière une fois appliqué, l'utilisateur doit voir ce qu'il perd avant de valider.
/// C'est bon marché — <see cref="MacroCleaner"/> ne fait que du calcul.
/// </remarks>
public sealed partial class CleanupDialog : Window
{
    private readonly Macro _macro;
    private readonly bool _ready;

    private CleanupOptions _committed = new();

    public CleanupDialog(Macro macro)
    {
        InitializeComponent();

        _macro = macro ?? throw new ArgumentNullException(nameof(macro));
        _ready = true;

        UpdatePreview();
    }

    /// <summary>Traducteur exposé aux liaisons compilées du XAML.</summary>
    public Localizer Loc => Localizer.Current;

    /// <summary>Vrai si l'utilisateur a validé plutôt qu'annulé.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Réglages figés au moment de valider.
    /// </summary>
    /// <remarks>
    /// Copie et non propriété calculée : une lecture des contrôles après la fermeture
    /// porterait sur un arbre visuel détaché.
    /// </remarks>
    public CleanupOptions Options => _committed;

    /// <summary>Affiche la fenêtre en modale et renvoie vrai si l'utilisateur a validé.</summary>
    public async Task<bool> ShowModalAsync(Window owner)
    {
        // Sans focus dans le contenu, un accélérateur clavier ne se déclenche jamais et
        // Échap resterait sans effet.
        Root.Loaded += (_, _) => CancelButton.Focus(FocusState.Programmatic);

        // Contenu entièrement fixe : la taille a été mesurée une fois, la correction
        // après mesure n'a donc normalement rien à faire.
        await ModalHost.ShowModalAsync(this, owner, Root, new Size(608, 590), maximumContentWidth: 620);
        return Confirmed;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        _committed = ReadOptions();
        Confirmed = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdatePreview();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private CleanupOptions ReadOptions()
    {
        // Rapide / Normale / Posée : millisecondes par 100 px, et plafond de durée.
        (int perHundred, int maximum) = TravelSpeedCombo.SelectedIndex switch
        {
            0 => (35, 260),
            2 => (110, 800),
            _ => (60, 450),
        };

        return new CleanupOptions
        {
            RemoveIdleMoves = RemoveMovesCheck.IsChecked == true,
            StraightLineMoves = StraightLineCheck.IsChecked == true,
            SimplifyDrags = SimplifyDragsCheck.IsChecked == true,
            KeepIdleTime = KeepIdleTimeCheck.IsChecked == true,
            TravelMsPer100Px = perHundred,
            MaxTravelMs = maximum,
            MaxDelayMs = Read(MaxDelayBox.Value, fallback: 0),
        };
    }

    private void UpdatePreview()
    {
        // Les gestionnaires se déclenchent pendant InitializeComponent, avant que les
        // champs soient prêts.
        if (!_ready)
        {
            return;
        }

        CleanupOptions options = ReadOptions();
        TravelSpeedCombo.IsEnabled = options.StraightLineMoves || options.SimplifyDrags;

        (_, CleanupReport report) = MacroCleaner.Clean(_macro, options);

        string avant = report.DurationBefore.TotalSeconds.ToString("F1", Loc.Culture);
        string apres = report.DurationAfter.TotalSeconds.ToString("F1", Loc.Culture);

        string duration =
            Math.Abs(report.DurationAfter.TotalMilliseconds - report.DurationBefore.TotalMilliseconds) < 1
                ? Loc.Format("dialog.cleanup.result.unchanged", avant)
                : Loc.Format("dialog.cleanup.result.duration", avant, apres);

        PreviewText.Text =
            Loc.Format("dialog.cleanup.result.counts", report.EventsBefore, report.EventsAfter, duration)
            + "\n"
            + Loc.Format("dialog.cleanup.result.moves", report.RemovedMoves, report.InsertedMoves);
    }

    /// <summary>NumberBox renvoie NaN quand le champ est vide.</summary>
    private static int Read(double value, int fallback) =>
        double.IsNaN(value) ? fallback : (int)value;
}
