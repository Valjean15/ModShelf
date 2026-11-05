namespace ModShelf.Cli.Mcp;

/// <summary>
/// Marks a tool's argument as one of the <see cref="Deprecations"/>, by its code, so its input schema says
/// <c>"deprecated": true</c>.
/// </summary>
/// <param name="code">The deprecation's code, such as <c>DL_LINK_ARG</c>.</param>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class DeprecatedAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}
