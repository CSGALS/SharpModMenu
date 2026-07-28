using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

namespace SharpModMenu;

internal sealed class SharpModMenuPluginConfig
{
	public bool Debug { get; set; }
	public float DefaultMenuPositionX { get; set; } = -6.55f;
	public float DefaultMenuPositionY { get; set; } = 0.0f;
	public string WorldTextFontName { get; set; } = "Tahoma Bold";
	public int WorldTextFontSize { get; set; } = 25;
	public string WorldTextHighlightColor { get; set; } = "#7FFFFF40";
	public string WorldTextForegroundColor { get; set; } = "#E79827";
	public string WorldTextBackgroundTextColor { get; set; } = "#EAD1AF";
	public string WorldTextPanelColor { get; set; } = "#C87F7F7F";
	public string HtmlForegroundHighlightColor { get; set; } = "#EFCE21";
	public string HtmlForegroundColor { get; set; } = "#E28B12";
	public string HtmlBackgroundHighlightColor { get; set; } = "#EFE472";
	public string HtmlBackgroundColor { get; set; } = "#E7CCA5";
	public string HtmlEmptyLineColor { get; set; } = "#000";
	public bool FreezePlayerMovement { get; set; } = true;
	public string SelectorIcon { get; set; } = "\u279C";
	public string SelectorIconColor { get; set; } = "#EFCE21";
	public string HtmlFontClass { get; set; } = "fontSize-s";
	public string HtmlCompactFooterMessage { get; set; } = string.Empty;
	public string HtmlCompactFooterFontClass { get; set; } = string.Empty;
	public bool WasdAutoPage { get; set; } = true;
	public bool ForceFallbackInput { get; set; }
}

internal sealed class SharpModMenuRuntimeOptions
{
	public static SharpModMenuRuntimeOptions Default { get; } = new();

	public bool Debug { get; set; }
	public Vector2 DefaultMenuPosition { get; set; } = new(-6.55f, 0.0f);
	public string WorldTextFontName { get; set; } = "Tahoma Bold";
	public int WorldTextFontSize { get; set; } = 25;
	public Color WorldTextHighlightColor { get; set; } = Color.FromArgb(127, 255, 255, 64);
	public Color WorldTextForegroundColor { get; set; } = Color.FromArgb(231, 152, 39);
	public Color WorldTextBackgroundTextColor { get; set; } = Color.FromArgb(234, 209, 175);
	public Color WorldTextPanelColor { get; set; } = Color.FromArgb(200, 127, 127, 127);
	public string HtmlForegroundHighlightColor { get; set; } = "#EFCE21";
	public string HtmlForegroundColor { get; set; } = "#E28B12";
	public string HtmlBackgroundHighlightColor { get; set; } = "#EFE472";
	public string HtmlBackgroundColor { get; set; } = "#E7CCA5";
	public string HtmlEmptyLineColor { get; set; } = "#000";
	public bool FreezePlayerMovement { get; set; } = true;
	public string SelectorIcon { get; set; } = "\u279C";
	public string SelectorIconColor { get; set; } = "#EFCE21";
	public string HtmlFontClass { get; set; } = "fontSize-s";
	public string HtmlCompactFooterMessage { get; set; } = string.Empty;
	public string HtmlCompactFooterFontClass { get; set; } = string.Empty;
	public bool WasdAutoPage { get; set; } = true;
}

internal sealed class SharpModMenuTranslationFile
{
	public string DefaultLanguage { get; set; } = "en";
	public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SharpModMenuTranslations
{
	public static SharpModMenuTranslations Default { get; } = new(
		"en",
		new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
		{
			["en"] = new(StringComparer.OrdinalIgnoreCase)
			{
				["nav.previous"] = "Previous",
				["nav.back"] = "Back",
				["nav.next"] = "Next",
				["nav.close"] = "Close",
				["nav.exit"] = "Close",
				["nav.compact_message"] = "Use 8 Previous | 9 Next | 0 Close",
			},
			["fr"] = new(StringComparer.OrdinalIgnoreCase)
			{
				["nav.previous"] = "Precedent",
				["nav.back"] = "Retour",
				["nav.next"] = "Suivant",
				["nav.close"] = "Fermer",
				["nav.exit"] = "Fermer",
				["nav.compact_message"] = "Utilise 8 Precedent | 9 Suivant | 0 Fermer",
			},
		});

	private readonly Dictionary<string, Dictionary<string, string>> _languages;
	private readonly string _defaultLanguage;

	public SharpModMenuTranslations(
		string defaultLanguage,
		Dictionary<string, Dictionary<string, string>> languages)
	{
		_languages = languages;
		_defaultLanguage = NormalizeLanguage(defaultLanguage) ?? "en";
	}

	public string Translate(CCSPlayerController? player, string key, string fallback)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return fallback;
		}

		CultureInfo? culture = null;
		if (player is { IsValid: true })
		{
			culture = player.GetLanguage();
		}

		var cultureName = NormalizeLanguage(culture?.Name);
		if (TryGet(cultureName, key, out var value))
		{
			return value;
		}

		var twoLetter = NormalizeLanguage(culture?.TwoLetterISOLanguageName);
		if (TryGet(twoLetter, key, out value))
		{
			return value;
		}

		if (TryGet(_defaultLanguage, key, out value))
		{
			return value;
		}

		if (TryGet("en", key, out value))
		{
			return value;
		}

		return fallback;
	}

	private bool TryGet(string? languageTag, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrWhiteSpace(languageTag))
		{
			return false;
		}

		if (!_languages.TryGetValue(languageTag, out var languageEntries))
		{
			return false;
		}

		if (!languageEntries.TryGetValue(key, out var found))
		{
			return false;
		}

		value = found;
		return true;
	}

	private static string? NormalizeLanguage(string? language)
	{
		if (string.IsNullOrWhiteSpace(language))
		{
			return null;
		}

		return language.Trim().Replace('_', '-').ToLowerInvariant();
	}
}
