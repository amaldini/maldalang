// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Message catalogs for <c>ui.messages</c> / <c>ui.t</c>. The JavaScript runtime's <c>ui</c> area
/// has the same rules, so a program prints the same text on every backend: lookup in the current
/// locale, then its language (<c>it-IT</c> → <c>it</c>), then the key itself; plural forms chosen
/// by <c>args.count</c> with an exact <c>"=N"</c> form first, then the CLDR category of the
/// language (<see cref="PluralCategory"/>); <c>{name}</c> placeholders filled from <c>args</c>,
/// numbers with the locale's separators, grouped by thousands and at most three decimals.
/// </summary>
public static class UiMessages
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<string, RuntimeValue>> Catalogs = new(StringComparer.OrdinalIgnoreCase);
    private static string _locale = "en";
    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static string CurrentLocale
    {
        get
        {
            lock (Gate)
                return _locale;
        }
    }

    /// <summary>Makes <paramref name="locale"/> current, first replacing its catalog when one is given.</summary>
    public static void Use(string locale, IEnumerable<KeyValuePair<string, RuntimeValue>>? catalog)
    {
        lock (Gate)
        {
            if (catalog != null)
                Catalogs[locale] = catalog.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            _locale = locale;
        }
    }

    /// <summary>Forgets every catalog and goes back to <c>en</c> (tests).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Catalogs.Clear();
            _locale = "en";
        }
    }

    public static string Translate(string key, IReadOnlyDictionary<string, RuntimeValue>? args)
    {
        string locale;
        RuntimeValue? entry = null;
        lock (Gate)
        {
            locale = _locale;
            foreach (var candidate in new[] { locale, Language(locale) })
            {
                if (Catalogs.TryGetValue(candidate, out var catalog) && catalog.TryGetValue(key, out var found) && found.Type != ValueType.Null)
                {
                    entry = found;
                    break;
                }
            }
        }

        var text = entry == null ? key : SelectForm(entry, key, locale, args);
        return Placeholder.Replace(text, match =>
            args != null && args.TryGetValue(match.Groups[1].Value, out var value) ? Format(value, locale) : match.Value);
    }

    private static string SelectForm(RuntimeValue entry, string key, string locale, IReadOnlyDictionary<string, RuntimeValue>? args)
    {
        if (entry.Type == ValueType.String)
            return entry.AsString();

        var forms = Members(entry);
        if (forms == null)
            return entry.ToString();

        double? count = null;
        if (args != null && args.TryGetValue("count", out var countValue))
            count = countValue.Type switch
            {
                ValueType.Integer => countValue.AsInteger(),
                ValueType.Float => countValue.AsFloat(),
                _ => null
            };

        string? Form(string name) =>
            forms.TryGetValue(name, out var form) && form.Type == ValueType.String ? form.AsString() : null;

        if (count is { } n)
        {
            if (Form("=" + FormatInvariant(n)) is { } exact)
                return exact;
            if (Form(PluralCategory(locale, n)) is { } category)
                return category;
        }

        return Form("other") ?? key;
    }

    private static Dictionary<string, RuntimeValue>? Members(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
            return null;
        return value.AsObject() switch
        {
            JsonObject obj => obj.GetProperties().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            DictionaryInstance dict => dict.GetEntries().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            _ => null
        };
    }

    private static string Language(string locale)
    {
        var dash = locale.IndexOfAny(new[] { '-', '_' });
        return dash > 0 ? locale[..dash] : locale;
    }

    /// <summary>
    /// The CLDR cardinal plural category of <paramref name="n"/>: <c>zero</c>, <c>one</c>, <c>two</c>,
    /// <c>few</c>, <c>many</c> or <c>other</c>, for the languages in the table; any other language
    /// uses the English rule.
    /// </summary>
    public static string PluralCategory(string locale, double n)
    {
        var language = Language(locale).ToLowerInvariant();
        var absolute = Math.Abs(n);
        var isInteger = absolute == Math.Floor(absolute);
        var i = (long)Math.Floor(absolute);
        var mod10 = i % 10;
        var mod100 = i % 100;

        switch (language)
        {
            case "ja" or "zh" or "ko" or "th" or "vi" or "id" or "ms":
                return "other";
            case "fr":
                return i is 0 or 1 ? "one" : "other";
            case "pt" when !locale.Equals("pt-PT", StringComparison.OrdinalIgnoreCase):
                return i is 0 or 1 ? "one" : "other";
            case "ru" or "uk" or "be":
                if (!isInteger)
                    return "other";
                if (mod10 == 1 && mod100 != 11)
                    return "one";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
                    return "few";
                return "many";
            case "pl":
                if (!isInteger)
                    return "other";
                if (i == 1)
                    return "one";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
                    return "few";
                return "many";
            case "cs" or "sk":
                if (!isInteger)
                    return "many";
                if (i == 1)
                    return "one";
                return i is >= 2 and <= 4 ? "few" : "other";
            default:
                return isInteger && i == 1 ? "one" : "other";
        }
    }

    /// <summary>
    /// A placeholder value: numbers with the locale's group and decimal separators (grouped by three,
    /// at most three decimals, rounded half away from zero), anything else as <c>io.print</c> shows it.
    /// </summary>
    public static string Format(RuntimeValue value, string locale)
    {
        double number;
        switch (value.Type)
        {
            case ValueType.Integer:
                number = value.AsInteger();
                break;
            case ValueType.Float:
                number = value.AsFloat();
                break;
            default:
                return value.ToString();
        }

        if (double.IsNaN(number) || double.IsInfinity(number))
            return value.ToString();

        var (group, decimalSeparator) = Separators(locale);
        var rounded = Math.Round(Math.Abs(number), 3, MidpointRounding.AwayFromZero);
        var digits = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        var dot = digits.IndexOf('.');
        var whole = dot < 0 ? digits : digits[..dot];
        var builder = new StringBuilder();
        if (number < 0 && rounded != 0)
            builder.Append('-');
        for (var index = 0; index < whole.Length; index++)
        {
            if (index > 0 && (whole.Length - index) % 3 == 0)
                builder.Append(group);
            builder.Append(whole[index]);
        }
        if (dot >= 0)
            builder.Append(decimalSeparator).Append(digits[(dot + 1)..]);
        return builder.ToString();
    }

    private static (string Group, string Decimal) Separators(string locale)
    {
        try
        {
            var format = CultureInfo.GetCultureInfo(locale).NumberFormat;
            return (format.NumberGroupSeparator, format.NumberDecimalSeparator);
        }
        catch (CultureNotFoundException)
        {
            return (",", ".");
        }
    }

    private static string FormatInvariant(double n) =>
        n == Math.Floor(n) && Math.Abs(n) < 1e15 ? ((long)n).ToString(CultureInfo.InvariantCulture) : n.ToString("R", CultureInfo.InvariantCulture);
}
