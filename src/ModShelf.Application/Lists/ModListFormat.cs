using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// The file format of a <see cref="ModList"/>, <c>modshelf-list</c> (ADR 0098): JSON, versioned, read leniently. Fields
/// a later version adds are passed over, and a list of a newer version than this one reads is refused, saying so.
/// </summary>
internal static class ModListFormat
{
    /// <summary>
    /// The value of the <c>format</c> field that says a file is a ModShelf list.
    /// </summary>
    public const string Name = "modshelf-list";

    /// <summary>
    /// The version this ModShelf writes, and the newest it reads.
    /// </summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions Reading = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly (ModFileCategory Category, string Name)[] Categories =
    [
        (ModFileCategory.Main, "main"),
        (ModFileCategory.Update, "update"),
        (ModFileCategory.Optional, "optional"),
        (ModFileCategory.Miscellaneous, "miscellaneous"),
        (ModFileCategory.OldVersion, "old-version"),
        (ModFileCategory.Archived, "archived"),
        (ModFileCategory.Unknown, "unknown"),
    ];

    public static string Write(ModList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        JsonObject file = new()
        {
            ["format"] = Name,
            ["version"] = Version,
        };
        if (list.Title is { } title)
        {
            file["title"] = title;
        }

        file["createdAt"] = list.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        JsonArray mods = [];
        foreach (ListedMod mod in list.Mods)
        {
            mods.Add(ModNode(mod));
        }

        file["mods"] = mods;
        return file.ToJsonString(Writing);
    }

    /// <exception cref="InvalidDataException">The text isn't a ModShelf list this version can read, saying why.</exception>
    public static ModList Read(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);

        JsonObject file;
        try
        {
            file = JsonNode.Parse(text, documentOptions: Reading) as JsonObject
                ?? throw new InvalidDataException("This isn't a ModShelf list: it isn't a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"This isn't a ModShelf list: it isn't valid JSON ({exception.Message})", exception);
        }

        if (!string.Equals(Text(file["format"]), Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"This isn't a ModShelf list: its \"format\" isn't \"{Name}\".");
        }

        int version = Number(file["version"]) ?? throw new InvalidDataException("This ModShelf list doesn't say its version.");
        if (version > Version)
        {
            throw new InvalidDataException(
                $"This list was made by a newer ModShelf (list version {version}); update ModShelf to import it.");
        }

        if (version < 1)
        {
            throw new InvalidDataException($"This ModShelf list's version, {version}, isn't one there has been.");
        }

        DateTimeOffset createdAt = DateTimeOffset.TryParse(Text(file["createdAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at
            : now;
        List<ListedMod> mods = [];
        if (file["mods"] is JsonArray listed)
        {
            for (int index = 0; index < listed.Count; index++)
            {
                ListedMod mod = ModOf(listed[index], index + 1);
                int same = mods.FindIndex(earlier => earlier.Mod == mod.Mod);
                if (same < 0)
                {
                    mods.Add(mod);
                }
                else
                {
                    // Listed twice: one entry, with the files of both.
                    ListedMod earlier = mods[same];
                    mods[same] = earlier with { Files = [.. earlier.Files, .. mod.Files.Where(file => earlier.Files.All(kept => kept.File != file.File))] };
                }
            }
        }
        else if (file["mods"] is not null)
        {
            throw new InvalidDataException("This ModShelf list's \"mods\" isn't a list.");
        }

        return new ModList(Blank(Text(file["title"])), createdAt, mods);
    }

    private static JsonObject ModNode(ListedMod mod)
    {
        JsonObject node = new()
        {
            ["site"] = mod.Mod.Game.Source.Value,
            ["game"] = mod.Mod.Game.Key,
            ["mod"] = mod.Mod.Id,
            ["name"] = mod.Name,
        };
        if (mod.Author is { } author)
        {
            node["author"] = author;
        }

        if (mod.PageUrl is { } page)
        {
            node["page"] = page.AbsoluteUri;
        }

        node["watched"] = mod.Watched;
        if (mod.Pinned)
        {
            node["pinned"] = true;
        }

        if (mod.Note is { } note)
        {
            node["note"] = note;
        }

        JsonArray files = [];
        foreach (ListedFile file in mod.Files)
        {
            JsonObject fileNode = new()
            {
                ["file"] = file.File.Id,
                ["name"] = file.Name,
            };
            if (file.Version is { } version)
            {
                fileNode["version"] = version;
            }

            fileNode["category"] = NameOf(file.Category);
            if (file.Optional)
            {
                fileNode["optional"] = true;
            }

            files.Add(fileNode);
        }

        node["files"] = files;
        return node;
    }

    private static ListedMod ModOf(JsonNode? node, int number)
    {
        if (node is not JsonObject mod)
        {
            throw new InvalidDataException($"Mod {number} of the list isn't an object.");
        }

        ModRef modRef;
        try
        {
            modRef = new ModRef(
                new GameRef(new SourceId(Required(mod, "site", number)), Required(mod, "game", number)),
                Required(mod, "mod", number));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Mod {number} of the list can't be read: {exception.Message}", exception);
        }

        List<ListedFile> files = [];
        if (mod["files"] is JsonArray listed)
        {
            for (int index = 0; index < listed.Count; index++)
            {
                ListedFile file = FileOf(listed[index], modRef, number, index + 1);
                if (files.All(earlier => earlier.File != file.File))
                {
                    files.Add(file);
                }
            }
        }

        Uri? page = Uri.TryCreate(Text(mod["page"]), UriKind.Absolute, out Uri? link) && link.Scheme is "https" or "http" ? link : null;
        return new ListedMod(
            modRef,
            Blank(Text(mod["name"])) ?? modRef.Id,
            Blank(Text(mod["author"])),
            page,
            Flag(mod["watched"]) ?? true,
            Flag(mod["pinned"]) ?? false,
            Blank(Text(mod["note"])),
            files);
    }

    private static ListedFile FileOf(JsonNode? node, ModRef mod, int modNumber, int number)
    {
        if (node is not JsonObject file || Blank(Text(file["file"])) is not { } id)
        {
            throw new InvalidDataException($"File {number} of mod {modNumber} of the list doesn't say which file it is.");
        }

        return new ListedFile(
            new ModFileRef(mod, id),
            Blank(Text(file["name"])) ?? id,
            Blank(Text(file["version"])),
            CategoryOf(Text(file["category"])),
            Flag(file["optional"]) ?? false);
    }

    private static string Required(JsonObject node, string name, int number)
        => Blank(Text(node[name])) ?? throw new InvalidDataException($"Mod {number} of the list doesn't say its \"{name}\".");

    private static string NameOf(ModFileCategory category)
        => Categories.FirstOrDefault(known => known.Category == category).Name ?? "unknown";

    private static ModFileCategory CategoryOf(string? name)
    {
        foreach ((ModFileCategory category, string known) in Categories)
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return ModFileCategory.Unknown;
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out string? text) ? text
            : node is JsonValue number && number.TryGetValue(out long whole) ? whole.ToString(CultureInfo.InvariantCulture)
            : null;

    private static int? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int number) ? number : null;

    private static bool? Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
