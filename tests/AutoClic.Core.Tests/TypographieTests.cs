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

using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;
using AutoClic.Core.Vision;
using AutoClic.Localization;
using Xunit;

namespace AutoClic.Core.Tests;

/// <summary>
/// Vérifie que changer de langue change aussi la ponctuation et la mise en forme
/// des nombres, pas seulement les mots.
/// </summary>
/// <remarks>
/// Les deux défauts que ces tests verrouillent ont survécu à toute la traduction et
/// ne sont apparus qu'en lisant un vrai message de refus : le séparateur de causes
/// était figé à « ; », typographie française appliquée à l'anglais et à l'allemand,
/// et les pourcentages gardaient la culture du système — « 71 % » en plein texte
/// anglais. Rien dans le code ne les signalait ; seul l'œil sur la phrase finale.
/// </remarks>
public class TypographieTests
{
    private static readonly string Dossier = Path.Combine(AppContext.BaseDirectory, "Strings");

    private static Localizer Charger(string langue)
    {
        Localizer loc = Localizer.Current;
        loc.Load(Dossier, langue);
        loc.Language = langue;
        return loc;
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("zh")]
    [InlineData("ko")]
    public void Chaque_langue_a_son_separateur(string langue)
    {
        Localizer loc = Charger(langue);

        string separateur = loc["list.separator"];

        Assert.NotEqual("list.separator", separateur);

        // Seul le français place une espace devant le point-virgule.
        if (langue != "fr")
        {
            Assert.DoesNotContain(" ;", separateur, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("pt")]
    public void Le_refus_n_enchaine_pas_les_causes_a_la_francaise(string langue)
    {
        Localizer loc = Charger(langue);

        Assert.DoesNotContain(" ;", loc.Describe(Refus()), StringComparison.Ordinal);
    }

    /// <summary>
    /// L'anglais colle le signe au nombre. L'allemand et l'espagnol, eux, gardent
    /// l'espace comme le français : la règle n'est pas « tout sauf le français ».
    /// </summary>
    [Fact]
    public void Le_refus_colle_le_signe_pourcent_en_anglais()
    {
        Localizer loc = Charger("en");

        string message = loc.Describe(Refus());

        Assert.Contains("71%", message, StringComparison.Ordinal);
        Assert.DoesNotContain(" %", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_refus_garde_la_typographie_francaise_en_francais()
    {
        Localizer loc = Charger("fr");

        string message = loc.Describe(Refus());

        Assert.Contains(" ;", message, StringComparison.Ordinal);
        Assert.Contains(" %", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fr", "71 %")]
    [InlineData("en", "71%")]
    public void Les_pourcentages_suivent_la_langue(string langue, string attendu)
    {
        Localizer loc = Charger(langue);

        Assert.Equal(attendu, 0.71.ToString("P0", loc.Culture));
    }

    /// <summary>Le refus tel que le produit <c>ReferenceResolver</c>, faits compris.</summary>
    private static AutoClicException Refus() =>
        new(ErrorCode.ReferenceNotFound,
            "Landmark scored below threshold.",
            new Dictionary<string, object?>
            {
                ["label"] = "en-tête",
                ["score"] = 0.71,
                ["threshold"] = 0.95,
                ["matching"] = ReferenceMatching.Pixel,
                ["tolerance"] = 12,
                ["currentWidth"] = 504,
                ["currentHeight"] = 412,
                ["recordedWidth"] = 1164,
                ["recordedHeight"] = 772,
                ["shrunk"] = true,
            });
}
