using System.Globalization;

namespace PlunderCount;

/// <summary>
/// Text helpers. Plunder Count is English only.
/// Names stored in data.json (chest kinds, treasure items) keep their original French keys so existing
/// data files stay compatible; <see cref="Name"/> turns them into English for display.
/// </summary>
public static class L
{
    public const string Code = "en";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-GB");

    public static string N(int n) => n.ToString("N0", Culture);

    /// <summary>"1 chest" / "3 chests" (regular plural: + "s").</summary>
    public static string Count(int n, string one) => Count(n, one, one + "s");

    public static string Count(int n, string one, string many) => $"{N(n)} {(n == 1 ? one : many)}";

    /// <summary>"1st", "12th", "22nd".</summary>
    public static string Ordinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    /// <summary>Short date + time of a match ("5 Nov 22:54").</summary>
    public static string DateTimeShort(DateTime d) => d.ToString("d MMM HH:mm", Culture);

    // Keys stored in data.json -> English display names.
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffre"] = "Chest",
        ["Coffre rare"] = "Rare chest",
        ["Coffre enterré"] = "Buried chest",
        ["Coffre d'esprit (rare)"] = "Sprite chest (rare)",
        ["Coffre des affiches d'esprit"] = "Sprite poster chest",
        ["Autre coffre"] = "Other chest",
        ["Canon à butin"] = "Loot cannon",
        ["Caisse de munitions"] = "Ammo box",
        ["Caisse de nourriture"] = "Food box",
        ["Autre conteneur"] = "Other container",
        ["Trésor"] = "Treasure",
        ["Trésor (type inconnu)"] = "Treasure gizmo",
        ["Gizmo de trésor"] = "Treasure gizmo",
        ["Feu d'artifice doré"] = "Golden firework",
        ["Extracteur portable"] = "Portable extractor",
        ["Localisateur d'esprit"] = "Sprite locator",
        ["Localisateur chanceux"] = "Lucky locator",
        ["Accélérateur d'extraction"] = "Extraction accelerator",
        ["inconnu"] = "unknown",
    };

    /// <summary>English display name of a chest kind or treasure item.</summary>
    public static string Name(string? stored)
    {
        if (stored != null)
        {
            stored = ChestKinds.Canonical(stored); // older name of a kind
        }
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }
        return Names.TryGetValue(stored, out var en) ? en : stored;
    }
}
