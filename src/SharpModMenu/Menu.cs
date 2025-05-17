using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using CounterStrikeSharp.API.Core;

using CSSUniversalMenuAPI;
using CSSUniversalMenuAPI.Extensions;

namespace SharpModMenu;

internal struct MenuButtonState
{
	public bool ShowExitButton { get; set; }
	public bool ShowBackButton { get; set; }
	public bool ShowPrevButton { get; set; }
	public bool ShowNextButton { get; set; }
	public bool ShowNavigation { get; set; }
}

internal class Menu : IMenu, IMenuPriorityExtension, INavigateBackMenuExtension
{
	public required PlayerMenuState MenuState { get; init; }
	public required MenuDriver Driver { get; init; }

	public required List<Menu> Parents { get; init; }
	public required CancellationToken Ct { get; init; }
	public CancellationTokenRegistration Ctr { get; set; }
	public List<MenuItem> Items { get; } = [];
	public int CurrentPage { get; set; }
	public const int ItemsPerPage = 7;
	public Menu? Parent => Parents.Count > 0 ? Parents[^1] : null;
	public DateTime OpenedAt { get; set; }

	public int SelectionIndex { get; set; }

	public int PrevSelectionIndex
	{
		get
		{
			var itemsVisible = Math.Min(ItemsPerPage, Items.Count - (CurrentPage * ItemsPerPage));
			var buttonStates = GetButtonStates();

			if (SelectionIndex > 8 && buttonStates.ShowNextButton)
				return 8;
			if (SelectionIndex > 7 && (buttonStates.ShowBackButton || buttonStates.ShowPrevButton))
				return 7;
			if (SelectionIndex > itemsVisible - 1)
				return itemsVisible - 1;
			if (SelectionIndex > 0)
				return SelectionIndex - 1;

			if (SelectionIndex == 0)
			{
				if (buttonStates.ShowExitButton)
					return 9;
				if (buttonStates.ShowNextButton)
					return 8;
				if (buttonStates.ShowBackButton || buttonStates.ShowPrevButton)
					return 7;
				if (itemsVisible > 0)
					return itemsVisible - 1;
				return SelectionIndex;
			}

			return SelectionIndex;
		}
	}
	public int NextSelectionIndex
	{
		get
		{
			var itemsVisible = Math.Min(ItemsPerPage, Items.Count - (CurrentPage * ItemsPerPage));
			var buttonStates = GetButtonStates();

			if (SelectionIndex < itemsVisible - 1)
				return SelectionIndex + 1;
			if (SelectionIndex < 7 && (buttonStates.ShowBackButton || buttonStates.ShowPrevButton))
				return 7;
			if (SelectionIndex < 8 && buttonStates.ShowNextButton)
				return 8;
			if (SelectionIndex < 9 && buttonStates.ShowExitButton)
				return 9;

			if (SelectionIndex == 9)
			{
				if (itemsVisible > 0)
					return 0;
				if (buttonStates.ShowBackButton || buttonStates.ShowPrevButton)
					return 7;
				if (buttonStates.ShowNextButton)
					return 8;
				if (buttonStates.ShowExitButton)
					return 9;
				return SelectionIndex;
			}

			return SelectionIndex;
		}
	}

	internal MenuButtonState GetButtonStates()
	{
		var ret = new MenuButtonState();
		ret.ShowExitButton = Parents.Where(x => !x.PlayerCanClose).Any() == false;
		ret.ShowBackButton = this switch
		{
			{ CurrentPage: not 0 } => false,
			{ NavigateBack: not null } => true,
			_ => ret.ShowExitButton switch
			{
				true => PlayerCanClose && Parent is not null,
				false => PlayerCanClose,
			},
		};
		ret.ShowPrevButton = CurrentPage > 0;
		ret.ShowNextButton = (CurrentPage + 1) * ItemsPerPage < Items.Count;
		ret.ShowNavigation = ret.ShowBackButton || ret.ShowPrevButton || ret.ShowNextButton;
		return ret;
	}

	// IMenu
	IMenu? IMenu.Parent => Parent;
	public required CCSPlayerController Player { get; internal init; }
	public bool IsActive => MenuState.FocusStack.Contains(this);
	public string Title { get; set; } = string.Empty;
	public bool PlayerCanClose { get; set; } = true;

	public IMenuItem CreateItem()
	{
		var ret = new MenuItem() { Menu = this };
		Items.Add(ret);
		return ret;
	}

	internal bool IsDirty { get; set; }
	public void Display()
	{
		IsDirty = true;
		if (IsActive)
		{
			MenuState.Refresh();
			return;
		}
		OpenedAt = DateTime.UtcNow;
		MenuState.FocusStack.Add(this);
		if (Ct.CanBeCanceled)
			Ctr = Ct.Register(() => Close());
		MenuState.Refresh();
	}
	public void Close()
	{
		Ctr.Unregister();
		MenuState.FocusStack.Remove(this);
		MenuState.Refresh(sortPriorities: false);
	}

	// IMenuPriorityExtension
	public double _Priority = 0.0;
	public double Priority
	{
		get => _Priority;
		set
		{
			if (value == _Priority)
				return;
			_Priority = value;
			MenuState.Refresh();
		}
	}
	public bool IsFocused => MenuState.FocusStack.Count > 0 && MenuState.FocusStack[0] == this;

	// INavigateBackMenuExtension
	public Action<IMenu>? NavigateBack { get; set; }
}
