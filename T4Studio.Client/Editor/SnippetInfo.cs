using MaterialDesignThemes.Wpf;

namespace T4Studio.Client.Editor;

public class SnippetInfo
{
    public string Tag { get; set; }
    public string Display { get; set; }
    public PackIconKind Icon { get; set; }

    public SnippetInfo(string tag, string display, PackIconKind icon)
    {
        Tag = tag;
        Display = display;
        Icon = icon;
    }
}
