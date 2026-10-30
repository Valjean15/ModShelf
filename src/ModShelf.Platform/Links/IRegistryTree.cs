namespace ModShelf.Platform.Links;

/// <summary>
/// A tree of registry keys, such as the current user's hive, by paths of key names separated by backslashes.
/// </summary>
internal interface IRegistryTree
{
    bool Exists(string path);

    /// <summary>
    /// A string value, or null when the key or the value is missing, or isn't a string.
    /// </summary>
    /// <param name="name">The value's name; null for the key's default value.</param>
    string? GetString(string path, string? name);

    /// <summary>
    /// Sets a string value, creating the key and its parents as needed.
    /// </summary>
    /// <param name="name">The value's name; null for the key's default value.</param>
    void SetString(string path, string? name, string value);

    /// <summary>
    /// Deletes a value; nothing when the key or the value is missing.
    /// </summary>
    void DeleteValue(string path, string name);

    /// <summary>
    /// Copies the key at <paramref name="from"/>, its values and its subkeys, to <paramref name="to"/>, which must not
    /// exist yet.
    /// </summary>
    void CopyTree(string from, string to);

    /// <summary>
    /// Deletes the key and everything under it; nothing when it's missing.
    /// </summary>
    void DeleteTree(string path);

    /// <summary>
    /// Deletes the key when it has neither values nor subkeys.
    /// </summary>
    void DeleteIfEmpty(string path);
}
