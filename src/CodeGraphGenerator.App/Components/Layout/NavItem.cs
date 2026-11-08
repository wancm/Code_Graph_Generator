namespace CodeGraphGenerator.App.Components.Layout;

/// <summary>One sidebar entry: a section title or a link.</summary>
public sealed record NavItem(string Text, string? Href = null, string? Icon = null, bool IsTitle = false)
{
    public static NavItem Title(string text) => new(text, IsTitle: true);

    public static NavItem Link(string text, string href, string? icon = null) => new(text, href, icon);
}
