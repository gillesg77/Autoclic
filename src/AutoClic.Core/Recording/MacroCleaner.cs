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

using AutoClic.Core.Models;

namespace AutoClic.Core.Recording;

/// <summary>Réglages du nettoyage d'une macro.</summary>
public sealed record CleanupOptions
{
    /// <summary>
    /// Supprime les déplacements enregistrés entre deux clics — ceux du moment où l'on
    /// cherchait l'élément à l'écran.
    /// </summary>
    public bool RemoveIdleMoves { get; init; } = true;

    /// <summary>
    /// Remplace le trajet supprimé par une ligne droite vers la cible.
    /// </summary>
    /// <remarks>
    /// Ne pas la cocher fait sauter le curseur d'un clic à l'autre. C'est plus rapide,
    /// mais une interface qui ne révèle un élément qu'au survol du parent ne réagira
    /// pas — un menu déroulant, par exemple.
    /// </remarks>
    public bool StraightLineMoves { get; init; } = true;

    /// <summary>
    /// Conserve le temps passé à chercher l'élément, sous forme de pause avant le
    /// déplacement.
    /// </summary>
    /// <remarks>
    /// Décoché — le défaut — ce temps est supprimé avec les déplacements qui le
    /// portaient : c'est bien de l'hésitation, pas une attente que l'application
    /// impose. Le cocher conserve la durée totale d'origine à la milliseconde près.
    /// </remarks>
    public bool KeepIdleTime { get; init; }

    /// <summary>Durée du trajet reconstruit, en millisecondes par 100 pixels.</summary>
    public int TravelMsPer100Px { get; init; } = 60;

    /// <summary>Plancher de durée d'un trajet, pour qu'un petit saut reste perceptible.</summary>
    public int MinTravelMs { get; init; } = 60;

    /// <summary>
    /// Plafond de durée d'un trajet.
    /// </summary>
    /// <remarks>
    /// Sans plafond, une longue traversée d'écran prendrait plusieurs secondes sans
    /// rien apporter.
    /// </remarks>
    public int MaxTravelMs { get; init; } = 450;

    /// <summary>
    /// Intervalle visé entre deux points de passage.
    /// </summary>
    /// <remarks>
    /// C'est la densité temporelle, et non la densité spatiale, qui décide de la
    /// fluidité : des points espacés de 10 ms donnent un déplacement continu à l'œil,
    /// quelle que soit la distance parcourue.
    /// </remarks>
    public int StepIntervalMs { get; init; } = 10;

    /// <summary>Borne le nombre de points de passage pour un même déplacement.</summary>
    public int MaxStepsPerMove { get; init; } = 120;

    /// <summary>
    /// Redresse aussi les déplacements effectués bouton enfoncé.
    /// </summary>
    /// <remarks>
    /// Pour un glisser-déposer ordinaire — attraper un élément, le lâcher ailleurs —
    /// seuls le point de prise et le point de lâcher comptent, et l'activer ne change
    /// rien au résultat. Le trajet redevient significatif dans trois cas : une
    /// arborescence qui déplie un dossier au survol pendant le glisser, une liste qui
    /// défile quand on approche du bord, et tout tracé libre — lasso, dessin,
    /// signature — où le chemin est la donnée elle-même. D'où le défaut prudent.
    /// </remarks>
    public bool SimplifyDrags { get; init; }

    /// <summary>
    /// Plafonne chaque attente conservée, en millisecondes. Zéro ne plafonne rien.
    /// </summary>
    /// <remarks>
    /// Ne s'applique qu'aux délais portés par les événements gardés — les vraies
    /// attentes, celles où l'on patientait pendant que l'application répondait.
    /// </remarks>
    public int MaxDelayMs { get; init; }
}

/// <summary>Ce que le nettoyage a changé, pour l'annoncer avant de l'appliquer.</summary>
public readonly record struct CleanupReport(
    int EventsBefore,
    int EventsAfter,
    int RemovedMoves,
    int InsertedMoves,
    TimeSpan DurationBefore,
    TimeSpan DurationAfter);

/// <summary>
/// Débarrasse une macro des déplacements de recherche et rectifie les trajets.
/// </summary>
public static class MacroCleaner
{
    /// <summary>En deçà de cette distance, reconstruire un trajet n'apporte rien.</summary>
    private const double NegligibleDistance = 6;

    /// <summary>Renvoie une copie nettoyée ; la macro d'origine n'est pas modifiée.</summary>
    public static (Macro Cleaned, CleanupReport Report) Clean(Macro macro, CleanupOptions options)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(options);

        List<MacroEvent> output = [];
        HashSet<MouseButton> held = [];

        MouseEvent? lastPosition = null;
        int removedTime = 0;
        int removed = 0;
        int inserted = 0;
        bool skippedSinceLastKept = false;

        foreach (MacroEvent ev in macro.Events)
        {
            // Bouton enfoncé : le déplacement fait partie d'un glisser, et n'est
            // simplifié que si l'utilisateur l'a explicitement demandé.
            bool dragging = held.Count > 0;
            bool simplify = dragging ? options.SimplifyDrags : options.RemoveIdleMoves;

            if (simplify && ev is MouseMoveEvent)
            {
                removedTime += ev.DelayMs;
                removed++;
                skippedSinceLastKept = true;
                continue;
            }

            // Le délai propre à l'événement gardé est une vraie attente : il survit tel
            // quel. Le temps des déplacements supprimés, lui, finance le trajet.
            int ownDelay = Cap(ev.DelayMs, options.MaxDelayMs);

            bool line = dragging ? options.SimplifyDrags : options.StraightLineMoves;
            int minimumSteps = dragging ? 2 : 0;

            if (ev is MouseEvent target && skippedSinceLastKept && line)
            {
                inserted += Interpolate(output, lastPosition, target, removedTime, options, minimumSteps);
            }
            else if (skippedSinceLastKept && options.KeepIdleTime)
            {
                // Rien à relier, mais le temps supprimé doit être rendu.
                ownDelay += removedTime;
            }

            if (skippedSinceLastKept)
            {
                removedTime = 0;
                skippedSinceLastKept = false;
            }

            output.Add(ev with { DelayMs = ownDelay });

            if (ev is MouseEvent moved)
            {
                lastPosition = moved;
            }

            Track(held, ev);
        }

        Macro cleaned = new()
        {
            Name = macro.Name,
            CreatedAt = macro.CreatedAt,
            Target = macro.Target,
            AnchorMode = macro.AnchorMode,
            Reference = macro.Reference,
            Events = output,
        };

        return (cleaned, new CleanupReport(
            macro.Events.Count,
            cleaned.Events.Count,
            removed,
            inserted,
            macro.Duration,
            cleaned.Duration));
    }

    /// <summary>
    /// Insère les points de passage d'une ligne droite vers <paramref name="to"/>.
    /// </summary>
    /// <param name="removedTime">Temps porté par les déplacements supprimés.</param>
    /// <returns>Nombre de points insérés.</returns>
    private static int Interpolate(
        List<MacroEvent> output,
        MouseEvent? from,
        MouseEvent to,
        int removedTime,
        CleanupOptions options,
        int minimumSteps)
    {
        if (from is null)
        {
            return 0; // aucune position de départ connue : rien à relier
        }

        double distance = Distance(from, to);

        if (distance <= NegligibleDistance && minimumSteps == 0)
        {
            return 0;
        }

        int travel = TravelDuration(distance, removedTime, options);
        int steps = StepCount(distance, travel, options, minimumSteps);

        int slice = Math.Max(1, travel / steps);

        // Le reliquat de la division entière revient au premier pas : sans lui, une
        // macro dont on conserve le temps de recherche perdrait quelques millisecondes
        // à chaque trajet.
        int remainder = Math.Max(0, travel - (slice * steps));

        // Le temps de recherche non consommé par le trajet devient une pause avant le
        // départ : le curseur attend, puis file vers sa cible.
        int pause = options.KeepIdleTime ? Math.Max(0, removedTime - travel) : 0;

        for (int i = 1; i <= steps; i++)
        {
            // Accélération puis décélération : un déplacement à vitesse constante se
            // repère immédiatement comme mécanique.
            double t = Smooth(i / (double)(steps + 1));

            output.Add(Lerp(from, to, t, i == 1 ? pause + remainder + slice : slice));
        }

        return steps;
    }

    /// <summary>
    /// Durée du trajet : proportionnelle à la distance, bornée, et jamais plus longue
    /// que le temps réellement passé à l'origine.
    /// </summary>
    private static int TravelDuration(double distance, int removedTime, CleanupOptions options)
    {
        int minimum = Math.Max(1, options.MinTravelMs);
        int natural = (int)(distance * Math.Max(1, options.TravelMsPer100Px) / 100.0);

        int travel = Math.Clamp(natural, minimum, Math.Max(minimum, options.MaxTravelMs));

        // Ne pas rallonger une macro qui allait plus vite que le modèle — mais le
        // plancher reste impératif : sous quelques dizaines de millisecondes, il ne
        // reste plus assez de pas pour que le déplacement soit continu.
        return removedTime > 0 ? Math.Max(minimum, Math.Min(travel, removedTime)) : travel;
    }

    /// <summary>
    /// Nombre de points de passage : gouverné par la durée, pas par la distance.
    /// </summary>
    /// <remarks>
    /// C'est le correctif du défaut d'origine. Répartir un temps d'hésitation de
    /// plusieurs secondes sur une poignée de points espacés en distance donnait des
    /// sauts de 40 pixels toutes les 140 millisecondes — soit environ sept images par
    /// seconde, parfaitement saccadé.
    /// </remarks>
    private static int StepCount(double distance, int travel, CleanupOptions options, int minimumSteps)
    {
        int byTime = travel / Math.Max(1, options.StepIntervalMs);

        // Inutile d'avoir plus de points que de pixels à parcourir.
        int byDistance = (int)(distance / 2);

        int steps = Math.Min(byTime, byDistance);

        return Math.Clamp(
            steps,
            Math.Max(1, minimumSteps),
            Math.Max(Math.Max(1, minimumSteps), options.MaxStepsPerMove));
    }

    /// <summary>Courbe en S : départ et arrivée en douceur.</summary>
    private static double Smooth(double t) => t * t * (3 - (2 * t));

    private static MouseMoveEvent Lerp(MouseEvent from, MouseEvent to, double t, int delayMs)
    {
        WindowAnchor? anchor = null;

        // L'ancrage n'est interpolable que si les deux extrémités se rapportent à la
        // même taille de fenêtre — sinon les coordonnées clientes ne sont pas comparables.
        if (from.Anchor is { } a && to.Anchor is { } b
            && a.ClientWidth == b.ClientWidth && a.ClientHeight == b.ClientHeight)
        {
            anchor = new WindowAnchor
            {
                ClientX = Between(a.ClientX, b.ClientX, t),
                ClientY = Between(a.ClientY, b.ClientY, t),
                ClientWidth = a.ClientWidth,
                ClientHeight = a.ClientHeight,
            };
        }

        return new MouseMoveEvent
        {
            DelayMs = delayMs,
            X = Between(from.X, to.X, t),
            Y = Between(from.Y, to.Y, t),
            Anchor = anchor,
        };
    }

    private static int Between(int from, int to, double t) => (int)Math.Round(from + ((to - from) * t));

    private static double Distance(MouseEvent from, MouseEvent to)
    {
        // Les coordonnées clientes priment : elles restent justes même si la fenêtre a
        // bougé pendant l'enregistrement.
        if (from.Anchor is { } a && to.Anchor is { } b)
        {
            return Math.Sqrt(Math.Pow(b.ClientX - a.ClientX, 2) + Math.Pow(b.ClientY - a.ClientY, 2));
        }

        return Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
    }

    private static int Cap(int delay, int maximum) => maximum <= 0 ? delay : Math.Min(delay, maximum);

    private static void Track(HashSet<MouseButton> held, MacroEvent ev)
    {
        if (ev is not MouseButtonEvent button)
        {
            return;
        }

        if (button.IsDown)
        {
            held.Add(button.Button);
        }
        else
        {
            held.Remove(button.Button);
        }
    }
}
