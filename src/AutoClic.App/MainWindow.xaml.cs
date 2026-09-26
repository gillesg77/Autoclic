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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Native;
using AutoClic.Core.Playback;
using AutoClic.Core.Recording;
using AutoClic.Core.Storage;
using AutoClic.Core.Vision;
using AutoClic.Localization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace AutoClic.App;

public sealed partial class MainWindow : Window
{
    private const ushort VkEscape = 0x1B;

    private readonly MacroRecorder _recorder = new();

    // Hook actif uniquement pendant le rejeu : sans issue de secours, une macro
    // répétée qui déplace la souris rend le poste inutilisable.
    private readonly LowLevelKeyboardHook _abortWatcher = new();

    private readonly DispatcherQueue _dispatcher;

    private readonly ReferencePicker _picker = new();

    private Macro? _macro;
    private VisualReference? _reference;
    private CancellationTokenSource? _playbackCts;
    private bool _simulating;

    // La fenêtre mère est désactivée pendant une modale, mais son thread continue de
    // pomper : les événements de l'enregistreur peuvent encore muter l'état. Le drapeau
    // évite de faire reposer la protection sur la seule désactivation.
    private bool _modalOpen;

    public MainWindow()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue;
        AppWindow.Resize(new SizeInt32(1180, 780));

        // Fond Mica et barre de titre intégrée : l'apparence attendue d'une application
        // Windows 11. La barre personnalisée ne contient que du texte, donc toute sa
        // surface reste saisissable pour déplacer la fenêtre.
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyWindowIcon();

        _recorder.StopRequested += (_, _) => _dispatcher.TryEnqueue(StopRecording);
        _recorder.EventRecorded += (_, _) => _dispatcher.TryEnqueue(ShowRecordingProgress);
        _abortWatcher.KeyAction += OnAbortKey;
        _picker.Completed += OnReferencePicked;

        Closed += OnClosed;

        Localizer.Current.LanguageChanged += (_, _) => ApplyLanguage();

        RefreshTargets();
        UpdateEventList();
        ApplyLanguage();
    }

    /// <summary>
    /// Donne à la fenêtre l'icône de l'exécutable.
    /// </summary>
    /// <remarks>
    /// Les deux icônes viennent de sources distinctes : <c>ApplicationIcon</c> inscrit
    /// celle du fichier dans les ressources de l'exécutable, tandis que celle de la
    /// fenêtre — barre des tâches, Alt+Tab, gestionnaire des tâches — doit être posée à
    /// l'exécution, faute de quoi Windows affiche une icône générique. On l'extrait de
    /// l'exécutable lui-même plutôt que d'un fichier séparé : les deux ne peuvent alors
    /// pas diverger.
    /// </remarks>
    private void ApplyWindowIcon()
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        if (ExtractIconEx(executable, 0, out nint large, out nint small, 1) == 0)
        {
            return;
        }

        nint chosen = large != 0 ? large : small;
        nint unused = large != 0 ? small : 0;

        if (unused != 0)
        {
            DestroyIcon(unused);
        }

        if (chosen != 0)
        {
            // Le handle reste vivant tant que la fenêtre l'affiche : ne pas le détruire.
            AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(chosen));
        }
    }

    // --- Menu système -----------------------------------------------------

    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_NULL = 0x0000;
    private const uint SC_CLOSE = 0xF060;

    private const uint SC_SIZE = 0xF000;
    private const uint SC_MOVE = 0xF010;
    private const uint SC_MINIMIZE = 0xF020;
    private const uint SC_MAXIMIZE = 0xF030;
    private const uint SC_RESTORE = 0xF120;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint MF_BYCOMMAND = 0x0000;
    private const uint MF_ENABLED = 0x0000;
    private const uint MF_GRAYED = 0x0001;

    private void OnAppIconClick(object sender, RoutedEventArgs e) => ShowSystemMenu(BelowIcon());

    private void OnAppIconRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ShowSystemMenu(BelowIcon());
    }

    /// <summary>
    /// Ouvre le menu système de la fenêtre à cette position écran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Étendre le contenu dans la barre de titre retire l'icône que Windows y dessinait,
    /// et avec elle son menu. Il faut donc le rouvrir soi-même.
    /// </para>
    /// <para>
    /// Refusé pendant un enregistrement ou un rejeu : <c>TrackPopupMenu</c> ouvre sa
    /// propre boucle de messages et bloque le thread UI, or c'est ce thread qui distribue
    /// les rappels de F9 et d'Échap. Menu ouvert, les coupe-circuits ne répondraient plus.
    /// </para>
    /// </remarks>
    private void ShowSystemMenu((int X, int Y) position)
    {
        if (_recorder.IsRecording || _playbackCts is not null || _picker.IsPicking)
        {
            return;
        }

        nint handle = WindowNative.GetWindowHandle(this);
        nint menu = GetSystemMenu(handle, bRevert: false);

        if (menu == 0)
        {
            return;
        }

        PrepareMenuItems(menu);

        // Sans premier plan, le menu ne se referme pas quand on clique ailleurs.
        // Contournement documenté de longue date par Microsoft ; en cas d'échec on
        // renonce, plutôt que d'afficher un menu qui resterait collé à l'écran.
        if (!SetForegroundWindow(handle))
        {
            return;
        }

        // TPM_RIGHTBUTTON ajoute le suivi du bouton droit sans retirer le gauche : sans
        // lui, maintenir le bouton droit et relâcher sur une entrée ne déclenche rien.
        int command = TrackPopupMenu(
            menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN, position.X, position.Y, 0, handle, 0);

        // Débloque la file de messages du menu, sans quoi la deuxième ouverture
        // s'affiche puis disparaît aussitôt.
        PostMessageW(handle, WM_NULL, 0, 0);

        if (command != 0)
        {
            PostMessageW(handle, WM_SYSCOMMAND, command, 0);
        }
    }

    /// <summary>
    /// Accorde les entrées du menu à l'état réel de la fenêtre.
    /// </summary>
    /// <remarks>
    /// Mesuré sur cette version : les états ne se mettent pas à jour tout seuls — fenêtre
    /// agrandie, « Agrandir » reste actif — et le <c>WM_INITMENUPOPUP</c> que l'on peut
    /// envoyer avant l'affichage n'y change rien. Il faut donc les poser explicitement.
    /// </remarks>
    private void PrepareMenuItems(nint menu)
    {
        OverlappedPresenter? presenter = AppWindow.Presenter as OverlappedPresenter;
        OverlappedPresenterState state = presenter?.State ?? OverlappedPresenterState.Restored;

        bool maximized = state == OverlappedPresenterState.Maximized;
        bool minimized = state == OverlappedPresenterState.Minimized;
        bool restored = !maximized && !minimized;

        Enable(menu, SC_RESTORE, !restored);
        Enable(menu, SC_MOVE, !maximized);
        Enable(menu, SC_SIZE, restored && (presenter?.IsResizable ?? true));
        Enable(menu, SC_MINIMIZE, !minimized && (presenter?.IsMinimizable ?? true));
        Enable(menu, SC_MAXIMIZE, !maximized && (presenter?.IsMaximizable ?? true));
        Enable(menu, SC_CLOSE, true);

        // Fermer en gras, comme dans toute fenêtre Windows.
        SetMenuDefaultItem(menu, SC_CLOSE, fByPos: false);
    }

    private static void Enable(nint menu, uint command, bool enabled) =>
        EnableMenuItem(menu, command, MF_BYCOMMAND | (enabled ? MF_ENABLED : MF_GRAYED));

    /// <summary>Coin inférieur gauche de l'icône, en pixels écran.</summary>
    private (int X, int Y) BelowIcon() =>
        ToScreen(AppIconButton.TransformToVisual(Content)
            .TransformPoint(new Windows.Foundation.Point(0, AppIconButton.ActualHeight)));

    private (int X, int Y) AtPointer(RightTappedRoutedEventArgs e) =>
        ToScreen(e.GetPosition(Content));

    /// <summary>
    /// Convertit un point logique du contenu en pixels écran.
    /// </summary>
    /// <remarks>
    /// XAML raisonne en unités logiques, TrackPopupMenu en pixels physiques : sans la
    /// mise à l'échelle, le menu s'ouvrirait ailleurs dès que l'affichage dépasse 100 %.
    /// L'échelle vient de <c>GetDpiForWindow</c>, comme dans <see cref="ModalHost"/>, et
    /// non de <c>RasterizationScale</c> : ce dernier peut être indisponible et retomber
    /// silencieusement sur 1,0, ce qui décalerait le menu sans rien signaler.
    /// </remarks>
    private (int X, int Y) ToScreen(Windows.Foundation.Point local)
    {
        nint handle = WindowNative.GetWindowHandle(this);

        uint dpi = GetDpiForWindow(handle);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;

        var origin = new POINT { x = 0, y = 0 };
        ClientToScreen(handle, ref origin);

        return (origin.x + (int)Math.Round(local.X * scale),
                origin.y + (int)Math.Round(local.Y * scale));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableMenuItem(nint hMenu, uint uIDEnableItem, uint uEnable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(nint hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPos);

    /// <summary>Traducteur exposé aux liaisons compilées du XAML.</summary>
    public Localizer Loc => Localizer.Current;

    public ObservableCollection<EventRow> Rows { get; } = [];

    public ObservableCollection<WindowChoice> Targets { get; } = [];

    // --- Fenêtre cible ----------------------------------------------------

    private void OnRefreshTargetsClick(object sender, RoutedEventArgs e) => RefreshTargets();

    private void OnTargetChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void RefreshTargets()
    {
        WindowDescriptor? previous = SelectedTarget();

        Targets.Clear();
        Targets.Add(new WindowChoice(null));

        // Notre propre fenêtre n'a rien à faire dans la liste : une macro qui se
        // viserait elle-même n'a pas de sens.
        foreach (LiveWindow window in WindowLocator.List(Environment.ProcessId))
        {
            Targets.Add(new WindowChoice(window));
        }

        SelectTarget(previous);
    }

    private WindowDescriptor? SelectedTarget() =>
        (TargetCombo.SelectedItem as WindowChoice)?.Window?.Descriptor;

    private void SelectTarget(WindowDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            TargetCombo.SelectedIndex = 0;
            return;
        }

        WindowChoice? match = Targets.FirstOrDefault(c =>
            c.Window is not null
            && string.Equals(c.Window.ProcessName, descriptor.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Window.Title, descriptor.Title, StringComparison.CurrentCulture));

        match ??= Targets.FirstOrDefault(c =>
            c.Window is not null
            && string.Equals(c.Window.ProcessName, descriptor.ProcessName, StringComparison.OrdinalIgnoreCase));

        TargetCombo.SelectedIndex = match is null ? 0 : Targets.IndexOf(match);
    }

    // --- Repère visuel ----------------------------------------------------

    private void OnPickReferenceClick(object sender, RoutedEventArgs e)
    {
        HideError();

        if (SelectedTarget() is not { } descriptor)
        {
            ShowError(Loc["reference.targetFirst"]);
            return;
        }

        nint handle = WindowLocator.Find(descriptor);
        if (handle == 0)
        {
            ShowError(Loc.Format("status.targetMissing", descriptor));
            return;
        }

        try
        {
            _picker.Start(handle, new PickerLabels(
                Loc["reference.draw"],
                Loc["picker.size"],
                Loc["picker.tooSmall"])
            {
                Culture = Loc.Culture,
            });
        }
        catch (Exception ex)
        {
            ShowError(Explain(ex));
            return;
        }

        WindowLocator.BringToFront(handle);
        UpdateButtons();
        StatusText.Text = Loc["reference.draw"];
    }

    private void OnReferencePicked(object? sender, PickedRegion? region) =>
        _dispatcher.TryEnqueue(async () => await ApplyPickedRegionAsync(region));

    private async Task ApplyPickedRegionAsync(PickedRegion? region)
    {
        // Impérativement avant toute capture : si PrintWindow échoue, on retombe sur
        // une copie d'écran, qui photographierait le calque de sélection.
        _picker.HideOverlay();

        UpdateButtons();

        if (region is not { } picked)
        {
            StatusText.Text = Loc["reference.cancelled"];
            return;
        }

        if (SelectedTarget() is not { } descriptor || WindowLocator.Find(descriptor) is var handle && handle == 0)
        {
            ShowError(Loc["reference.targetGone"]);
            return;
        }

        using CapturedRegion? captured = ReferenceResolver.CaptureRegion(
            handle, picked.ClientX, picked.ClientY, picked.Width, picked.Height);

        if (captured is null)
        {
            ShowError(Loc["error.windowCaptureFailed"]);
            return;
        }

        ReferenceDialog dialog = await ReferenceDialog.CreateAsync(captured);
        bool confirmed;

        _modalOpen = true;
        UpdateButtons();

        try
        {
            confirmed = await dialog.ShowModalAsync(this);
        }
        finally
        {
            _modalOpen = false;
            UpdateButtons();
        }

        if (!confirmed)
        {
            StatusText.Text = Loc["reference.abandoned"];
            return;
        }

        // Le repère est bâti sur les pixels déjà montrés, pas sur une nouvelle capture :
        // entre-temps l'application aurait pu se redessiner.
        VisualReference reference = ReferenceResolver.Build(
            captured, Loc["reference.title"], dialog.Outline, dialog.Matching, dialog.Tolerance);

        _reference = reference;

        if (_macro is not null)
        {
            _macro.Reference = reference;
        }

        await ShowReferenceAsync(reference);
        UpdateButtons();

        StatusText.Text = Loc.Format(
            "reference.defined",
            reference.Width,
            reference.Height,
            reference.IsOutlined ? Loc["reference.defined.outlined"] : string.Empty);
    }

    private void OnClearReferenceClick(object sender, RoutedEventArgs e)
    {
        _reference = null;

        if (_macro is not null)
        {
            _macro.Reference = null;
        }

        ReferenceBorder.Visibility = Visibility.Collapsed;
        ReferenceImage.Source = null;
        ReferenceText.Text = Loc["reference.none"];
        UpdateButtons();
    }

    private async Task ShowReferenceAsync(VisualReference reference)
    {
        using InMemoryRandomAccessStream stream = new();

        using (DataWriter writer = new(stream))
        {
            writer.WriteBytes(Convert.FromBase64String(reference.ImagePng));
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);

        BitmapImage image = new();
        await image.SetSourceAsync(stream);

        ReferenceImage.Source = image;
        ReferenceBorder.Visibility = Visibility.Visible;

        string comparison = reference.Matching == ReferenceMatching.Pixel
            ? Loc.Format("reference.matching.pixel", reference.PixelTolerance)
            : Loc["reference.matching.correlation"];

        ReferenceText.Text = Loc.Format(
            "reference.active",
            Loc[reference.IsOutlined ? "reference.outlined" : "reference.rectangular"],
            reference.Width,
            reference.Height,
            comparison,
            reference.MinimumScore.ToString("P0", Loc.Culture),
            reference.SearchRadius);
    }

    private AnchorMode SelectedAnchorMode() => AnchorCombo.SelectedIndex switch
    {
        1 => AnchorMode.TopLeft,
        2 => AnchorMode.Proportional,
        _ => AnchorMode.Auto,
    };

    // --- Enregistrement ---------------------------------------------------

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        HideError();

        _recorder.RecordMouseMoves = MouseMoveToggle.IsOn;
        _recorder.Target = SelectedTarget();

        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            ShowError(Explain(ex));
            return;
        }

        if (_recorder.Target is not null && !_recorder.TargetResolved)
        {
            ShowError(Loc.Format("status.targetMissing", _recorder.Target));
        }

        Rows.Clear();
        UpdateEventList();
        _macro = null;
        UpdateButtons();

        StatusText.Text = _recorder.TargetResolved
            ? Loc.Format("status.recording.window", _recorder.Target)
            : Loc["status.recording.screen"];
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            StopRecording();
        }
        else
        {
            _playbackCts?.Cancel();
        }
    }

    private void StopRecording()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        _macro = _recorder.Stop($"Macro du {DateTime.Now:g}");
        _macro.Reference = _reference;
        Populate(_macro);
        UpdateButtons();

        if (_macro.Events.Count == 0)
        {
            StatusText.Text = Loc["status.recorded.empty"];
            return;
        }

        int anchored = _macro.Events.OfType<MouseEvent>().Count(e => e.Anchor is not null);

        StatusText.Text = Loc.Format(
                "status.recorded",
                _macro.Events.Count,
                _macro.Duration.TotalSeconds.ToString("F1", Loc.Culture))
            + (_macro.Target is null ? "." : Loc.Format("status.recorded.anchored", anchored));
    }

    private void ShowRecordingProgress() =>
        StatusText.Text = Loc.Format("status.recording.progress", _recorder.EventCount);

    // --- Rejeu ------------------------------------------------------------

    private async void OnPlayClick(object sender, RoutedEventArgs e) => await RunAsync(simulate: false);

    private async void OnSimulateClick(object sender, RoutedEventArgs e) => await RunAsync(simulate: true);

    /// <summary>
    /// Déroule la macro, réellement ou à blanc.
    /// </summary>
    /// <remarks>
    /// Les deux chemins passent par le même <see cref="MacroPlayer"/> ; seule la
    /// destination des entrées change. La simulation montre donc exactement les
    /// positions que le rejeu viserait, repère et ancrage compris.
    /// </remarks>
    private async Task RunAsync(bool simulate)
    {
        if (_macro is not { Events.Count: > 0 } macro)
        {
            return;
        }

        HideError();

        string action = Loc[simulate ? "status.simulate" : "status.play"];

        using SimulationSink? simulation = simulate ? new SimulationSink() : null;
        MacroPlayer player = simulation is null ? new MacroPlayer() : new MacroPlayer(simulation);

        player.RepeatCount = (int)ReadNumber(RepeatBox.Value, fallback: 1);
        player.SpeedFactor = ReadNumber(SpeedBox.Value, fallback: 1);

        // Le même commutateur vaut à l'enregistrement et au rejeu : le basculer après
        // coup doit produire l'effet attendu sur une macro déjà capturée.
        player.ReplayMouseMoves = MouseMoveToggle.IsOn;

        // Lu au lancement et non à l'enregistrement : la règle se change sans réenregistrer.
        macro.AnchorMode = SelectedAnchorMode();

        _playbackCts = new CancellationTokenSource();
        _simulating = simulate;
        player.Progress += OnProgress;

        try
        {
            _abortWatcher.Install();
        }
        catch (Exception ex)
        {
            // Sans coupe-circuit, on refuse de lancer quoi que ce soit.
            player.Progress -= OnProgress;
            _playbackCts.Dispose();
            _playbackCts = null;
            ShowError(Loc.Format("status.cancelled", action, Explain(ex)));
            return;
        }

        UpdateButtons();
        StatusText.Text = Loc.Format("status.running", action);

        try
        {
            CancellationToken ct = _playbackCts.Token;

            // Le rejeu attend à la milliseconde près (attente active en fin de délai) :
            // le garder hors du thread UI, qui doit rester libre pour le hook Échap.
            await Task.Run(() => player.PlayAsync(macro, ct), ct);

            StatusText.Text = Loc.Format("status.finished", action) + ReferenceSummary(player);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.Format("status.interrupted", action);
        }
        catch (Exception ex)
        {
            ShowError(Explain(ex));
        }
        finally
        {
            player.Progress -= OnProgress;
            _abortWatcher.Uninstall();
            _playbackCts.Dispose();
            _playbackCts = null;
            _simulating = false;
            UpdateButtons();
        }
    }

    private string ReferenceSummary(MacroPlayer player) =>
        player.LastReferenceScore is { } score && _macro?.Reference is { } reference
            ? reference.Matching == ReferenceMatching.Pixel
                ? " " + Loc.Format("status.reference.pixel", score.ToString("P1", Loc.Culture))
                : " " + Loc.Format("status.reference.correlation", score.ToString("P1", Loc.Culture))
            : string.Empty;

    private void OnProgress(object? sender, MacroProgress p) =>
        _dispatcher.TryEnqueue(() =>
            StatusText.Text =
                Loc.Format(
                    "status.progress",
                    Loc[_simulating ? "status.simulate" : "status.play"],
                    p.Repeat, p.TotalRepeats, p.EventIndex, p.TotalEvents));

    private void OnAbortKey(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsInjected || e.VirtualKey != VkEscape)
        {
            return;
        }

        e.Handled = true;

        // Cancel() exécute des continuations : on sort du rappel de hook, qui doit
        // rendre la main avant LowLevelHooksTimeout.
        _dispatcher.TryEnqueue(() => _playbackCts?.Cancel());
    }

    // --- Nettoyage --------------------------------------------------------

    private async void OnCleanClick(object sender, RoutedEventArgs e)
    {
        if (_macro is not { Events.Count: > 0 } macro)
        {
            return;
        }

        HideError();

        CleanupDialog dialog = new(macro);
        bool confirmed;

        _modalOpen = true;
        UpdateButtons();

        try
        {
            confirmed = await dialog.ShowModalAsync(this);
        }
        finally
        {
            _modalOpen = false;
            UpdateButtons();
        }

        if (!confirmed)
        {
            return;
        }

        // La macro a pu être remplacée pendant que la boîte était ouverte.
        if (!ReferenceEquals(_macro, macro))
        {
            StatusText.Text = Loc["status.macroChanged"];
            return;
        }

        (Macro cleaned, CleanupReport report) = MacroCleaner.Clean(macro, dialog.Options);

        _macro = cleaned;
        Populate(_macro);
        UpdateButtons();

        StatusText.Text = Loc.Format(
            "status.cleaned",
            report.EventsBefore,
            report.EventsAfter,
            report.DurationBefore.TotalSeconds.ToString("F1", Loc.Culture),
            report.DurationAfter.TotalSeconds.ToString("F1", Loc.Culture));
    }

    // --- Fichiers ---------------------------------------------------------

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        HideError();

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(MacroStorage.FileExtension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            _macro = await MacroStorage.LoadAsync(file.Path);
            Populate(_macro);

            RefreshTargets();
            SelectTarget(_macro.Target);
            AnchorCombo.SelectedIndex = _macro.AnchorMode switch
            {
                AnchorMode.TopLeft => 1,
                AnchorMode.Proportional => 2,
                _ => 0,
            };

            _reference = _macro.Reference;

            if (_reference is null)
            {
                OnClearReferenceClick(this, e);
            }
            else
            {
                await ShowReferenceAsync(_reference);
            }

            UpdateButtons();

            StatusText.Text = Loc.Format("status.loaded", _macro.Name, _macro.Events.Count)
                + (_macro.Target is null ? "." : Loc.Format("status.loaded.target", _macro.Target));
        }
        catch (Exception ex)
        {
            ShowError(Loc.Format("status.readFailed", Explain(ex)));
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_macro is null)
        {
            return;
        }

        HideError();

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "macro",
        };
        picker.FileTypeChoices.Add("Macro", new List<string> { MacroStorage.FileExtension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await MacroStorage.SaveAsync(_macro, file.Path);
            StatusText.Text = Loc.Format("status.saved", file.Path);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Format("status.writeFailed", Explain(ex)));
        }
    }

    // --- Utilitaires ------------------------------------------------------

    private void Populate(Macro macro)
    {
        Rows.Clear();

        for (int i = 0; i < macro.Events.Count; i++)
        {
            MacroEvent ev = macro.Events[i];
            Rows.Add(new EventRow(
                (i + 1).ToString(Loc.Culture),
                $"+{ev.DelayMs} ms",
                EventDescriber.Describe(ev)));
        }

        UpdateEventList();
    }

    private void UpdateEventList()
    {
        EventCountText.Text = Rows.Count.ToString(Loc.Culture);

        bool empty = Rows.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EventList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateButtons()
    {
        bool busy = _recorder.IsRecording || _playbackCts is not null || _picker.IsPicking || _modalOpen;
        bool hasMacro = _macro is { Events.Count: > 0 };

        PickReferenceButton.IsEnabled = !busy && TargetCombo.SelectedItem is WindowChoice { Window: not null };
        ClearReferenceButton.IsEnabled = !busy && _reference is not null;

        RecordButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
        PlayButton.IsEnabled = !busy && hasMacro;
        SimulateButton.IsEnabled = !busy && hasMacro;
        CleanButton.IsEnabled = !busy && hasMacro;
        OpenButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy && hasMacro;
        MouseMoveToggle.IsEnabled = !busy;
        RepeatBox.IsEnabled = !busy;
        SpeedBox.IsEnabled = !busy;
        TargetCombo.IsEnabled = !busy;
        RefreshTargetsButton.IsEnabled = !busy;
        AnchorCombo.IsEnabled = !busy;

        // Le menu système bloque le thread UI, donc les coupe-circuits F9 et Échap.
        AppIconButton.IsEnabled = !busy;
    }

    /// <summary>NumberBox renvoie NaN quand le champ est vide.</summary>
    private static double ReadNumber(double value, double fallback) =>
        double.IsNaN(value) || value <= 0 ? fallback : value;

    /// <summary>
    /// Met une exception en mots pour l'utilisateur.
    /// </summary>
    /// <remarks>
    /// Une erreur du cœur porte un code et des faits, pas de phrase : c'est ici qu'elle
    /// devient lisible, dans la langue courante. Les autres exceptions gardent leur
    /// message, destiné au diagnostic.
    /// </remarks>
    private string Explain(Exception error) =>
        error is AutoClicException known ? Loc.Describe(known) : error.Message;

    /// <summary>
    /// Réévalue tout ce qui porte du texte après un changement de langue.
    /// </summary>
    /// <remarks>
    /// Les liaisons compilées n'observent pas le traducteur : elles ne se réévaluent
    /// que sur appel explicite. Les textes composés en code, eux, doivent être refaits.
    /// </remarks>
    private void ApplyLanguage()
    {
        Bindings.Update();

        LanguageCombo.SelectedItem = Loc.Languages.FirstOrDefault(
            l => string.Equals(l.Code, Loc.Language, StringComparison.OrdinalIgnoreCase));

        if (_reference is null)
        {
            ReferenceText.Text = Loc["reference.none"];
        }

        if (_macro is not null)
        {
            Populate(_macro);
        }

        if (!_recorder.IsRecording && _playbackCts is null && !_picker.IsPicking)
        {
            StatusText.Text = Loc["status.ready"];
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is LanguageInfo choix)
        {
            Loc.Language = choix.Code;
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void HideError() => ErrorBar.IsOpen = false;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _playbackCts?.Cancel();
        _recorder.Dispose();
        _abortWatcher.Dispose();
        _picker.Dispose();
    }
}
