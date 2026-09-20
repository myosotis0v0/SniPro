using System.Globalization;
using System.Windows;
using SniPro.Core;
using SniPro.Windows;
using WpfApplication = System.Windows.Application;

namespace SniPro.App;

public sealed class LocalizationService
{
    private ResourceDictionary? _activeDictionary;

    public string RequestedLanguageCode { get; private set; } = LanguageCodes.System;

    public string EffectiveLanguageCode { get; private set; } = LanguageCodes.English;

    public event EventHandler? LanguageChanged;

    public void Apply(string? languageCode)
    {
        var requested = string.IsNullOrWhiteSpace(languageCode)
            ? LanguageCodes.System
            : languageCode.Trim();
        var candidates = GetCandidates(requested).ToList();
        ResourceDictionary? dictionary = null;
        string? effectiveCode = null;

        foreach (var candidate in candidates)
        {
            dictionary = TryLoadDictionary(candidate);
            if (dictionary is not null)
            {
                effectiveCode = candidate;
                break;
            }
        }

        if (dictionary is null)
        {
            throw new InvalidOperationException("The default English localization resource is unavailable.");
        }

        effectiveCode ??= LanguageCodes.English;

        var mergedDictionaries = WpfApplication.Current.Resources.MergedDictionaries;
        if (_activeDictionary is not null)
        {
            mergedDictionaries.Remove(_activeDictionary);
        }

        mergedDictionaries.Add(dictionary);
        _activeDictionary = dictionary;
        RequestedLanguageCode = requested;
        EffectiveLanguageCode = effectiveCode;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        return WpfApplication.Current.TryFindResource(key) as string ?? key;
    }

    public string Format(string key, params object[] arguments)
    {
        return string.Format(
            CultureInfo.GetCultureInfo(EffectiveLanguageCode),
            Get(key),
            arguments);
    }

    public string GetHotkeyError(GlobalHotkeyParseError error)
    {
        var key = error switch
        {
            GlobalHotkeyParseError.Empty => "HotkeyErrorEmpty",
            GlobalHotkeyParseError.MissingModifier => "HotkeyErrorMissingModifier",
            GlobalHotkeyParseError.DuplicateModifier => "HotkeyErrorDuplicateModifier",
            GlobalHotkeyParseError.MultipleKeys => "HotkeyErrorMultipleKeys",
            GlobalHotkeyParseError.MissingKey => "HotkeyErrorMissingKey",
            GlobalHotkeyParseError.UnsupportedKey => "HotkeyErrorUnsupportedKey",
            _ => "HotkeyErrorUnsupportedKey"
        };

        return Get(key);
    }

    private static IReadOnlyList<string> GetCandidates(string requested)
    {
        var candidates = new List<string>();
        var effectiveRequested = requested.Equals(LanguageCodes.System, StringComparison.OrdinalIgnoreCase)
            ? GetSystemLanguageCode()
            : requested;

        if (!IsSafeLanguageCode(effectiveRequested))
        {
            return new[] { LanguageCodes.English };
        }

        candidates.Add(effectiveRequested);

        try
        {
            var culture = CultureInfo.GetCultureInfo(effectiveRequested);
            if (!string.Equals(culture.Name, effectiveRequested, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(culture.Name);
            }

            if (!string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
            {
                candidates.Add(culture.TwoLetterISOLanguageName);
            }
        }
        catch (CultureNotFoundException)
        {
        }

        if (!effectiveRequested.Equals(LanguageCodes.English, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(LanguageCodes.English);
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetSystemLanguageCode()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase)
            ? LanguageCodes.SimplifiedChinese
            : LanguageCodes.English;
    }

    private static bool IsSafeLanguageCode(string languageCode)
    {
        return languageCode.Length > 0 &&
               languageCode.All(character =>
                   char.IsLetterOrDigit(character) || character is '-' or '_');
    }

    private static ResourceDictionary? TryLoadDictionary(string languageCode)
    {
        try
        {
            var source = new Uri(
                $"/SniPro;component/Localization/Strings.{languageCode}.xaml",
                UriKind.Relative);
            return new ResourceDictionary { Source = source };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
