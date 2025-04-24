using System;
using System.Collections.Generic;
using System.Threading;

using CounterStrikeSharp.API.Core;

using CSSUniversalMenuAPI;
using CSSUniversalMenuAPI.Extensions;

namespace SharpModMenu;

internal sealed class MenuDriver : IMenuAPI
{
	private Dictionary<ulong, PlayerMenuState> MenuStates = new();
	public List<(CBaseEntity ent, CCSPlayerController target)> MenuEntities { get; } = new();
	public List<PlayerMenuState> ActiveHtmlMenuStates { get; } = new();

	internal PlayerMenuState GetMenuState(CCSPlayerController player, bool create = false)
	{
		if (!MenuStates.TryGetValue(player.SteamID, out var menuState))
		{
			if (!create)
				return new() { Player = player, Driver = this }; // throw a menu state into the ether
			MenuStates.Add(player.SteamID, menuState = new() { Player = player, Driver = this });
		}
		return menuState;
	}
	internal void PlayerDisconnected(CCSPlayerController? player)
	{
		if (player is null)
			return;
		MenuStates.Remove(player.SteamID);
	}

	public IMenu CreateMenu(CCSPlayerController player, CancellationToken ct = default)
	{
		return new Menu()
		{
			Driver = this,
			MenuState = GetMenuState(player, create: true),
			Parents = [],
			Player = player,
			Ct = ct,
		};
	}

	public IMenu CreateMenu(IMenu parent, CancellationToken ct = default)
	{
		if (parent is not Menu parentTyped)
			throw new ArgumentException("Mismatched menu type", nameof(parent));
		if (!ReferenceEquals(parentTyped.Driver, this))
			throw new ArgumentException("From a different menu instance", nameof(parent));

		CancellationToken linkedToken;
		if (!ct.CanBeCanceled)
			linkedToken = parentTyped.Ct;
		else if (!parentTyped.Ct.CanBeCanceled)
			linkedToken = ct;
		else
			linkedToken = CancellationTokenSource.CreateLinkedTokenSource(ct, parentTyped.Ct).Token;

		var parents = new List<Menu>(parentTyped.Parents)
		{
			parentTyped
		};

		return new Menu()
		{
			Driver = this,
			MenuState = GetMenuState(parentTyped.Player, create: true),
			Parents = parents,
			Player = parent.Player,
			Ct = linkedToken,
		};
	}

	public bool IsExtensionSupported(Type extension)
	{
		if (extension == typeof(IMenuPriorityExtension))
			return true;
		if (extension == typeof(INavigateBackMenuExtension))
			return true;
		if (extension == typeof(IMenuItemSubtitleExtension))
			return true;
		return false;
	}

	public bool IsMenuOpen(CCSPlayerController player)
	{
		return GetMenuState(player).FocusStack.Count != 0;
	}
}
