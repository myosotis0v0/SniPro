namespace SniPro.Windows;

public readonly record struct GlobalHotkeyDefinition(
    uint Modifiers,
    uint VirtualKey,
    string CanonicalText);

public static class GlobalHotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWindows = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public static bool TryParse(
        string? text,
        out GlobalHotkeyDefinition definition,
        out string error)
    {
        definition = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a hotkey such as Ctrl+Shift+G.";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = "A hotkey must contain at least one modifier and one key.";
            return false;
        }

        uint modifiers = 0;
        string? keyToken = null;

        foreach (var token in tokens)
        {
            if (TryGetModifier(token, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"The modifier '{token}' is repeated.";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (keyToken is not null)
            {
                error = "Enter only one non-modifier key.";
                return false;
            }

            keyToken = token;
        }

        if (keyToken is null)
        {
            error = "Enter a key after the modifier(s).";
            return false;
        }

        if (!TryGetVirtualKey(keyToken, out var virtualKey, out var canonicalKey))
        {
            error = $"Unsupported key '{keyToken}'. Use A-Z, 0-9, or F1-F12.";
            return false;
        }

        definition = new GlobalHotkeyDefinition(
            modifiers,
            virtualKey,
            $"{FormatModifiers(modifiers)}{canonicalKey}");
        return true;
    }

    private static bool TryGetModifier(string token, out uint modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "ALT" => ModAlt,
            "CTRL" or "CONTROL" => ModControl,
            "SHIFT" => ModShift,
            "WIN" or "WINDOWS" or "META" => ModWindows,
            _ => 0
        };

        return modifier != 0;
    }

    private static bool TryGetVirtualKey(string token, out uint virtualKey, out string canonicalKey)
    {
        virtualKey = 0;
        canonicalKey = string.Empty;

        if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
        {
            var character = char.ToUpperInvariant(token[0]);
            virtualKey = character;
            canonicalKey = character.ToString();
            return true;
        }

        if (token.Length >= 2 && token[0] is 'F' or 'f' &&
            int.TryParse(token[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            virtualKey = (uint)(0x70 + functionNumber - 1);
            canonicalKey = $"F{functionNumber}";
            return true;
        }

        return false;
    }

    private static string FormatModifiers(uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWindows) != 0)
        {
            parts.Add("Win");
        }

        return string.Join('+', parts) + "+";
    }
}
