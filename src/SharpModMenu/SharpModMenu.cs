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
		foreach (var menuState in DriverInstance!.ActiveHtmlMenuStates)
		{
			if (menuState.HtmlContent is null)
				continue;
			menuState.Player.PrintToCenterHtml(menuState.HtmlContent);
		}
	}

	// prevent transmitting a player's menuState entities to other players
	// TODO: hot path, this needs to be extremely quick, precompute data structure for fast iteration
	// NOTE: do not replace with foreach, else you will put a lot of pressure on the garbage collector
	private void OnCheckTransmit(CCheckTransmitInfoList infoList)
	{
		if (DriverInstance is null)
			return;
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
}
