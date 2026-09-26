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

using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace AutoClic.App;

/// <summary>
/// Transforme une fenêtre WinUI ordinaire en boîte de dialogue : possédée par une
/// fenêtre mère, modale, et dimensionnée sur son contenu.
/// </summary>
/// <remarks>
/// <para>
/// Remplace <c>ContentDialog</c>, qui borne son contenu à la fenêtre hôte et à une
/// largeur maximale interne : un contenu plus large s'y fait rogner au lieu
/// d'agrandir la boîte.
/// </para>
/// <para>
/// Le lien de possession passe par <c>GWLP_HWNDPARENT</c> et non par
/// <c>AppWindow.OwnerWindowId</c>, en lecture seule dans cette version du Windows App
/// SDK — le seul moyen officiel d'attribuer un propriétaire, <c>AppWindow.Create</c>,
/// ne s'applique pas à une fenêtre XAML dont l'AppWindow est créée pour nous.
/// </para>
/// <para>
/// La modalité repose sur <c>EnableWindow</c> et non sur
/// <c>OverlappedPresenter.IsModal</c> : mesuré sur cette version, IsModal laisse la
/// fenêtre mère active, donc cliquable.
/// </para>
/// </remarks>
internal static class ModalHost
{
    private const int GWLP_HWNDPARENT = -8;

    /// <summary>Marge laissée autour de la boîte dans la zone de travail.</summary>
    private const int WorkAreaPadding = 80;

    private const int MinimumWidth = 240;
    private const int MinimumHeight = 160;

    /// <summary>
    /// Écart en deçà duquel la correction après mesure n'a pas lieu.
    /// </summary>
    /// <remarks>
    /// Un redimensionnement de quelques pixels se verrait sans rien apporter.
    /// </remarks>
    private const int RefinementThreshold = 6;

    /// <summary>
    /// Affiche la fenêtre en modale au-dessus de <paramref name="owner"/> et rend la
    /// main à sa fermeture.
    /// </summary>
    /// <param name="root">
    /// Élément dont la taille désirée fixe celle de la fenêtre. Ses marges comptent.
    /// </param>
    /// <param name="maximumContentWidth">
    /// Largeur logique au-delà de laquelle le contenu doit se replier plutôt que
    /// s'étaler.
    /// </param>
    /// <param name="initialClientSize">
    /// Taille logique estimée du contenu, appliquée avant l'activation.
    /// </param>
    /// <remarks>
    /// L'estimation n'est pas un pis-aller : le contenu XAML n'est mesurable qu'une
    /// fois la fenêtre activée — avant, il n'a pas de <c>XamlRoot</c> et sa taille
    /// désirée vaut zéro. Activer puis redimensionner ferait apparaître la fenêtre à sa
    /// taille par défaut, mesurée à 1920×1023, avant de la voir se rétracter.
    /// Activer hors écran ne marche pas non plus : WinUI passe par le compositeur, qui
    /// ne fait aucune passe de mise en page pour une fenêtre qu'aucun moniteur ne
    /// couvre — <c>Loaded</c> ne se déclenche alors jamais et la fenêtre reste perdue.
    /// D'où ce schéma : naître à peu près à la bonne taille, puis corriger après mesure
    /// si l'écart le justifie.
    /// </remarks>
    public static async Task ShowModalAsync(
        Window window, Window owner, FrameworkElement root, Size initialClientSize, double maximumContentWidth = 660)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(root);

        nint handle = WindowNative.GetWindowHandle(window);
        nint ownerHandle = WindowNative.GetWindowHandle(owner);

        // Le propriétaire doit être posé AVANT la première activation : une fenêtre
        // activée sans propriétaire crée son entrée de barre des tâches, et celle-ci ne
        // disparaît pas rétroactivement.
        SetWindowLongPtrW(handle, GWLP_HWNDPARENT, ownerHandle);

        OverlappedPresenter presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;

        // Redimensionnable à dessein : filet de sécurité si le contenu dépasse malgré
        // le calcul, sur un petit écran ou à forte mise à l'échelle.
        presenter.IsResizable = true;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        // Taille et position AVANT l'activation : la fenêtre n'apparaît jamais à la
        // taille par défaut du système.
        Resize(window, initialClientSize, DpiScale(ownerHandle), ownerHandle);

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool restored = false;

        void Restore()
        {
            if (restored)
            {
                return;
            }

            restored = true;

            // Tout premier geste : une fenêtre mère laissée désactivée ne reçoit plus
            // ni clic ni WM_SYSCOMMAND, donc plus même Alt+F4.
            EnableWindow(ownerHandle, true);
            SetForegroundWindow(ownerHandle);
            completion.TrySetResult();
        }

        void OnOwnerClosed(object sender, WindowEventArgs args)
        {
            // Windows détruit d'office une fenêtre possédée avec son propriétaire ;
            // sans cela l'attente ci-dessous ne se terminerait jamais.
            window.Close();
            Restore();
        }

        void OnClosed(object sender, WindowEventArgs args)
        {
            owner.Closed -= OnOwnerClosed;
            Restore();
        }

        bool refined = false;

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            // Une seule correction : les rechargements ultérieurs du contenu ne doivent
            // pas redimensionner une fenêtre que l'utilisateur a peut-être ajustée.
            if (refined)
            {
                return;
            }

            refined = true;
            Refine(window, root, ownerHandle, initialClientSize, maximumContentWidth);
        }

        window.Closed += OnClosed;
        owner.Closed += OnOwnerClosed;
        root.Loaded += OnLoaded;

        try
        {
            EnableWindow(ownerHandle, false);
            window.Activate();

            if (root.IsLoaded)
            {
                OnLoaded(root, new RoutedEventArgs());
            }

            await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            // Filet ultime : si l'activation ou la mesure lève, la fenêtre mère ne doit
            // pas rester inerte pour le restant de la session.
            owner.Closed -= OnOwnerClosed;
            Restore();
        }
    }

    /// <summary>
    /// Corrige la taille de la fenêtre d'après la mesure réelle du contenu, si l'écart
    /// avec l'estimation le justifie.
    /// </summary>
    private static void Refine(
        Window window, FrameworkElement root, nint ownerHandle, Size estimate, double maximumWidth)
    {
        root.Measure(new Size(maximumWidth, double.PositiveInfinity));
        Size desired = root.DesiredSize;

        if (desired.Width <= 0 || desired.Height <= 0)
        {
            return; // mesure indisponible : l'estimation reste en place, la fenêtre est déjà visible
        }

        if (Math.Abs(desired.Width - estimate.Width) < RefinementThreshold
            && Math.Abs(desired.Height - estimate.Height) < RefinementThreshold)
        {
            return; // l'estimation était bonne, ne rien faire bouger
        }

        Resize(window, desired, DpiScale(ownerHandle), ownerHandle);
    }

    private static void Resize(Window window, Size desired, double scale, nint ownerHandle)
    {
        RectInt32 work = WorkArea(ownerHandle);

        int maximumClientWidth = Math.Max(MinimumWidth, work.Width - WorkAreaPadding);
        int maximumClientHeight = Math.Max(MinimumHeight, work.Height - WorkAreaPadding);

        window.AppWindow.ResizeClient(new SizeInt32(
            Math.Clamp((int)Math.Ceiling(desired.Width * scale), MinimumWidth, maximumClientWidth),
            Math.Clamp((int)Math.Ceiling(desired.Height * scale), MinimumHeight, maximumClientHeight)));

        CenterOnOwner(window, ownerHandle, work);
    }

    private static double DpiScale(nint handle)
    {
        double scale = GetDpiForWindow(handle) / 96.0;
        return scale <= 0 ? 1 : scale;
    }

    /// <summary>Zone de travail du moniteur portant la fenêtre mère.</summary>
    private static RectInt32 WorkArea(nint ownerHandle)
    {
        try
        {
            WindowId ownerId = Win32Interop.GetWindowIdFromWindow(ownerHandle);
            return DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest).WorkArea;
        }
        catch (Exception)
        {
            return new RectInt32(0, 0, 1024, 768);
        }
    }

    private static void CenterOnOwner(Window window, nint ownerHandle, RectInt32 work)
    {
        if (!GetWindowRect(ownerHandle, out RECT owner))
        {
            return;
        }

        SizeInt32 size = window.AppWindow.Size;

        int x = owner.left + (((owner.right - owner.left) - size.Width) / 2);
        int y = owner.top + (((owner.bottom - owner.top) - size.Height) / 2);

        // Une boîte plus grande que sa fenêtre mère déborderait de l'écran.
        window.AppWindow.Move(new PointInt32(
            Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - size.Width)),
            Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height))));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    // DllImport et non LibraryImport : ce dernier impose AllowUnsafeBlocks, qu'il
    // serait excessif d'activer sur tout le projet d'interface pour cinq appels.
    // L'application ne cible que win-x64 et win-arm64, la variante 32 bits
    // SetWindowLongW n'a donc pas lieu d'être.
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
