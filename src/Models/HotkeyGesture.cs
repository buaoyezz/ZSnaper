namespace ZSnaper.Models;

public enum HotkeyCommand
{
    Capture,
    Ocr
}

public readonly record struct HotkeyChangeResult(bool Success, string Message);

public readonly record struct HotkeyGesture(Keys KeyCode, Keys Modifiers)
{
    private const Keys SupportedModifiers = Keys.Control | Keys.Alt | Keys.Shift;

    public bool IsValid =>
        KeyCode != Keys.None &&
        !IsModifierKey(KeyCode) &&
        ((Modifiers & SupportedModifiers) != Keys.None || IsStandaloneKey(KeyCode));

    public bool IsValidForForceBinding =>
        KeyCode != Keys.None &&
        KeyCode != Keys.Escape &&
        !IsModifierKey(KeyCode);

    public string DisplayText => string.Join(" + ", GetParts());

    public string ConfigText => string.Join("+", GetParts());

    public static HotkeyGesture FromKeyEvent(KeyEventArgs e) =>
        new(e.KeyCode, e.Modifiers & SupportedModifiers);

    public static bool TryParse(
        string? value,
        out HotkeyGesture gesture,
        bool forceBinding = false)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Keys modifiers = Keys.None;
        Keys keyCode = Keys.None;
        foreach (string rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= Keys.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= Keys.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= Keys.Shift;
                continue;
            }

            if (!Enum.TryParse(part, ignoreCase: true, out Keys parsedKey))
            {
                return false;
            }

            keyCode = parsedKey & Keys.KeyCode;
        }

        gesture = new HotkeyGesture(keyCode, modifiers);
        return forceBinding ? gesture.IsValidForForceBinding : gesture.IsValid;
    }

    public static bool IsModifierKey(Keys keyCode) => keyCode is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    private static bool IsStandaloneKey(Keys keyCode) => keyCode == Keys.PrintScreen;

    private IEnumerable<string> GetParts()
    {
        if (Modifiers.HasFlag(Keys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(Keys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(Keys.Shift)) yield return "Shift";
        if (KeyCode != Keys.None) yield return GetKeyName(KeyCode);
    }

    private static string GetKeyName(Keys keyCode)
    {
        if (keyCode is >= Keys.D0 and <= Keys.D9)
        {
            return ((int)keyCode - (int)Keys.D0).ToString();
        }

        if (keyCode is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            return "Num " + ((int)keyCode - (int)Keys.NumPad0);
        }

        return keyCode switch
        {
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            _ => keyCode.ToString()
        };
    }
}
