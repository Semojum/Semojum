using Semojum.Feature.Editor;

internal static class MathTemplateChecks
{
    internal static Task RunAsync()
    {
        var templates = MathTemplates.Groups.SelectMany(group => group.Templates).ToArray();
        var fraction = templates.Single(t => t.Label == "분수");
        Equal(new MathInsertion(@"\frac{a}{b}", 6, 1), fraction.Insert());
        Equal(new MathInsertion(@"\frac{x+1}{b}", 11, 1), fraction.Insert("x+1"));
        var nested = fraction.Insert(@"\sqrt{x}");
        Equal(@"\frac{\sqrt{x}}{b}", nested.Text);
        Equal("b", nested.Text.Substring(nested.SelectionStart, nested.SelectionLength));
        var power = templates.Single(t => t.Label == "거듭제곱").Insert("a+b");
        Equal("{a+b}^{2}", power.Text);
        Equal("2", power.Text.Substring(power.SelectionStart, power.SelectionLength));
        var root = templates.Single(t => t.Label == "제곱근").Insert("가😀");
        Equal(new MathInsertion(@"\sqrt{가😀}", 10, 0), root);
        var pi = templates.Single(t => t.Label == "파이").Insert();
        Equal(@"\pi x", pi.Text + "x");
        foreach (var template in templates)
        {
            foreach (var selection in new[] { "", "x+1" })
            {
                var insertion = template.Insert(selection);
                if (insertion.Text.Contains('«') || insertion.Text.Contains('»') || insertion.SelectionStart < 0 ||
                    insertion.SelectionStart + insertion.SelectionLength > insertion.Text.Length)
                    throw new Exception($"Invalid insertion for {template.Label}");
            }
        }
        return Task.CompletedTask;
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
    }
}
