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

using System.Drawing;
using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Playback;

namespace AutoClic.Core.Vision;

/// <summary>
/// Résultat de la vérification du repère. En cas d'échec, <see cref="Failure"/> est
/// rédigé pour être montré tel quel à l'utilisateur.
/// </summary>
/// <summary>
/// Résultat de la vérification du repère.
/// </summary>
/// <remarks>
/// En cas d'échec, <see cref="Error"/> porte un code et des faits chiffrés, jamais du
/// texte : c'est la couche de traduction qui le met en mots.
/// </remarks>
public readonly record struct ReferenceCheck(
    bool Found, int OffsetX, int OffsetY, double Score, AutoClicException? Error)
{
    public static ReferenceCheck Failed(AutoClicException error) => new(false, 0, 0, 0, error);
}

/// <summary>Retrouve le repère visuel dans la fenêtre et en déduit la correction à appliquer.</summary>
public static class ReferenceResolver
{
    /// <summary>
    /// Capture une région de la zone cliente, sans encore en faire un repère.
    /// </summary>
    /// <remarks>
    /// La capture est séparée de la construction pour que l'utilisateur puisse voir,
    /// puis détourer, exactement les pixels qui serviront de repère. Recapturer au
    /// moment de valider donnerait une image potentiellement différente — une
    /// animation, un survol — de celle qu'il a examinée.
    /// </remarks>
    public static CapturedRegion? CaptureRegion(nint window, int clientX, int clientY, int width, int height)
    {
        using Bitmap? shot = WindowCapture.CaptureClientArea(window);
        if (shot is null)
        {
            return null;
        }

        Bitmap? patch = WindowCapture.Crop(shot, clientX, clientY, width, height);
        return patch is null ? null : new CapturedRegion(patch, clientX, clientY, shot.Width, shot.Height);
    }

    /// <summary>
    /// Construit le repère à partir d'une région déjà capturée.
    /// </summary>
    /// <param name="outline">
    /// Détourage facultatif, en coordonnées de la région capturée. Null conserve tout
    /// le rectangle.
    /// </param>
    public static VisualReference Build(
        CapturedRegion region,
        string label,
        IReadOnlyList<Point>? outline = null,
        ReferenceMatching matching = ReferenceMatching.Pixel,
        int pixelTolerance = PatternMatcher.DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(region);

        bool outlined = outline is { Count: >= 3 };
        using Bitmap stored = outlined
            ? WindowCapture.ApplyOutline(region.Patch, outline!)
            : region.Patch;

        return new VisualReference
        {
            Label = string.IsNullOrWhiteSpace(label) ? "landmark" : label,
            ClientX = region.ClientX,
            ClientY = region.ClientY,
            Width = stored.Width,
            Height = stored.Height,
            ClientWidth = region.ClientWidth,
            ClientHeight = region.ClientHeight,
            ImagePng = WindowCapture.ToBase64Png(stored),
            IsOutlined = outlined,
            Matching = matching,
            PixelTolerance = pixelTolerance,
        };
    }

    /// <summary>
    /// Cherche le repère autour de la position déduite du redimensionnement et renvoie
    /// l'écart entre la position trouvée et la position attendue.
    /// </summary>
    public static ReferenceCheck Verify(nint window, VisualReference reference, AnchorMode mode)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrEmpty(reference.ImagePng))
        {
            return ReferenceCheck.Failed(new AutoClicException(
                ErrorCode.ReferenceEmpty,
                $"Landmark '{reference.Label}' carries no image.",
                new Dictionary<string, object?> { ["label"] = reference.Label }));
        }

        using Bitmap? shot = WindowCapture.CaptureClientArea(window);
        if (shot is null)
        {
            return ReferenceCheck.Failed(new AutoClicException(
                ErrorCode.WindowCaptureFailed, "The target window could not be captured."));
        }

        using Bitmap patchBitmap = WindowCapture.FromBase64Png(reference.ImagePng);
        ReferencePattern pattern = ReferencePattern.FromBitmap(patchBitmap);

        (int expectedX, int expectedY) = ExpectedPosition(reference, shot.Width, shot.Height, mode);

        MatchResult? match = PatternMatcher.Find(
            PixelBuffer.FromBitmap(shot),
            pattern,
            expectedX,
            expectedY,
            reference.SearchRadius,
            reference.Matching,
            reference.PixelTolerance);

        if (match is null)
        {
            return ReferenceCheck.Failed(new AutoClicException(
                ErrorCode.ReferenceUnsearchable,
                $"Landmark '{reference.Label}' ({pattern.Width}x{pattern.Height}) does not fit in {shot.Width}x{shot.Height}.",
                new Dictionary<string, object?>
                {
                    ["label"] = reference.Label,
                    ["width"] = pattern.Width,
                    ["height"] = pattern.Height,
                    ["clientWidth"] = shot.Width,
                    ["clientHeight"] = shot.Height,
                }));
        }

        MatchResult found = match.Value;

        if (found.Score < reference.MinimumScore)
        {
            return ReferenceCheck.Failed(Explain(reference, shot.Width, shot.Height, found.Score));
        }

        return new ReferenceCheck(true, found.X - expectedX, found.Y - expectedY, found.Score, null);
    }

    private static (int X, int Y) ExpectedPosition(VisualReference reference, int width, int height, AnchorMode mode)
    {
        var anchor = new WindowAnchor
        {
            ClientX = reference.ClientX,
            ClientY = reference.ClientY,
            ClientWidth = reference.ClientWidth,
            ClientHeight = reference.ClientHeight,
        };

        return AnchorSolver.Resolve(anchor, width, height, mode);
    }

    /// <summary>
    /// Rassemble les faits d'un repère non retrouvé.
    /// </summary>
    /// <remarks>
    /// Aucun texte ici : seulement des nombres et des drapeaux. C'est la couche de
    /// traduction qui choisit les causes à énoncer et dans quelle langue.
    /// </remarks>
    private static AutoClicException Explain(
        VisualReference reference, int currentWidth, int currentHeight, double score) =>
        new(ErrorCode.ReferenceNotFound,
            $"Landmark '{reference.Label}' scored {score:P0}, below the {reference.MinimumScore:P0} threshold.",
            new Dictionary<string, object?>
            {
                ["label"] = reference.Label,
                ["score"] = score,
                ["threshold"] = reference.MinimumScore,
                ["matching"] = reference.Matching,
                ["tolerance"] = reference.PixelTolerance,
                ["currentWidth"] = currentWidth,
                ["currentHeight"] = currentHeight,
                ["recordedWidth"] = reference.ClientWidth,
                ["recordedHeight"] = reference.ClientHeight,
                ["shrunk"] = currentWidth < reference.ClientWidth || currentHeight < reference.ClientHeight,
            });
}
