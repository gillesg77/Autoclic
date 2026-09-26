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

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using AutoClic.Core.Diagnostics;

namespace AutoClic.Localization;

public sealed record LanguageInfo(string Code, string DisplayName);

/// <summary>
/// Traductions chargées depuis des fichiers JSON plats.
/// </summary>
/// <remarks>
/// Le changement de langue est immédiat : les liaisons passent par
/// <see cref="Get"/>, et la notification sur « Item[] » les invalide toutes d'un coup.
/// </remarks>
public sealed class Localizer : INotifyPropertyChanged
{
    public const string FallbackLanguage = "en";

    private readonly Dictionary<string, Dictionary<string, string>> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LanguageInfo> _languages = [];

    private string _language = FallbackLanguage;
    private CultureInfo _culture = CultureInfo.InvariantCulture;

    public static Localizer Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageInfo> Languages => _languages;

    /// <summary>
    /// Culture de formatage correspondant à la langue choisie.
    /// </summary>
    /// <remarks>
    /// Sans elle, les nombres garderaient les conventions du système : « 71 % » avec
    /// une espace avant le signe, correct en français, fautif en anglais. Changer de
    /// langue doit changer la ponctuation des chiffres, pas seulement les mots.
    /// </remarks>
    public CultureInfo Culture => _culture;

    private static CultureInfo CultureFor(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string Language
    {
        get => _language;
        set
        {
            if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase)
                || !_catalogs.ContainsKey(value))
            {
                return;
            }

            _language = value;
            _culture = CultureFor(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Clé absente : la clé elle-même est rendue.
    /// </summary>
    /// <remarks>
    /// Un manque devient ainsi visible à l'écran sans faire échouer l'affichage.
    /// </remarks>
    public string this[string key] => Lookup(key) ?? key;

    /// <summary>
    /// Forme appelable depuis XAML.
    /// </summary>
    /// <remarks>
    /// Les liaisons compilées acceptent un appel de méthode à argument littéral, pas
    /// un indexeur à clé pointée.
    /// </remarks>
    public string Get(string key) => Lookup(key) ?? key;

    public string Format(string key, params object?[] args)
    {
        string pattern = Lookup(key) ?? key;
        return args.Length == 0 ? pattern : string.Format(_culture, pattern, args);
    }

    /// <summary>
    /// Met en mots une erreur du cœur, dans la langue courante.
    /// </summary>
    /// <remarks>
    /// Les paramètres nommés de l'exception sont substitués sous la forme {nom}. Le
    /// cœur ne porte donc aucun texte d'interface.
    /// </remarks>
    public string Describe(AutoClicException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string code = error.Code.ToString();
        string key = "error." + char.ToLowerInvariant(code[0]) + code[1..];
        string pattern = Lookup(key) ?? Lookup("error.unknown") ?? error.Message;

        IReadOnlyDictionary<string, object?> args =
            error.Code == ErrorCode.ReferenceNotFound ? WithDiagnosis(error.Args) : error.Args;

        foreach ((string name, object? value) in args)
        {
            pattern = pattern.Replace(
                "{" + name + "}",
                Convert.ToString(value, _culture) ?? string.Empty,
                StringComparison.Ordinal);
        }

        return pattern;
    }

    /// <summary>
    /// Compose la mesure et les causes d'un repère non retrouvé.
    /// </summary>
    /// <remarks>
    /// Le cœur ne livre que des faits ; choisir les causes à énoncer est un acte de
    /// rédaction, donc il appartient à cette couche. Les causes dépendent des faits :
    /// le rétrécissement n'est mentionné que s'il a eu lieu, et le reproche fait au
    /// rendu n'a de sens qu'en comparaison au pixel.
    /// </remarks>
    private Dictionary<string, object?> WithDiagnosis(IReadOnlyDictionary<string, object?> facts)
    {
        Dictionary<string, object?> enriched = new(facts);

        string Percent(string name) =>
            facts.GetValueOrDefault(name) is double d
                ? d.ToString("P0", _culture)
                : string.Empty;

        bool pixel = facts.GetValueOrDefault("matching")?.ToString() == "Pixel";

        enriched["measure"] = Format(
            pixel ? "measure.pixel" : "measure.correlation", Percent("score"), Percent("threshold"));

        List<string> causes = [];

        if (facts.GetValueOrDefault("shrunk") is true)
        {
            causes.Add(Format(
                "cause.shrunk",
                facts.GetValueOrDefault("currentWidth"),
                facts.GetValueOrDefault("currentHeight"),
                facts.GetValueOrDefault("recordedWidth"),
                facts.GetValueOrDefault("recordedHeight")));
        }

        causes.Add(this["cause.otherScreen"]);
        causes.Add(pixel
            ? Format("cause.rendering", facts.GetValueOrDefault("tolerance"))
            : this["cause.theme"]);

        enriched["causes"] = string.Join(this["list.separator"], causes);

        return enriched;
    }

    public void Load(string directory, string? preferred = null)
    {
        _catalogs.Clear();
        _languages.Clear();

        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                TryLoadFile(file);
            }
        }

        if (_catalogs.Count == 0)
        {
            _catalogs[FallbackLanguage] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach ((string code, Dictionary<string, string> catalog) in
                 _catalogs.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            _languages.Add(new LanguageInfo(code, catalog.GetValueOrDefault("language.name", code)));
        }

        _language = Resolve(preferred);
        _culture = CultureFor(_language);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Languages)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        // Les catalogues arrivent après la construction des vues : sans cet appel, les
        // liaisons déjà évaluées resteraient figées sur le nom de leur clé.
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryLoadFile(string file)
    {
        try
        {
            Dictionary<string, string>? entries =
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));

            if (entries is null)
            {
                return;
            }

            _catalogs[Path.GetFileNameWithoutExtension(file)] =
                new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Fichier de langue illisible : les autres restent disponibles.
        }
    }

    private string Resolve(string? preferred)
    {
        if (preferred is not null && _catalogs.ContainsKey(preferred))
        {
            return preferred;
        }

        string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (_catalogs.ContainsKey(system))
        {
            return system;
        }

        return _catalogs.ContainsKey(FallbackLanguage) ? FallbackLanguage : _catalogs.Keys.First();
    }

    /// <summary>
    /// Cherche la clé dans la langue courante, puis dans la langue de repli.
    /// </summary>
    /// <remarks>
    /// Une traduction incomplète reste ainsi livrable : les clés manquantes tombent en
    /// anglais plutôt que de laisser un trou.
    /// </remarks>
    private string? Lookup(string key)
    {
        if (_catalogs.TryGetValue(_language, out Dictionary<string, string>? catalog)
            && catalog.TryGetValue(key, out string? value))
        {
            return value;
        }

        if (_catalogs.TryGetValue(FallbackLanguage, out Dictionary<string, string>? fallback)
            && fallback.TryGetValue(key, out string? backup))
        {
            return backup;
        }

        return null;
    }
}
