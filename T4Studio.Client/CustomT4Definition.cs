using ICSharpCode.AvalonEdit.Highlighting;

namespace T4Studio.Client;

public class CustomT4Definition : IHighlightingDefinition
{
    private readonly IHighlightingDefinition _baseDef;
    public string Name { get; }
    public HighlightingRuleSet MainRuleSet { get; }

    public CustomT4Definition(string name, HighlightingRuleSet ruleSet, IHighlightingDefinition baseDef)
    {
        Name = name;
        MainRuleSet = ruleSet;
        _baseDef = baseDef;
    }

    public HighlightingRuleSet? GetNamedRuleSet(string name)
    {
        if (string.IsNullOrEmpty(name))
            return MainRuleSet;

        return _baseDef?.GetNamedRuleSet(name);
    }

    public HighlightingColor? GetNamedColor(string name) => _baseDef?.GetNamedColor(name);

    public IEnumerable<HighlightingColor> NamedHighlightingColors
        => _baseDef?.NamedHighlightingColors ?? Enumerable.Empty<HighlightingColor>();

    public IDictionary<string, string> Properties
        => _baseDef?.Properties ?? new Dictionary<string, string>();
}
