using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

using CSSUniversalMenuAPI;


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
	}

	public override void Unload(bool hotReload)
	{
		UniversalMenu.UnregisterDriver("SharpModMenu");
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
