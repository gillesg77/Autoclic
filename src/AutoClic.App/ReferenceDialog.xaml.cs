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
using AutoClic.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;
using DrawingPoint = System.Drawing.Point;

namespace AutoClic.App;

/// <summary>
/// Montre la région capturée agrandie et laisse l'utilisateur la détourer avant d'en
/// faire un repère.
/// </summary>
/// <remarks>
/// Fenêtre indépendante et non <c>ContentDialog</c> : l'aperçu agrandi dépasse la
/// largeur maximale d'une boîte de dialogue, qui rognerait le contenu au lieu de
/// s'agrandir.
/// </remarks>
public sealed partial class ReferenceDialog : Window
{
    private const int MaximumPreviewSide = 460;

    /// <summary>Doit correspondre au MaxHeight du ScrollViewer de l'aperçu, dans le XAML.</summary>
    private const int PreviewMaxHeight = 420;

    private readonly List<Point> _points = [];
    private readonly int _zoom;
    private readonly int _patchWidth;
    private readonly int _patchHeight;

    private ReferenceDialog(int patchWidth, int patchHeight, int zoom)
    {
        InitializeComponent();

        _patchWidth = patchWidth;
        _patchHeight = patchHeight;
        _zoom = zoom;

        OutlineCanvas.Width = patchWidth * zoom;
        OutlineCanvas.Height = patchHeight * zoom;

        UpdateHints();
    }

    /// <summary>Traducteur exposé aux liaisons compilées du XAML.</summary>
    public Localizer Loc => Localizer.Current;

    /// <summary>Vrai si l'utilisateur a validé plutôt qu'annulé.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Détourage retenu, en coordonnées de la région capturée. Null = rectangle entier.</summary>
    public IReadOnlyList<DrawingPoint>? Outline { get; private set; }

    public ReferenceMatching Matching { get; private set; } = ReferenceMatching.Pixel;

    public int Tolerance { get; private set; } = PatternMatcher.DefaultTolerance;

    /// <summary>
    /// Taille attendue de la fenêtre, appliquée avant l'activation.
    /// </summary>
    /// <remarks>
    /// Seule la hauteur de l'aperçu varie d'une capture à l'autre ; le reste du contenu
    /// est fixe, d'où une constante mesurée une fois. La correction après mesure
    /// rattrape le résidu.
    /// </remarks>
    private Size EstimatedSize => new(648, 460 + Math.Min(_patchHeight * _zoom, PreviewMaxHeight));

    public static async Task<ReferenceDialog> CreateAsync(CapturedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        int zoom = Math.Clamp(
            Math.Min(MaximumPreviewSide / Math.Max(1, region.Width), MaximumPreviewSide / Math.Max(1, region.Height)),
            1,
            8);

        var dialog = new ReferenceDialog(region.Width, region.Height, zoom);
        await dialog.LoadPreviewAsync(region.ToPreviewPng(zoom));
        return dialog;
    }

    private async Task LoadPreviewAsync(byte[] png)
    {
        using InMemoryRandomAccessStream stream = new();

        using (DataWriter writer = new(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);

        BitmapImage image = new();
        await image.SetSourceAsync(stream);
        PatchImage.Source = image;
    }

    private void OnCanvasPressed(object sender, PointerRoutedEventArgs e)
    {
        Point position = e.GetCurrentPoint(OutlineCanvas).Position;

        _points.Add(position);
        Redraw();
    }

    /// <summary>Affiche la fenêtre en modale et renvoie vrai si l'utilisateur a validé.</summary>
    public async Task<bool> ShowModalAsync(Window owner)
    {
        // Sans focus dans le contenu, un accélérateur clavier ne se déclenche jamais et
        // Échap resterait sans effet.
        Root.Loaded += (_, _) => CancelButton.Focus(FocusState.Programmatic);

        await ModalHost.ShowModalAsync(this, owner, Root, EstimatedSize, maximumContentWidth: 700);
        return Confirmed;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        // Fige les choix maintenant, pas après la fermeture : une fois la fenêtre close,
        // l'arbre visuel est détaché et la lecture des contrôles n'a plus de sens.
        Commit();
        Confirmed = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_points.Count > 0)
        {
            _points.RemoveAt(_points.Count - 1);
            Redraw();
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _points.Clear();
        Redraw();
    }

    private void OnMatchingChanged(object sender, SelectionChangedEventArgs e)
    {
        // La tolérance n'a de sens qu'en comparaison au pixel ; la corrélation compare
        // des formes, pas des valeurs absolues.
        if (ToleranceBox is not null)
        {
            ToleranceBox.IsEnabled = MatchingCombo.SelectedIndex == 0;
        }

        UpdateHints();
    }

    private void Redraw()
    {
        OutlineShape.Points.Clear();

        foreach (Point point in _points)
        {
            OutlineShape.Points.Add(point);
        }

        UndoButton.IsEnabled = _points.Count > 0;
        ClearButton.IsEnabled = _points.Count > 0;
        UpdateHints();
    }

    private void UpdateHints()
    {
        if (OutlineHint is null || ModeHint is null)
        {
            return;
        }

        OutlineHint.Text = _points.Count switch
        {
            0 => Loc.Format("dialog.reference.whole", _patchWidth, _patchHeight, _zoom),
            1 or 2 => Loc.Format("dialog.reference.partial", _points.Count),
            _ => Loc.Format("dialog.reference.outline", _points.Count),
        };

        ModeHint.Text = Loc[MatchingCombo?.SelectedIndex == 1
            ? "dialog.reference.hint.correlation"
            : "dialog.reference.hint.pixel"];
    }

    /// <summary>Fige les choix de l'utilisateur au moment de valider.</summary>
    private void Commit()
    {
        Matching = MatchingCombo.SelectedIndex == 1 ? ReferenceMatching.Correlation : ReferenceMatching.Pixel;
        Tolerance = double.IsNaN(ToleranceBox.Value) ? PatternMatcher.DefaultTolerance : (int)ToleranceBox.Value;

        // Les points sont saisis dans l'aperçu agrandi : les ramener à l'échelle des
        // pixels réels, seule échelle où la comparaison a lieu.
        Outline = _points.Count < 3
            ? null
            : [.. _points.Select(p => new DrawingPoint(
                Math.Clamp((int)Math.Round(p.X / _zoom), 0, _patchWidth - 1),
                Math.Clamp((int)Math.Round(p.Y / _zoom), 0, _patchHeight - 1)))];
    }
}
