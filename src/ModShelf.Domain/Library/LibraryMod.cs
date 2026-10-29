using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Library;

/// <summary>
/// A mod in the library: one the user watches for updates, has downloaded files of, or both.
/// </summary>
/// <remarks>
/// Immutable: every change returns a new instance, so whoever holds one sees it as it was read. A change that leaves
/// the mod neither watched nor with files returns null, since nothing keeps it in the library any more.
/// <para/>
/// A watched mod also has the updates its last check found: newer files of the mod that the library doesn't have. One
/// leaves when its file comes into the library, and they all leave when the mod is unwatched.
/// </remarks>
public sealed class LibraryMod
{
    private LibraryMod(
        ModRef mod,
        string name,
        string? author,
        Uri? pageUrl,
        bool isWatched,
        DateTimeOffset addedAt,
        DateTimeOffset? checkedAt,
        LibraryFile[] files,
        LibraryUpdate[] updates,
        bool needsFullCheck)
    {
        Mod = mod;
        Name = name;
        Author = author;
        PageUrl = pageUrl;
        IsWatched = isWatched;
        AddedAt = addedAt;
        CheckedAt = checkedAt;
        Files = Array.AsReadOnly(files);
        Updates = Array.AsReadOnly(updates);
        NeedsFullCheck = needsFullCheck;
    }

    public ModRef Mod { get; }

    /// <summary>
    /// The mod's name on its site, as it was when last read.
    /// </summary>
    public string Name { get; }

    public string? Author { get; }

    /// <summary>
    /// The mod's page on its site.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// Whether the mod is checked for updates.
    /// </summary>
    public bool IsWatched { get; }

    public DateTimeOffset AddedAt { get; }

    /// <summary>
    /// When the mod was last checked for updates, or null if it never was.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; }

    /// <summary>
    /// The mod's files in the library, in the order they were added. Empty for a mod that's only watched.
    /// </summary>
    public IReadOnlyList<LibraryFile> Files { get; }

    /// <summary>
    /// The updates checks found, the newest first, as they were given. Empty for a mod that isn't watched.
    /// </summary>
    public IReadOnlyList<LibraryUpdate> Updates { get; }

    /// <summary>
    /// Whether the library's side of the mod changed since its last check, so the next check asks its site about it
    /// rather than going by the site's feed of changed mods: it was watched again, or a file came or went. The feed says
    /// what changed on the site, so it would leave the mod's updates as the change left them.
    /// </summary>
    public bool NeedsFullCheck { get; }

    /// <summary>
    /// A mod the user starts watching, without any files yet.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    public static LibraryMod Watch(ModRef mod, string name, string? author, Uri? pageUrl, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(mod);
        EnsureDetails(name, pageUrl);

        return new LibraryMod(mod, name, Blank(author), pageUrl, isWatched: true, addedAt, checkedAt: null, [], [], needsFullCheck: false);
    }

    /// <summary>
    /// Rebuilds a mod that was stored earlier, such as one loaded from the library database.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The details are invalid; a file isn't the mod's, or two files are the same file or at the same path; the mod is
    /// neither watched nor has files; or an update isn't one the mod can have.
    /// </exception>
    public static LibraryMod Restore(
        ModRef mod,
        string name,
        string? author,
        Uri? pageUrl,
        bool isWatched,
        DateTimeOffset addedAt,
        DateTimeOffset? checkedAt,
        IEnumerable<LibraryFile> files,
        IEnumerable<LibraryUpdate>? updates = null,
        bool needsFullCheck = false)
    {
        ArgumentNullException.ThrowIfNull(mod);
        EnsureDetails(name, pageUrl);
        ArgumentNullException.ThrowIfNull(files);

        LibraryFile[] fileList = [.. files];
        for (int index = 0; index < fileList.Length; index++)
        {
            EnsureFits(mod, fileList[index], fileList.Take(index), nameof(files));
        }

        if (!isWatched && fileList.Length == 0)
        {
            throw new ArgumentException("A library mod is watched, has files, or both.", nameof(files));
        }

        LibraryUpdate[] updateList = [.. updates ?? []];
        EnsureUpdates(mod, isWatched, fileList, updateList, nameof(updates));
        return new LibraryMod(mod, name, Blank(author), pageUrl, isWatched, addedAt, checkedAt, fileList, updateList, needsFullCheck);
    }

    /// <summary>
    /// The mod, watched for updates. A mod watched again needs a full check, since its updates went when it was
    /// unwatched.
    /// </summary>
    public LibraryMod Watched() => IsWatched ? this : With(isWatched: true, needsFullCheck: true);

    /// <summary>
    /// The mod, no longer watched and without updates; or null when it has no files, so nothing keeps it in the library.
    /// </summary>
    public LibraryMod? Unwatched()
        => Files.Count == 0 ? null : new LibraryMod(Mod, Name, Author, PageUrl, isWatched: false, AddedAt, CheckedAt, [.. Files], [], NeedsFullCheck);

    /// <summary>
    /// The mod with its details as its site gives them now.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    public LibraryMod WithDetails(string name, string? author, Uri? pageUrl)
    {
        EnsureDetails(name, pageUrl);

        return new LibraryMod(Mod, name, Blank(author), pageUrl, IsWatched, AddedAt, CheckedAt, [.. Files], [.. Updates], NeedsFullCheck);
    }

    /// <summary>
    /// The mod, last checked for updates at <paramref name="at"/>, with no full check needed.
    /// </summary>
    public LibraryMod Checked(DateTimeOffset at) => new(Mod, Name, Author, PageUrl, IsWatched, AddedAt, at, [.. Files], [.. Updates], needsFullCheck: false);

    /// <summary>
    /// The mod with the updates a check found, in place of the ones it had. An update of a file it had already keeps
    /// when it was first found.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The mod isn't watched; or an update is of another mod, of a file the library has, of a file it doesn't have, or
    /// listed twice.
    /// </exception>
    public LibraryMod WithUpdates(IEnumerable<LibraryUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        LibraryUpdate[] updateList = [.. updates];
        for (int index = 0; index < updateList.Length; index++)
        {
            if (updateList[index] is { } update && Updates.FirstOrDefault(found => found.File == update.File) is { } earlier)
            {
                updateList[index] = update.FoundSince(earlier.FoundAt);
            }
        }

        EnsureUpdates(Mod, IsWatched, [.. Files], updateList, nameof(updates));
        return new LibraryMod(Mod, Name, Author, PageUrl, IsWatched, AddedAt, CheckedAt, [.. Files], updateList, NeedsFullCheck);
    }

    /// <summary>
    /// The mod with <paramref name="file"/> in the library, in place of the same file if it was there already. An update
    /// to that file is no longer one.
    /// </summary>
    /// <exception cref="ArgumentException">The file isn't the mod's, or another of its files is at the same path.</exception>
    public LibraryMod WithFile(LibraryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        EnsureFits(Mod, file, Files.Where(other => other.File != file.File), nameof(file));

        int index = Files.ToList().FindIndex(other => other.File == file.File);
        LibraryFile[] files = index < 0 ? [.. Files, file] : [.. Files.Take(index), file, .. Files.Skip(index + 1)];
        // Another file, unlike the same one moved, may have updates of its own, or be older than the ones found.
        return new LibraryMod(
            Mod, Name, Author, PageUrl, IsWatched, AddedAt, CheckedAt, files, [.. Updates.Where(update => update.File != file.File)], NeedsFullCheck || index < 0);
    }

    /// <summary>
    /// The mod without <paramref name="file"/>; or null when that leaves it unwatched and without files.
    /// </summary>
    public LibraryMod? WithoutFile(ModFileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);

        LibraryFile[] files = [.. Files.Where(other => other.File != file)];
        return !IsWatched && files.Length == 0
            ? null
            : new LibraryMod(
                Mod, Name, Author, PageUrl, IsWatched, AddedAt, CheckedAt, files, [.. Updates.Where(update => update.Replaces != file)], needsFullCheck: true);
    }

    private static void EnsureDetails(string name, Uri? pageUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (pageUrl is not null
            && !(pageUrl.IsAbsoluteUri && (pageUrl.Scheme == Uri.UriSchemeHttps || pageUrl.Scheme == Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("A mod's page must be an absolute HTTP or HTTPS URL.", nameof(pageUrl));
        }
    }

    /// <summary>
    /// Throws unless <paramref name="file"/> is the mod's, and neither the same file nor at the same path as any of
    /// <paramref name="others"/>. Paths that differ only in case count as the same, since they are on Windows and macOS.
    /// </summary>
    private static void EnsureFits(ModRef mod, LibraryFile file, IEnumerable<LibraryFile> others, string paramName)
    {
        if (file is null)
        {
            throw new ArgumentException("The files can't include null.", paramName);
        }

        if (file.File.Mod != mod)
        {
            throw new ArgumentException($"File {file.File.Id} belongs to mod {file.File.Mod.Id}, not {mod.Id}.", paramName);
        }

        foreach (LibraryFile other in others)
        {
            if (other.File == file.File)
            {
                throw new ArgumentException($"File {file.File.Id} is in the library twice.", paramName);
            }

            if (string.Equals(other.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Files {other.File.Id} and {file.File.Id} are both at {file.RelativePath}.", paramName);
            }
        }
    }

    /// <summary>
    /// Throws unless every update is of a watched mod, of the mod, of a file the library doesn't have, listed once, and
    /// replaces a file the library has, if any.
    /// </summary>
    private static void EnsureUpdates(ModRef mod, bool isWatched, LibraryFile[] files, LibraryUpdate[] updates, string paramName)
    {
        if (updates.Length > 0 && !isWatched)
        {
            throw new ArgumentException("Only a watched mod has updates.", paramName);
        }

        HashSet<ModFileRef> seen = [];
        foreach (LibraryUpdate update in updates)
        {
            if (update is null)
            {
                throw new ArgumentException("The updates can't include null.", paramName);
            }

            if (update.File.Mod != mod)
            {
                throw new ArgumentException($"Update {update.File.Id} is of mod {update.File.Mod.Id}, not {mod.Id}.", paramName);
            }

            if (Array.Exists(files, file => file.File == update.File))
            {
                throw new ArgumentException($"File {update.File.Id} is in the library already, so it isn't an update.", paramName);
            }

            if (update.Replaces is { } replaced && !Array.Exists(files, file => file.File == replaced))
            {
                throw new ArgumentException($"Update {update.File.Id} replaces file {replaced.Id}, which isn't in the library.", paramName);
            }

            if (!seen.Add(update.File))
            {
                throw new ArgumentException($"Update {update.File.Id} is listed twice.", paramName);
            }
        }
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private LibraryMod With(bool isWatched, bool needsFullCheck)
        => new(Mod, Name, Author, PageUrl, isWatched, AddedAt, CheckedAt, [.. Files], [.. Updates], needsFullCheck);
}
