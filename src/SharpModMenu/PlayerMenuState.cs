using System;
using System.Collections.Generic;

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using CSSUniversalMenuAPI;

namespace SharpModMenu;

internal class PlayerMenuState
{
	public required CCSPlayerController Player { get; init; }
	public List<Menu> FocusStack { get; } = new();
	public Menu? LastPresented { get; set; }
	public Menu? CurrentMenu { get; set; }

	private bool _HasBinds;
	public bool HasKeyBinds
	{
		get => _HasBinds;
		set
		{
			if (value == _HasBinds)
				return;
			_HasBinds = value;
			Refresh(sortPriorities: false);
		}
	}

	public void HandleInput(PlayerKey key, bool fromBind)
	{
		if (CurrentMenu is null)
			return;

		var itemsStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
		var buttonState = CurrentMenu.GetButtonStates();

		switch (key)
		{
			case >= PlayerKey.D1 and <= PlayerKey.D7:
				int index = key - PlayerKey.D1;
				if (index < CurrentMenu.Items.Count && CurrentMenu.Items[itemsStart + index] is MenuItem { Enabled: true } menuItem)
					menuItem.RaiseSelected();
				break;
			case PlayerKey.D8:
				if (buttonState.ShowPrevButton)
					CurrentMenu.CurrentPage--;
				else if (buttonState.ShowBackButton)
				{
					if (CurrentMenu.NavigateBack is not null)
						CurrentMenu.NavigateBack(CurrentMenu);
					else
						CurrentMenu.Close();
				}
				break;
			case PlayerKey.D9:
				if (buttonState.ShowNextButton)
					CurrentMenu.CurrentPage++;
				break;
			case PlayerKey.D0:
				if (buttonState.ShowExitButton)
					(CurrentMenu as IMenu).Exit();
				break;
			default:
				break;
		}
	}

	public void Refresh(bool sortPriorities = true)
	{
		if (sortPriorities)
		{
			FocusStack.Sort((left, right) =>
			{
				int ret;
				int parentDepth = Math.Min(left.Parents.Count, right.Parents.Count);
				for (int i = 0; i < parentDepth; i++)
				{
					ret = right.Parents[i].Priority.CompareTo(left.Parents[i].Priority);
					if (ret != 0)
						return ret;
				}
				ret = right.Parents.Count.CompareTo(left.Parents.Count);
				if (ret != 0)
					return ret;
				ret = right.Priority.CompareTo(left.Priority); // highest priority comes first
				if (ret != 0)
					return ret;
				return right.OpenedAt.CompareTo(left.OpenedAt); // ok, select the newest menu first
			});
		}

		CurrentMenu = FocusStack.Count > 0 ? FocusStack[0] : null;
		DrawActiveMenu();
	}

	public void DrawActiveMenu()
	{
		if (CurrentMenu == LastPresented && CurrentMenu?.IsDirty is null or false)
			return;

		if (CurrentMenu is null)
			return;

		Player.PrintToChat($"{ChatColors.White}{CurrentMenu.Title}:{ChatColors.Default}");

		var btnStates = CurrentMenu.GetButtonStates();

		var itemsStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
		var itemsTo = Math.Min(itemsStart + Menu.ItemsPerPage, CurrentMenu.Items.Count);
		for (int i = 0; i < Menu.ItemsPerPage && itemsStart + i < itemsTo; i++)
		{
			var item = CurrentMenu.Items[itemsStart + i];

			if (item.Enabled)
				Player.PrintToChat($"{ChatColors.Yellow}{i + 1}. {item.Title}{ChatColors.Default}");
			else
				Player.PrintToChat($"{ChatColors.Grey}{i + 1}. {item.Title}{ChatColors.Default}");

			if (!string.IsNullOrEmpty(item.Subtitle))
				Player.PrintToChat($"{ChatColors.Silver}   {item.Subtitle}{ChatColors.Default}");
		}

		if (btnStates.ShowBackButton)
			Player.PrintToChat($"{ChatColors.Orange}8. Back{ChatColors.Default}");
		if (btnStates.ShowPrevButton)
			Player.PrintToChat($"{ChatColors.Orange}8. Previous{ChatColors.Default}");
		if (btnStates.ShowNextButton)
			Player.PrintToChat($"{ChatColors.Orange}9. Back{ChatColors.Default}");
		if (btnStates.ShowExitButton)
			Player.PrintToChat($"{ChatColors.White}0. Exit{ChatColors.Default}");
	}
}
