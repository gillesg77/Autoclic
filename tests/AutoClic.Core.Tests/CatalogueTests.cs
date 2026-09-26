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

using System.Text.Json;
using System.Text.RegularExpressions;
using AutoClic.Core.Diagnostics;
using Xunit;

namespace AutoClic.Core.Tests;

/// <summary>
/// Vérifie la cohérence des catalogues de traduction.
/// </summary>
/// <remarks>
/// Une clé oubliée dans une langue ne se voit qu'en basculant dans cette langue, et
/// un paramètre égaré ne se voit qu'au moment précis où le message s'affiche — donc
/// rarement, et souvent devant l'utilisateur. Ces contrôles les attrapent à la
/// compilation des tests.
/// </remarks>
public class CatalogueTests
{
    private static readonly string Dossier = LocaliserLeDossier();

    private static string LocaliserLeDossier()
    {
        DirectoryInfo? courant = new(AppContext.BaseDirectory);

        while (courant is not null)
        {
            string candidat = Path.Combine(courant.FullName, "src", "AutoClic.Localization", "Strings");

            if (Directory.Exists(candidat))
            {
                return candidat;
            }

            courant = courant.Parent;
        }

        throw new DirectoryNotFoundException("Dossier des catalogues introuvable.");
    }

    private static Dictionary<string, Dictionary<string, string>> Charger()
    {
        Dictionary<string, Dictionary<string, string>> catalogues = [];

        foreach (string fichier in Directory.GetFiles(Dossier, "*.json"))
        {
            catalogues[Path.GetFileNameWithoutExtension(fichier)] =
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fichier))!;
        }

        return catalogues;
    }

    [Fact]
    public void Chaque_langue_annoncee_est_livree()
    {
        Dictionary<string, Dictionary<string, string>> catalogues = Charger();

        Assert.Equal(
            ["de", "en", "es", "fr", "ko", "pt", "zh"],
            catalogues.Keys.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Toutes_les_langues_portent_les_memes_cles()
    {
        Dictionary<string, Dictionary<string, string>> catalogues = Charger();
        HashSet<string> reference = [.. catalogues["fr"].Keys];

        foreach ((string langue, Dictionary<string, string> catalogue) in catalogues)
        {
            string[] manquantes = [.. reference.Except(catalogue.Keys).OrderBy(k => k, StringComparer.Ordinal)];
            string[] superflues = [.. catalogue.Keys.Except(reference).OrderBy(k => k, StringComparer.Ordinal)];

            Assert.True(manquantes.Length == 0, $"{langue} : clés manquantes — {string.Join(", ", manquantes)}");
            Assert.True(superflues.Length == 0, $"{langue} : clés en trop — {string.Join(", ", superflues)}");
        }
    }

    [Fact]
    public void Chaque_traduction_porte_les_memes_parametres_que_le_francais()
    {
        Dictionary<string, Dictionary<string, string>> catalogues = Charger();
        Dictionary<string, string> reference = catalogues["fr"];

        foreach ((string langue, Dictionary<string, string> catalogue) in catalogues)
        {
            foreach ((string cle, string modele) in reference)
            {
                if (!catalogue.TryGetValue(cle, out string? traduction))
                {
                    continue;
                }

                // Un paramètre en trop lèverait à l'exécution ; un paramètre en moins
                // ferait disparaître une information sans prévenir.
                Assert.Equal(
                    Parametres(modele),
                    Parametres(traduction));
            }
        }
    }

    [Fact]
    public void Chaque_code_d_erreur_a_sa_traduction()
    {
        Dictionary<string, Dictionary<string, string>> catalogues = Charger();

        foreach (ErrorCode code in Enum.GetValues<ErrorCode>())
        {
            string nom = code.ToString();
            string cle = "error." + char.ToLowerInvariant(nom[0]) + nom[1..];

            foreach ((string langue, Dictionary<string, string> catalogue) in catalogues)
            {
                Assert.True(catalogue.ContainsKey(cle), $"{langue} : « {cle} » manque.");
            }
        }
    }

    /// <summary>Paramètres numérotés et nommés, triés, doublons retirés.</summary>
    private static string[] Parametres(string modele) =>
        [.. Regex.Matches(modele, @"\{([A-Za-z0-9]+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)];
}
