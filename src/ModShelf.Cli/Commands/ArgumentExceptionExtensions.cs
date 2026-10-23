namespace ModShelf.Cli.Commands;

/// <summary>
/// Turns what the engine refused into a line the user can act on.
/// </summary>
internal static class ArgumentExceptionExtensions
{
    /// <summary>
    /// The exception's message without the "(Parameter 'name')" .NET appends, which means nothing to the user.
    /// </summary>
    public static string MessageForUser(this ArgumentException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ParamName is null
            ? exception.Message
            : exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);
    }
}
