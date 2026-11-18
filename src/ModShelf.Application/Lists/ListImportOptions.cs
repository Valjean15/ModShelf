namespace ModShelf.Application.Lists;

/// <summary>
/// How to import a list.
/// </summary>
/// <param name="Download">
/// Whether to download the listed files the library lacks; otherwise every listed mod is only watched.
/// </param>
/// <param name="IncludeOptional">Whether to download the files the list marks optional too.</param>
/// <param name="NewestForMissing">
/// Whether a listed file its site no longer offers is replaced by the mod's newest main file, rather than left out.
/// </param>
public sealed record ListImportOptions(bool Download = true, bool IncludeOptional = false, bool NewestForMissing = false);
