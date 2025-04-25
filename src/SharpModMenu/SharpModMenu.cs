using System.Collections.Generic;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

using CSSUniversalMenuAPI;
using static CounterStrikeSharp.API.Core.Listeners;


namespace SharpModMenu;


[MinimumApiVersion(314)]
public sealed class SharpModMenuPlugin : BasePlugin
{
	public override string ModuleName => "SharpModMenu";
	public override string ModuleDescription => "CSSUniversalMenuAPI menu driver to behave like SourceMod menus";
	public override string ModuleVersion => Verlite.Version.Full;


	private MenuDriver? DriverInstance { get; set; }

	public override void Load(bool hotReload)
	{
		DriverInstance = new();
		UniversalMenu.RegisterDriver("SharpModMenu", DriverInstance);

		RegisterListener<OnTick>(OnTick);
		RegisterListener<CheckTransmit>(OnCheckTransmit);
	}

	public override void Unload(bool hotReload)
	{
		UniversalMenu.UnregisterDriver("SharpModMenu");
	}

	private void OnTick()
	{
		for(int i = 0; i < DriverInstance!.ActiveMenuStates.Count; i++)
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
			for (int i = 0; i < DriverInstance.MenuEntities.Count; i++)
			{
				var info = infoList[n];
				var entInfo = DriverInstance.MenuEntities[i];
				if (entInfo.target != info.player && entInfo.target.IsValid)
					info.info.TransmitEntities.Remove(entInfo.target);
			}
		}
	}

	[GameEventHandler(HookMode.Pre)]
	public HookResult OnPlayerDisconnect(EventPlayerDisconnect e, GameEventInfo info)
	{
		DriverInstance?.PlayerDisconnected(e.Userid);
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerDisconnect(EventGameStart e, GameEventInfo info)
	{
		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerDisconnect(EventRoundStart e, GameEventInfo info)
	{
		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerDisconnect(EventBeginNewMatch e, GameEventInfo info)
	{
		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerDisconnect(EventGameInit e, GameEventInfo info)
	{
		foreach (var state in DriverInstance!.ActiveMenuStates)
			state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[GameEventHandler(HookMode.Post)]
	public HookResult OnPlayerDisconnect(EventPlayerSpawned e, GameEventInfo info)
	{
		foreach (var state in DriverInstance!.ActiveMenuStates)
			if (e.Userid == state.Player)
				state.ForceRefresh = true;
		return HookResult.Continue;
	}

	[ConsoleCommand("css_1")]
	public void Css1(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D1, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_2")]
	public void Css2(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D2, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_3")]
	public void Css3(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D3, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_4")]
	public void Css4(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D4, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_5")]
	public void Css5(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D5, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_6")]
	public void Css6(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D6, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_7")]
	public void Css7(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D7, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_8")]
	public void Css8(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D8, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_9")]
	public void Css9(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D9, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_0")]
	public void Css0(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.D0, info.CallingContext == CommandCallingContext.Console);
	}


	public static string[] PrimaryGuns { get; } =
	{
		"M4A4",
		"M4A1-S",
		"AK47",
		"Galil",
		"M249",
		"Famas",
		"SG553",
		"AUG",
		"Nova",
		"AWP",
		"SCAR-20",
		"G3SG1",
		"XM1014",
		"MAC 10",
		"MP9",
		"MP5",
		"UMP45",
		"P90",
		"Scout",
		"MAG-7",
		"Sawed-Off",
		"PP-Bizon",
		"MP7",
		"MP7",
		"Negev",
		"Random",
		"None",
	};
	public static string[] SecondaryGuns { get; } =
	{
		"USP",
		"Glock",
		"Deagle",
		"P250",
		"Elite",
		"Five Seven",
		"P2000",
		"Tec-9",
		"CZ75",
		"R8",
		"Random",
		"None",
	};
	public static Dictionary<string, string?> DisabledGuns { get; } = new()
	{
		["AWP"] = "2/2",
		["SG550"] = null,
		["G3SG1"] = null,
	};
	[ConsoleCommand("css_guns")]
	public void GunsTest(CCSPlayerController player, CommandInfo info)
	{
		var primaryMenu = UniversalMenu.CreateMenu(player);
		primaryMenu.Title = "Primary Weapon";

		foreach (var primaryGun in PrimaryGuns)
		{
			var item = primaryMenu.CreateItem();
			item.Title = primaryGun;

			if (DisabledGuns.TryGetValue(primaryGun, out var disabledInfo))
			{
				item.Enabled = false;
				if (disabledInfo is not null)
					item.Title = $"{primaryGun} [{disabledInfo}]";
			}

			if (item.Enabled)
				item.Selected += PrimaryGun_Selected;
		}

		primaryMenu.Display();
	}

	private static void PrimaryGun_Selected(IMenuItem selectedItem)
	{
		//PrimaryWeapon = selectedItem.Title; // should use .Context to find the real value

		var secondaryMenu = UniversalMenu.CreateMenu(selectedItem.Menu);
		secondaryMenu.Title = "Secondary Weapon";

		foreach (var secondaryGun in SecondaryGuns)
		{
			var item = secondaryMenu.CreateItem();
			item.Title = secondaryGun;

			if (DisabledGuns.TryGetValue(secondaryGun, out var disabledInfo))
			{
				item.Enabled = false;
				if (disabledInfo is not null)
					item.Title = $"{secondaryGun} [{disabledInfo}]";
			}

			if (item.Enabled)
				item.Selected += SecondaryGun_Selected;
		}

		secondaryMenu.Display();
	}

	private static void SecondaryGun_Selected(IMenuItem selectedItem)
	{
		//SecondaryWeapon = selectedItem.Title; // should use .Context to find the real value
		selectedItem.Menu.Exit();
	}
}
