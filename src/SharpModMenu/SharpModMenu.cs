using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

using CSSUniversalMenuAPI;

using static CounterStrikeSharp.API.Core.Listeners;

namespace SharpModMenu;

[MinimumApiVersion(314)]
public sealed class SharpModMenuPlugin : BasePlugin
{
	public override string ModuleName => "SharpModMenu";
	public override string ModuleDescription => "CSSUniversalMenuAPI menu driver to behave like SourceMod menus";
	public override string ModuleVersion => Verlite.Version.Full;
	private const string RuntimePatchMarker = "freeze-lock-2026-04-12-v2";

	private const string PrimaryConfigFileName = "sharpmodmenu_config.jsonc";
	private const string PrimaryTranslationsFileName = "sharpmodmenu_translations.jsonc";
	private static readonly string[] SupportedConfigFileNames = [PrimaryConfigFileName, "menu_config.jsonc"];
	private static readonly string[] SupportedTranslationsFileNames = [PrimaryTranslationsFileName, "menu_translations.jsonc"];
	private static readonly string[] ConfigDirectoryNames = ["SharpModMenu", "sharpmodmenu"];
	private static readonly string[] SharedDirectoryNames = ["SharpModMenu", "sharpmodmenu"];
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		PropertyNameCaseInsensitive = true,
	};

	private MenuDriver? DriverInstance { get; set; }
	private SharpModMenuPluginConfig PluginConfig { get; set; } = new();
	private SharpModMenuRuntimeOptions RuntimeOptions { get; set; } = SharpModMenuRuntimeOptions.Default;
	private SharpModMenuTranslations Translations { get; set; } = SharpModMenuTranslations.Default;
	private string ConfigPath { get; set; } = string.Empty;
	private string TranslationsPath { get; set; } = string.Empty;

	// near: "%sRecv usercmd %d.  Margin:%5.1fms net +%2d queue =%5.1f total\n"
	// https://github.com/CharlesBarone/CSSharp-Fixes/blob/9f20129bd4d17dcf7f30311ac0d24ec56895b04a/gamedata/cssharpfixes.json#L44
	private static string ProcessUserCmdsSig => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		? "48 8B C4 44 88 48 20 44 89 40 18 48 89 50 10 53"
		: "55 48 89 E5 41 57 41 56 41 55 41 54 53 48 89 FB 48 83 EC ? 89 4D";
	// void* FASTCALL ProcessUsercmds(CCSPlayerController* player, CUserCmdPB* cmds, int cmdCount, bool paused, float margin);
	private static MemoryFunctionWithReturn<nint, nint, int, bool, float, nint, nint>? ProcessUserCmdsFunc { get; set; }
	private bool UseFallbackWasdInputMethod { get; set; } = false;

	public override void Load(bool hotReload)
	{
		LoadDataFiles();
		DriverInstance = new(RuntimeOptions, Translations);
		Logger.LogWarning("SharpModMenu patch marker: {PatchMarker}", RuntimePatchMarker);

		// delay hooking ProcessUserCmds until CS2Fixes et al have had a chance to hook it
		Server.NextWorldUpdate(() =>
		{
			ProcessUserCmdsFunc ??= new(ProcessUserCmdsSig, Addresses.ServerPath);
			if (ProcessUserCmdsFunc.Handle == nint.Zero)
			{
				Console.WriteLine($"SharpModMenu: Failed to hook ProcessUserCmds, using subpar fallback method");
				UseFallbackWasdInputMethod = true;
				return;
			}

			ProcessUserCmdsFunc.Hook(ProcessUserCmds, HookMode.Pre);
		});

		UniversalMenu.RegisterDriver("SharpModMenu", DriverInstance);

		RegisterListener<OnTick>(OnTick);
		RegisterListener<CheckTransmit>(OnCheckTransmit);
	}

	private bool IsDebugEnabled()
	{
		return PluginConfig.Debug;
	}

	private void LogDebug(string message, params object?[] args)
	{
		if (!IsDebugEnabled())
		{
			return;
		}

		Logger.LogInformation("[SharpModMenu:DEBUG] " + message, args);
	}

	private void LoadDataFiles()
	{
		try
		{
			var moduleDirectory = Path.GetDirectoryName(ModulePath);
			var pluginDirectory = string.IsNullOrWhiteSpace(moduleDirectory) ? AppContext.BaseDirectory : moduleDirectory;
			var dataDirectories = ResolveDataDirectories(pluginDirectory);

			ConfigPath = ResolveDataFilePath(dataDirectories, SupportedConfigFileNames, PrimaryConfigFileName, out var configProbePaths);
			TranslationsPath = ResolveDataFilePath(dataDirectories, SupportedTranslationsFileNames, PrimaryTranslationsFileName, out var translationsProbePaths);

			EnsureConfigFileExists(ConfigPath);
			EnsureTranslationsFileExists(TranslationsPath);

			var configText = File.ReadAllText(ConfigPath);
			PluginConfig = JsonSerializer.Deserialize<SharpModMenuPluginConfig>(configText, JsonOptions) ?? new();
			RuntimeOptions = BuildRuntimeOptions(PluginConfig);
			Translations = LoadTranslations(TranslationsPath);

			Logger.LogInformation("SharpModMenu loaded config from {ConfigPath} and translations from {TranslationsPath}.",
				ConfigPath,
				TranslationsPath);
			LogDebug("Data directories: {Directories}", string.Join(" | ", dataDirectories));
			LogDebug("Config probes: {ProbePaths}", string.Join(" | ", configProbePaths));
			LogDebug("Translations probes: {ProbePaths}", string.Join(" | ", translationsProbePaths));
			LogDebug("Runtime options: DefaultPos=({PosX},{PosY}) Font={FontName} FontSize={FontSize}",
				RuntimeOptions.DefaultMenuPosition.X,
				RuntimeOptions.DefaultMenuPosition.Y,
				RuntimeOptions.WorldTextFontName,
				RuntimeOptions.WorldTextFontSize);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to load SharpModMenu config/translations. Using defaults.");
			PluginConfig = new();
			RuntimeOptions = SharpModMenuRuntimeOptions.Default;
			Translations = SharpModMenuTranslations.Default;
		}
	}

	private List<string> ResolveDataDirectories(string pluginDirectory)
	{
		var directories = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddDirectory(string? directory)
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				return;
			}

			try
			{
				var fullPath = Path.GetFullPath(directory);
				if (seen.Add(fullPath))
				{
					directories.Add(fullPath);
				}
			}
			catch
			{
				// Ignore invalid path candidates.
			}
		}

		// Prefer configs/plugins folders first.
		if (!string.IsNullOrWhiteSpace(Server.GameDirectory))
		{
			var configRoot = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs", "plugins");
			foreach (var configDirectoryName in ConfigDirectoryNames)
			{
				AddDirectory(Path.Combine(configRoot, configDirectoryName));
			}

			var sharedRoot = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "shared");
			foreach (var sharedDirectoryName in SharedDirectoryNames)
			{
				AddDirectory(Path.Combine(sharedRoot, sharedDirectoryName));
			}
		}

		try
		{
			var pluginDirInfo = new DirectoryInfo(pluginDirectory);
			var pluginsDirectory = pluginDirInfo.Parent;
			var cssRootDirectory = pluginsDirectory?.Parent;
			if (pluginsDirectory is not null &&
				cssRootDirectory is not null &&
				string.Equals(pluginsDirectory.Name, "plugins", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var configDirectoryName in ConfigDirectoryNames)
				{
					AddDirectory(Path.Combine(cssRootDirectory.FullName, "configs", "plugins", configDirectoryName));
				}

				foreach (var sharedDirectoryName in SharedDirectoryNames)
				{
					AddDirectory(Path.Combine(cssRootDirectory.FullName, "shared", sharedDirectoryName));
				}
			}
		}
		catch
		{
			// Ignore relative parsing failures and keep other candidates.
		}

		// Fallbacks.
		AddDirectory(pluginDirectory);
		AddDirectory(AppContext.BaseDirectory);

		if (directories.Count == 0)
		{
			directories.Add(AppContext.BaseDirectory);
		}

		return directories;
	}

	private static string ResolveDataFilePath(
		IReadOnlyList<string> dataDirectories,
		IReadOnlyList<string> candidateFileNames,
		string fallbackFileName,
		out string[] probePaths)
	{
		var normalizedDirectories = new List<string>();
		var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var directory in dataDirectories)
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				continue;
			}

			try
			{
				var fullPath = Path.GetFullPath(directory);
				if (!seenDirectories.Add(fullPath))
				{
					continue;
				}

				Directory.CreateDirectory(fullPath);
				normalizedDirectories.Add(fullPath);
			}
			catch
			{
				// Ignore invalid path candidates.
			}
		}

		if (normalizedDirectories.Count == 0)
		{
			var fallbackDirectory = Path.GetFullPath(AppContext.BaseDirectory);
			Directory.CreateDirectory(fallbackDirectory);
			normalizedDirectories.Add(fallbackDirectory);
		}

		var preferredDirectory = normalizedDirectories[0];
		var preferredPath = Path.Combine(preferredDirectory, fallbackFileName);

		var candidates = new List<(string path, int priority)>();
		int priority = 0;

		foreach (var directory in normalizedDirectories)
		{
			foreach (var fileName in candidateFileNames)
			{
				candidates.Add((Path.Combine(directory, fileName), priority++));
			}
		}

		probePaths = candidates.Select(x => x.path).ToArray();

		string? bestPath = null;
		DateTime bestWriteTime = DateTime.MinValue;
		int bestPriority = int.MaxValue;

		foreach (var candidate in candidates)
		{
			if (!File.Exists(candidate.path))
			{
				continue;
			}

			DateTime lastWrite;
			try
			{
				lastWrite = File.GetLastWriteTimeUtc(candidate.path);
			}
			catch
			{
				lastWrite = DateTime.MinValue;
			}

			if (bestPath is null ||
				lastWrite > bestWriteTime ||
				(lastWrite == bestWriteTime && candidate.priority < bestPriority))
			{
				bestPath = candidate.path;
				bestWriteTime = lastWrite;
				bestPriority = candidate.priority;
			}
		}

		if (File.Exists(preferredPath))
		{
			return preferredPath;
		}

		if (bestPath is not null &&
			!string.Equals(bestPath, preferredPath, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				File.Copy(bestPath, preferredPath, overwrite: true);
				return preferredPath;
			}
			catch
			{
				// Keep legacy file path if migration copy fails.
				return bestPath;
			}
		}

		return preferredPath;
	}

	private static void EnsureConfigFileExists(string path)
	{
		if (File.Exists(path))
		{
			return;
		}

		var defaultConfig = @"{
	/* SharpModMenu config */

	// Verbose debug logs in server console
	""Debug"": false,

	// Default menu position for players (3D/HTML fallback)
	""DefaultMenuPositionX"": -6.55,
	""DefaultMenuPositionY"": 0.0,

	// 3D menu text style
	""WorldTextFontName"": ""Tahoma Bold"",
	""WorldTextFontSize"": 25,
	""WorldTextHighlightColor"": ""#7FFFFF40"",
	""WorldTextForegroundColor"": ""#E79827"",
	""WorldTextBackgroundTextColor"": ""#FFFFFF"",
	""WorldTextPanelColor"": ""#C87F7F7F"",

	// HTML fallback colors
	""HtmlForegroundHighlightColor"": ""#EFCE21"",
	""HtmlForegroundColor"": ""#FFFFFF"",
	""HtmlBackgroundHighlightColor"": ""#EFE472"",
	""HtmlBackgroundColor"": ""#E7CCA5"",
	""HtmlEmptyLineColor"": ""#000"",

	// Freeze player movement while menu is open
	""FreezePlayerMovement"": true,

	// Icon shown around each selectable option
	""SelectorIcon"": ""\u279C"",
	""SelectorIconColor"": ""#EFCE21"",

	""HtmlFontClass"": ""fontSize-m"",


	// Message shown in compact footer mode (when HtmlFontClass is bigger than fontSize-s)
	// Leave empty to use translated defaults.
	""HtmlCompactFooterMessage"": ""<font color='#D10D0D'>Select: </font><font color='#F2A10F'>ZS/Use</font> <font color='#FFFFFF'>|</font> <font color='#D10D0D'>Exit:</font> <font color='#F2A10F'>Reload</font>"",

	// Optional font class for compact footer message only (example: fontSize-xs, fontSize-s, fontSize-m)
	""HtmlCompactFooterFontClass"": ""fontSize-s"",

	// In WASD mode, pressing movement key beyond first/last item changes page automatically.
	// AZERTY example: Z at top goes to previous page, S at bottom goes to next page.
	""WasdAutoPage"": true
}";

		File.WriteAllText(path, defaultConfig);
	}

	private static void EnsureTranslationsFileExists(string path)
	{
		if (File.Exists(path))
		{
			return;
		}

		var defaultTranslations = @"{
	/* SharpModMenu translations */
	""DefaultLanguage"": ""en"",
	""Languages"": {
		""en"": {
			""nav.previous"": ""Previous"",
			""nav.back"": ""Back"",
			""nav.next"": ""Next"",
			""nav.close"": ""Close"",
			""nav.exit"": ""Close"",
			""nav.compact_message"": ""Use 8 Previous | 9 Next | 0 Close""
		},
		""fr"": {
			""nav.previous"": ""Precedent"",
			""nav.back"": ""Retour"",
			""nav.next"": ""Suivant"",
			""nav.close"": ""Fermer"",
			""nav.exit"": ""Fermer"",
			""nav.compact_message"": ""Utilise 8 Precedent | 9 Suivant | 0 Fermer""
		}
	}
}";

		File.WriteAllText(path, defaultTranslations);
	}

	private SharpModMenuRuntimeOptions BuildRuntimeOptions(SharpModMenuPluginConfig config)
	{
		var options = new SharpModMenuRuntimeOptions
		{
			Debug = config.Debug,
			DefaultMenuPosition = new(
				SanitizeFiniteFloat(config.DefaultMenuPositionX, -6.55f),
				SanitizeFiniteFloat(config.DefaultMenuPositionY, 0.0f)),
			WorldTextFontName = string.IsNullOrWhiteSpace(config.WorldTextFontName) ? "Tahoma Bold" : config.WorldTextFontName.Trim(),
			WorldTextFontSize = Math.Clamp(config.WorldTextFontSize, 8, 72),
			HtmlForegroundHighlightColor = NormalizeHtmlColor(config.HtmlForegroundHighlightColor, "#EFCE21"),
			HtmlForegroundColor = NormalizeHtmlColor(config.HtmlForegroundColor, "#E28B12"),
			HtmlBackgroundHighlightColor = NormalizeHtmlColor(config.HtmlBackgroundHighlightColor, "#EFE472"),
			HtmlBackgroundColor = NormalizeHtmlColor(config.HtmlBackgroundColor, "#E7CCA5"),
			HtmlEmptyLineColor = NormalizeHtmlColor(config.HtmlEmptyLineColor, "#000"),
			FreezePlayerMovement = config.FreezePlayerMovement,
			SelectorIcon = string.IsNullOrWhiteSpace(config.SelectorIcon) ? "\u279C" : config.SelectorIcon.Trim(),
			SelectorIconColor = NormalizeHtmlColor(config.SelectorIconColor, "#EFCE21"),
			HtmlFontClass = string.IsNullOrWhiteSpace(config.HtmlFontClass) ? "fontSize-s" : config.HtmlFontClass.Trim(),
			HtmlCompactFooterMessage = config.HtmlCompactFooterMessage?.Trim() ?? string.Empty,
			HtmlCompactFooterFontClass = config.HtmlCompactFooterFontClass?.Trim() ?? string.Empty,
			WasdAutoPage = config.WasdAutoPage,
		};

		options.WorldTextHighlightColor = ParseColorOrDefault(config.WorldTextHighlightColor, options.WorldTextHighlightColor);
		options.WorldTextForegroundColor = ParseColorOrDefault(config.WorldTextForegroundColor, options.WorldTextForegroundColor);
		options.WorldTextBackgroundTextColor = ParseColorOrDefault(config.WorldTextBackgroundTextColor, options.WorldTextBackgroundTextColor);
		options.WorldTextPanelColor = ParseColorOrDefault(config.WorldTextPanelColor, options.WorldTextPanelColor);

		return options;
	}

	private SharpModMenuTranslations LoadTranslations(string path)
	{
		try
		{
			var translationsText = File.ReadAllText(path);
			var parsed = JsonSerializer.Deserialize<SharpModMenuTranslationFile>(translationsText, JsonOptions);
			if (parsed is null || parsed.Languages.Count == 0)
			{
				return SharpModMenuTranslations.Default;
			}

			var normalizedLanguages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var languageEntry in parsed.Languages)
			{
				var normalizedLanguage = NormalizeLanguageTag(languageEntry.Key);
				if (normalizedLanguage is null)
				{
					continue;
				}

				var normalizedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var keyValue in languageEntry.Value)
				{
					if (string.IsNullOrWhiteSpace(keyValue.Key))
					{
						continue;
					}

					normalizedEntries[keyValue.Key.Trim()] = keyValue.Value ?? string.Empty;
				}

				normalizedLanguages[normalizedLanguage] = normalizedEntries;
			}

			if (normalizedLanguages.Count == 0)
			{
				return SharpModMenuTranslations.Default;
			}

			var defaultLanguage = NormalizeLanguageTag(parsed.DefaultLanguage) ?? "en";
			return new SharpModMenuTranslations(defaultLanguage, normalizedLanguages);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to parse SharpModMenu translations file {TranslationsPath}. Using defaults.", path);
			return SharpModMenuTranslations.Default;
		}
	}

	private static string? NormalizeLanguageTag(string? languageTag)
	{
		if (string.IsNullOrWhiteSpace(languageTag))
		{
			return null;
		}

		return languageTag.Trim().Replace('_', '-').ToLowerInvariant();
	}

	private static float SanitizeFiniteFloat(float value, float fallback)
	{
		return float.IsFinite(value) ? value : fallback;
	}

	private static string NormalizeHtmlColor(string value, string fallback)
	{
		if (!TryParseHexColor(value, out var parsed))
		{
			return fallback;
		}

		return parsed.A == 255
			? $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}"
			: $"#{parsed.A:X2}{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
	}

	private static Color ParseColorOrDefault(string value, Color fallback)
	{
		return TryParseHexColor(value, out var parsed) ? parsed : fallback;
	}

	private static bool TryParseHexColor(string value, out Color color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var normalized = value.Trim();
		if (normalized.StartsWith('#'))
		{
			normalized = normalized[1..];
		}
		else if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[2..];
		}

		if (normalized.Length == 6 && uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
		{
			var r = (byte)((rgb >> 16) & 0xFF);
			var g = (byte)((rgb >> 8) & 0xFF);
			var b = (byte)(rgb & 0xFF);
			color = Color.FromArgb(r, g, b);
			return true;
		}

		if (normalized.Length == 8 && uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
		{
			var a = (byte)((argb >> 24) & 0xFF);
			var r = (byte)((argb >> 16) & 0xFF);
			var g = (byte)((argb >> 8) & 0xFF);
			var b = (byte)(argb & 0xFF);
			color = Color.FromArgb(a, r, g, b);
			return true;
		}

		return false;
	}

	public override void Unload(bool hotReload)
	{
		UniversalMenu.UnregisterDriver("SharpModMenu");

		ProcessUserCmdsFunc?.Unhook(ProcessUserCmds, HookMode.Pre);
	}

	private void OnTick()
	{
		if (UseFallbackWasdInputMethod)
		{
			for (int i = 0; i < DriverInstance!.ActiveMenuStates.Count; i++)
			{
				var menuState = DriverInstance!.ActiveMenuStates[i];
				if (!menuState.Player.IsValid)
					continue;

				if (!menuState.IsUsingKeybinds)
				{
					var buttons = menuState.Player.Buttons;

					bool pressingForward = buttons.HasFlag(PlayerButtons.Forward);
					bool pressingBack = buttons.HasFlag(PlayerButtons.Back);
					bool pressingLeft = buttons.HasFlag(PlayerButtons.Left);
					bool pressingRight = buttons.HasFlag(PlayerButtons.Right);
					bool pressingUse = buttons.HasFlag(PlayerButtons.Use);
					bool pressingTab = buttons.HasFlag((PlayerButtons)0x200000000);
					bool pressingReload = buttons.HasFlag(PlayerButtons.Reload);

					if (pressingForward && pressingForward != menuState.WasPressingForward)
						menuState.HandleInput(PlayerKey.Up, false);
					if (pressingBack && pressingBack != menuState.WasPressingBack)
						menuState.HandleInput(PlayerKey.Down, false);
					if (pressingUse && pressingUse != menuState.WasPressingUse)
						menuState.HandleInput(PlayerKey.Select, false);
					if (pressingTab && pressingTab != menuState.WasPressingTab)
						menuState.HandleInput(PlayerKey.ToggleFocus, false);
					if (pressingReload && pressingReload != menuState.WasPressingReload)
						menuState.HandleInput(PlayerKey.Close, false);

					menuState.WasPressingForward = pressingForward;
					menuState.WasPressingBack = pressingBack;
					menuState.WasPressingLeft = pressingLeft;
					menuState.WasPressingRight = pressingRight;
					menuState.WasPressingUse = pressingUse;
					menuState.WasPressingTab = pressingTab;
					menuState.WasPressingReload = pressingReload;
				}
			}
		}

		for (int i = 0; i < DriverInstance!.ActiveMenuStates.Count; i++)
			DriverInstance!.ActiveMenuStates[i].Tick();
	}

	// prevent transmitting a player's menuState entities to other players
	// TODO: hot path, this needs to be extremely quick, precompute data structure for fast iteration
	// NOTE: do not replace with foreach, else you will put a lot of pressure on the garbage collector
	private void OnCheckTransmit(CCheckTransmitInfoList infoList)
	{
		if (DriverInstance is null)
			return;
		
		if (DriverInstance.MenuEntities.Count == 0)
			return;

		for (int n = 0; n < infoList.Count; n++)
		{
			var info = infoList[n];
			for (int i = 0; i < DriverInstance.MenuEntities.Count; i++)
			{
				var entInfo = DriverInstance.MenuEntities[i];
				if (entInfo.target != info.player && entInfo.target.IsValid)
					info.info.TransmitEntities.Remove(entInfo.ent);
			}
		}
	}

	[GameEventHandler(HookMode.Pre)]
	public HookResult OnPlayerDisconnect(EventPlayerDisconnect e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		DriverInstance?.PlayerDisconnected(e.Userid);
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnGameStart(EventGameStart e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnRoundStart(EventRoundStart e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnBeginNewMatch(EventBeginNewMatch e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnGameInit(EventGameInit e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerSpawned(EventPlayerSpawned e, GameEventInfo info)
	{
		if (DriverInstance?.ActiveMenuStates is null)
			return HookResult.Continue;

		foreach (var state in DriverInstance!.ActiveMenuStates)
			if (e.Userid == state.Player)
				state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[StructLayout(LayoutKind.Explicit)]
	private unsafe struct RepeatedPtrField
	{
		[FieldOffset(0x0)]
		public nint ArenaAddress;
		[FieldOffset(0x8)]
		public int CurrentSize;
		[FieldOffset(0xC)]
		public int TotalSize;
		[FieldOffset(0x10)]
		public nint Rep;
	}

	[Flags]
	private enum ButtonsStateC : ulong
	{
		None = default,
		PrimaryAttack = 0x1,
		SecondaryAttack = 0x800,
		Inspect = 0x800000000,
		Walk = 0x10000,
		Crouch = 0x4,
		Forward = 0x8,
		Back = 0x10,
		Right = 0x400,
		Left = 0x200,
		Jump = 0x2,
		Use = 0x20,
		Reload = 0x2000,
		Tab = 0x200000000,
	}

	private const ButtonsStateC MovementButtonsMask =
		ButtonsStateC.Forward |
		ButtonsStateC.Back |
		ButtonsStateC.Left |
		ButtonsStateC.Right |
		ButtonsStateC.Jump |
		ButtonsStateC.Crouch |
		ButtonsStateC.Walk;

	[StructLayout(LayoutKind.Sequential)]
	private unsafe struct CInButtonStateReal
	{
		public nint Table;
		public ulong ButtonState1;
		public ulong ButtonState2;
		public ButtonsStateC ButtonState3;
		public ulong Padding0;
		public ulong Padding1;
		public ulong Padding2;
		public ulong Padding3;
		public ulong Padding4;
	}

	[StructLayout(LayoutKind.Explicit)]
	private unsafe struct CBaseUserCmdPB
	{
		[FieldOffset(0x18)]
		public RepeatedPtrField SubtickMoves;
		[FieldOffset(0x38)]
		public CInButtonStateReal* Buttons;
		[FieldOffset(0x48)]
		public int CommandNumber;
		[FieldOffset(0x4c)]
		public int ClientTick;
		[FieldOffset(0x50)]
		public float ForwardMove;
		[FieldOffset(0x54)]
		public float SideMove;
		[FieldOffset(0x58)]
		public float UpMove;
		[FieldOffset(0x5C)]
		public int Impulse;
		[FieldOffset(0x60)]
		public int WeaponSelect;
		[FieldOffset(0x64)]
		public int Seed;
		[FieldOffset(0x68)]
		public float MouseX;
		[FieldOffset(0x6c)]
		public float MouseY;
		[FieldOffset(0x70)]
		public uint ConsumedServerAngleChanges;
		[FieldOffset(0x74)]
		public uint CmdFlags;
		[FieldOffset(0x78)]
		public uint PawnEntityHandle;
	};

	[StructLayout(LayoutKind.Explicit)]
	private unsafe struct CUserCmdPB
	{
		[FieldOffset(0x40)]
		public CBaseUserCmdPB* Base;

		public static ulong Size => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (ulong)0x98 : (ulong)0x90;
	};

	// https://github.com/Wend4r/mms2-menu_system/blob/main/src/menusystem_plugin.cpp#L4310-L4336
	// https://github.com/Source2ZE/CS2Fixes/blob/main/gamedata/cs2fixes.games.txt#L303-L308 ProcessUsercmds
	// https://github.com/CharlesBarone/CSSharp-Fixes/blob/9f20129bd4d17dcf7f30311ac0d24ec56895b04a/CSSharpFixes/Detours/ProcessUserCmdsHandler.cs#L55
	// NOTE: hotpath, try to complete ASAP, avoid mem allocations
	private unsafe HookResult ProcessUserCmds(DynamicHook hook)
	{
		if (DriverInstance!.ActiveMenuStates.Count == 0)
			return HookResult.Continue;

		var playerPtr = hook.GetParam<nint>(0);
		//var slot = (*(int*)((ulong)playerPtr + 0x10) & (Utilities.MaxEdicts - 1)) - 1;
		//var player = Utilities.GetPlayerFromSlot(slot);
		var player = Utilities.GetPlayerFromSlot(new CCSPlayerController(playerPtr).Slot);
		if (player is not { IsValid: true, IsBot: false })
			return HookResult.Continue;

		var menuState = DriverInstance.GetMenuState(player);
		var currentMenu = menuState.CurrentMenu;
		if (currentMenu is null)
			return HookResult.Continue;

		var cmdsPtr = hook.GetParam<nint>(1);
		var cmdsCount = hook.GetParam<int>(2);

		var pressingForward = false;
		var pressingBack = false;
		var pressingLeft = false;
		var pressingRight = false;
		var pressingUse = false;
		var pressingTab = false;
		var pressingReload = false;
		var freezePlayerMovement = true;

		int? inhibitWeaponSelectionTick = menuState.InhibitWeaponSelection;
		menuState.InhibitWeaponSelection = null;
		bool inhibitWeaponSelect = false;

		for (ulong i = 0; i < (ulong)cmdsCount; i++)
		{
			var cmd = (CUserCmdPB*)((ulong)cmdsPtr + i * CUserCmdPB.Size);
			
			if ((nint)cmd->Base == nint.Zero)
				continue;

			inhibitWeaponSelect |= inhibitWeaponSelectionTick.HasValue && inhibitWeaponSelectionTick.Value <= cmd->Base->ClientTick;
			menuState.ClientTick = cmd->Base->ClientTick;

			var cmdPtr = (nint)cmd->Base;
			var span = new Span<byte>(cmd->Base, 0x82);

			if (false)
			{
				var sb = new StringBuilder();
				for (int n = 0; n < span.Length; n++)
				{
					if (n % 16 == 0)
						sb.Append($"\n{n:X2}: ");
					else if (n % 8 == 0)
						sb.Append("  ");
					else
						sb.Append(' ');
					sb.Append($"{span[n]:X2}");
				}
				Console.WriteLine(sb.ToString());
			}

			if (freezePlayerMovement || !menuState.IsUsingKeybinds)
			{
				cmd->Base->SideMove = 0.0f;
				cmd->Base->ForwardMove = 0.0f;
				cmd->Base->UpMove = 0.0f;
			}

			if (menuState.IsUsingKeybinds)
			{
				if (inhibitWeaponSelect)
					cmd->Base->WeaponSelect = 0;

				if (freezePlayerMovement && (nint)cmd->Base->Buttons != nint.Zero)
					cmd->Base->Buttons->ButtonState3 &= ~MovementButtonsMask;
			}
			else
			{
				if ((nint)cmd->Base->Buttons != nint.Zero)
				{
					var buttons = cmd->Base->Buttons->ButtonState3;

					pressingForward |= buttons.HasFlag(ButtonsStateC.Forward);
					pressingBack |= buttons.HasFlag(ButtonsStateC.Back);
					pressingLeft |= buttons.HasFlag(ButtonsStateC.Left);
					pressingRight |= buttons.HasFlag(ButtonsStateC.Right);
					pressingUse |= buttons.HasFlag(ButtonsStateC.Use);
					pressingTab |= buttons.HasFlag(ButtonsStateC.Tab);
					pressingReload |= buttons.HasFlag(ButtonsStateC.Reload);

					// don't pass any buttons in if we have an active WASD menu
					cmd->Base->Buttons->ButtonState3 = ButtonsStateC.None;
				}
			}
		}

		if (!menuState.IsUsingKeybinds)
		{
			if (pressingForward && pressingForward != menuState.WasPressingForward)
				menuState.HandleInput(PlayerKey.Up, false);
			if (pressingBack && pressingBack != menuState.WasPressingBack)
				menuState.HandleInput(PlayerKey.Down, false);
			if (pressingUse && pressingUse != menuState.WasPressingUse)
				menuState.HandleInput(PlayerKey.Select, false);
			if (pressingTab && pressingTab != menuState.WasPressingTab)
				menuState.HandleInput(PlayerKey.ToggleFocus, false);
			if (pressingReload && pressingReload != menuState.WasPressingReload)
				menuState.HandleInput(PlayerKey.Close, false);

			menuState.WasPressingForward = pressingForward;
			menuState.WasPressingBack = pressingBack;
			menuState.WasPressingLeft = pressingLeft;
			menuState.WasPressingRight = pressingRight;
			menuState.WasPressingUse = pressingUse;
			menuState.WasPressingTab = pressingTab;
			menuState.WasPressingReload = pressingReload;
		}

		return HookResult.Continue;
	}

	[ConsoleCommand("menuselect")]
	public void MenuSelect(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;

		if (info.ArgCount < 2)
		{
			info.ReplyToCommand($"menuselect: Invalid number of arguments provided");
			return;
		}

		var valStr = info.ArgByIndex(1);
		if (!int.TryParse(valStr, CultureInfo.InvariantCulture, out var val))
		{
			info.ReplyToCommand($"menuselect: Failed to parse argument as int: {valStr}");
			return;
		}

		var key = (PlayerKey)((int)PlayerKey.SelectItem1 + (val - 1));
		menuState.HandleInput(key, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_1"), ConsoleCommand("css_2"), ConsoleCommand("css_3"), ConsoleCommand("css_4"), ConsoleCommand("css_5")]
	[ConsoleCommand("css_6"), ConsoleCommand("css_7"), ConsoleCommand("css_8"), ConsoleCommand("css_9"), ConsoleCommand("css_0")]
	public void CssIndex(CCSPlayerController? player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;

		var key = info.GetArg(0) switch
		{
			"css_1" => PlayerKey.SelectItem1,
			"css_2" => PlayerKey.SelectItem2,
			"css_3" => PlayerKey.SelectItem3,
			"css_4" => PlayerKey.SelectItem4,
			"css_5" => PlayerKey.SelectItem5,
			"css_6" => PlayerKey.SelectItem6,
			"css_7" => PlayerKey.SelectItem7,
			"css_8" => PlayerKey.SelectItem8,
			"css_9" => PlayerKey.SelectItem9,
			"css_0" => PlayerKey.SelectItem0,
			_ => PlayerKey.Unknown,
		};

		menuState.HandleInput(key, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_sharpmodmenu_reload")]
	public void SharpModMenuReload(CCSPlayerController? _, CommandInfo info)
	{
		LoadDataFiles();
		if (DriverInstance is not null)
		{
			DriverInstance.UpdateRuntimeData(RuntimeOptions, Translations);
			DriverInstance.ForceRefreshAll();
		}

		info.ReplyToCommand($"SharpModMenu reloaded. config={ConfigPath} translations={TranslationsPath} HtmlFontClass={RuntimeOptions.HtmlFontClass} HtmlCompactFooterFontClass={RuntimeOptions.HtmlCompactFooterFontClass} FreezePlayerMovement={RuntimeOptions.FreezePlayerMovement} WasdAutoPage={RuntimeOptions.WasdAutoPage}");
	}

	[ConsoleCommand("css_sharpmodmenu_pos_x")]
	public void SharpModMenuPosX(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;

		if (info.ArgCount != 2)
		{
			info.ReplyToCommand($"css_sharpmodmenu_pos_x: Invalid number of arguments provided");
			return;
		}

		var valStr = info.ArgByIndex(1);
		if (!float.TryParse(valStr, CultureInfo.InvariantCulture, out var val))
		{
			info.ReplyToCommand($"css_sharpmodmenu_pos_x: Failed to parse argument as float: {valStr}");
			return;
		}

		menuState.MenuPosition = menuState.MenuPosition with { X = val };
	}

	[ConsoleCommand("css_sharpmodmenu_pos_y")]
	public void SharpModMenuPosY(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;

		if (info.ArgCount != 2)
		{
			info.ReplyToCommand($"css_sharpmodmenu_pos_y: Invalid number of arguments provided");
			return;
		}

		var valStr = info.ArgByIndex(1);
		if (!float.TryParse(valStr, CultureInfo.InvariantCulture, out var val))
		{
			info.ReplyToCommand($"css_sharpmodmenu_pos_y: failed to parse argument as float: {valStr}");
			return;
		}

		menuState.MenuPosition = menuState.MenuPosition with { Y = val };
	}
}
