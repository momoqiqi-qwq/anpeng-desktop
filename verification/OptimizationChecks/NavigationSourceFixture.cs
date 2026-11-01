using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anpeng.Desktop;

// The navigation arrays live in App.xaml.cs alongside WinUI startup. Read their actual
// initializers for SelfTest's D-6 check without starting App or substituting expected values.
internal static class NavMap
{
    public static string[] Order { get; private set; } = [];
    public static string[] Titles { get; private set; } = [];

    public static void ReadApplicationArrays(string path)
    {
        var source = File.ReadAllText(path);
        Order = ReadArray(source, nameof(Order));
        Titles = ReadArray(source, nameof(Titles));
    }

    private static string[] ReadArray(string source, string name)
    {
        var match = Regex.Match(source, @"public\s+static\s+readonly\s+string\[\]\s+" + name + @"\s*=\s*\{(?<items>[^}]+)\};");
        if (!match.Success) throw new InvalidOperationException($"Navigation initializer changed; update harness extraction for {name}");
        return JsonSerializer.Deserialize<string[]>("[" + match.Groups["items"].Value + "]", new JsonSerializerOptions { AllowTrailingCommas = true })
            ?? throw new InvalidOperationException($"Cannot parse actual navigation array {name}");
    }
}
