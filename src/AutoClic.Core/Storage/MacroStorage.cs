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
using System.Text.Json.Serialization;
using AutoClic.Core.Diagnostics;
using AutoClic.Core.Models;

namespace AutoClic.Core.Storage;

/// <summary>Lecture / écriture des macros au format JSON.</summary>
public static class MacroStorage
{
    public const string FileExtension = ".json";

    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Dossier par défaut : %APPDATA%\AutoClic.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoClic");

    public static async Task SaveAsync(Macro macro, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, macro, Options, ct).ConfigureAwait(false);
    }

    public static async Task<Macro> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = File.OpenRead(path);
        Macro? macro = await JsonSerializer
            .DeserializeAsync<Macro>(stream, Options, ct)
            .ConfigureAwait(false);

        return macro ?? throw new AutoClicException(
            ErrorCode.MacroFileInvalid,
            $"'{path}' does not contain a valid macro.",
            new Dictionary<string, object?> { ["path"] = path });
    }

    public static string Serialize(Macro macro) => JsonSerializer.Serialize(macro, Options);

    public static Macro Deserialize(string json) =>
        JsonSerializer.Deserialize<Macro>(json, Options)
        ?? throw new AutoClicException(ErrorCode.MacroFileInvalid, "Empty or null JSON.");
}
