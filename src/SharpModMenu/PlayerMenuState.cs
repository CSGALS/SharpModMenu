using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

using CSSUniversalMenuAPI;

using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace SharpModMenu;

internal class PlayerMenuState : IDisposable
{
	public required CCSPlayerController Player { get; init; }
	public required MenuDriver Driver { get; init; }

	public List<Menu> FocusStack { get; } = new();
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
	public bool CanUseKeybinds { get; set; }
	public bool IsUsingKeybinds => HasKeyBinds && CanUseKeybinds;

	public bool WasPressingForward { get; set; }
	public bool WasPressingBack { get; set; }
	public bool WasPressingLeft { get; set; }
	public bool WasPressingRight { get; set; }
	public bool WasPressingUse { get; set; }
	public bool WasPressingReload { get; set; }
	public bool WasPressingTab { get; set; }

	public int ClientTick { get; set; }
	public int? InhibitWeaponSelection { get; set; }
	private bool _movementFrozen;
	private bool _hasPreviousMoveType;
	private MoveType_t _previousMoveType = MoveType_t.MOVETYPE_WALK;
	private readonly Dictionary<string, int> _selectionIndexByMenuKey = new(StringComparer.Ordinal);
	private bool _pendingHtmlClear;
	private int _pendingHtmlClearGeneration;
	private const int HtmlClearDelayTicks = 32;
	private const int HtmlDisplayDurationTicks = 64;

	public void HandleInput(PlayerKey key, bool fromBind)
	{
		if (key is >= PlayerKey.SelectItem1 and <= PlayerKey.SelectItem0 && fromBind)
			HasKeyBinds = true;

		if (CurrentMenu is null)
			return;

		if (key is >= PlayerKey.SelectItem1 and <= PlayerKey.SelectItem0 && fromBind)
			InhibitWeaponSelection = ClientTick + 1;

		var itemsStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
		var buttonState = CurrentMenu.GetButtonStates();

		switch (key)
		{
			case >= PlayerKey.SelectItem1 and <= PlayerKey.SelectItem7:
				int index = key - PlayerKey.SelectItem1;
				if ((itemsStart + index) < CurrentMenu.Items.Count && CurrentMenu.Items[itemsStart + index] is MenuItem { Enabled: true } menuItem)
				{
					CurrentMenu.SelectionIndex = index;
					RememberCurrentSelection(CurrentMenu);
					menuItem.RaiseSelected();
					RememberCurrentSelection(CurrentMenu);
				}
				break;
			case PlayerKey.SelectItem8:
				if (buttonState.ShowPrevButton)
				{
					CurrentMenu.CurrentPage--;
					CurrentMenu.IsDirty = true;
					Refresh(sortPriorities: false);
				}
				else if (buttonState.ShowBackButton)
				{
					if (CurrentMenu.NavigateBack is not null)
						CurrentMenu.NavigateBack(CurrentMenu);
					else
						CurrentMenu.Close();
				}
				break;
			case PlayerKey.Left:
				break;
			case PlayerKey.SelectItem9:
				if (buttonState.ShowNextButton)
				{
					CurrentMenu.CurrentPage++;
					CurrentMenu.IsDirty = true;
					Refresh(sortPriorities: false);
				}
				break;
			case PlayerKey.Right:
				break;
			case PlayerKey.SelectItem0:
			case PlayerKey.Close:
				if (buttonState.ShowExitButton)
					(CurrentMenu as IMenu).Exit();
				break;
			case PlayerKey.Up:
				if (!fromBind &&
					Driver.Options.WasdAutoPage &&
					TryHandleWasdAutoPage(PlayerKey.Up, buttonState))
				{
					break;
				}

				CurrentMenu.SelectionIndex = CurrentMenu.PrevSelectionIndex;
				RememberCurrentSelection(CurrentMenu);
				CurrentMenu.IsDirty = true;
				Refresh(sortPriorities: false);
				break;
			case PlayerKey.Down:
				if (!fromBind &&
					Driver.Options.WasdAutoPage &&
					TryHandleWasdAutoPage(PlayerKey.Down, buttonState))
				{
					break;
				}

				CurrentMenu.SelectionIndex = CurrentMenu.NextSelectionIndex;
				RememberCurrentSelection(CurrentMenu);
				CurrentMenu.IsDirty = true;
				Refresh(sortPriorities: false);
				break;
			case PlayerKey.Select:
				var selectedIndex = CurrentMenu.SelectionIndex;
				var hoveringItem =
					selectedIndex < Menu.ItemsPerPage &&
					(itemsStart + selectedIndex) < CurrentMenu.Items.Count;

				if (hoveringItem && CurrentMenu.Items[itemsStart + selectedIndex] is MenuItem { Enabled: true } selectedMenuItem)
				{
					RememberCurrentSelection(CurrentMenu);
					selectedMenuItem.RaiseSelected();
					RememberCurrentSelection(CurrentMenu);
				}
				else if (selectedIndex == 7)
					goto case PlayerKey.SelectItem8;
				else if (selectedIndex == 8)
					goto case PlayerKey.SelectItem9;
				else if (selectedIndex == 9)
					goto case PlayerKey.Close;
				break;
			default:
				break;
		}
	}

	private bool TryHandleWasdAutoPage(PlayerKey key, MenuButtonState buttonState)
	{
		if (CurrentMenu is null)
			return false;

		var itemsStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
		var itemsRemaining = CurrentMenu.Items.Count - itemsStart;
		var itemsVisible = Math.Min(Menu.ItemsPerPage, Math.Max(0, itemsRemaining));
		if (itemsVisible <= 0)
			return false;

		if (key == PlayerKey.Up &&
			CurrentMenu.SelectionIndex == 0 &&
			buttonState.ShowPrevButton)
		{
			var currentSelectionIndex = CurrentMenu.SelectionIndex;
			CurrentMenu.CurrentPage--;
			var prevPageStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
			var prevPageRemaining = CurrentMenu.Items.Count - prevPageStart;
			var prevPageItemsVisible = Math.Min(Menu.ItemsPerPage, Math.Max(0, prevPageRemaining));
			CurrentMenu.SelectionIndex = Math.Min(currentSelectionIndex, Math.Max(0, prevPageItemsVisible - 1));
			RememberCurrentSelection(CurrentMenu);
			CurrentMenu.IsDirty = true;
			Refresh(sortPriorities: false);
			return true;
		}

		if (key == PlayerKey.Down &&
			CurrentMenu.SelectionIndex == itemsVisible - 1 &&
			buttonState.ShowNextButton)
		{
			var currentSelectionIndex = CurrentMenu.SelectionIndex;
			CurrentMenu.CurrentPage++;
			var nextPageStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
			var nextPageRemaining = CurrentMenu.Items.Count - nextPageStart;
			var nextPageItemsVisible = Math.Min(Menu.ItemsPerPage, Math.Max(0, nextPageRemaining));
			CurrentMenu.SelectionIndex = Math.Min(currentSelectionIndex, Math.Max(0, nextPageItemsVisible - 1));
			RememberCurrentSelection(CurrentMenu);
			CurrentMenu.IsDirty = true;
			Refresh(sortPriorities: false);
			return true;
		}

		return false;
	}

	public bool PresentingHtml { get; set; }
	private bool _MenuActive;
	private bool MenuActive
	{
		get => _MenuActive;
		set
		{
			if (value == _MenuActive)
				return;
			_MenuActive = value;
			if (value)
			{
				CancelPendingHtmlClear();
				Driver.ActiveMenuStates.Add(this);
			}
			else
			{
				Driver.ActiveMenuStates.Remove(this);
				if (ShowingHtmlContent)
					QueueHtmlClear();
				SetMovementFrozen(false);
			}
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
		if (CurrentMenu is not null)
			TryApplyRememberedSelection(CurrentMenu);
		MenuActive = CurrentMenu is not null;

		if (CreateInitialInvisibleWorldTextEntity())
		{
			ForceRefresh = true;
			UpdateMovementFreezeState();
			return;
		}

		if (DrawActiveMenu())
		{
			PresentingHtml = false;
		}
		else
		{
			if (!PresentingHtml)
				DestroyEntities();
			DrawActiveMenuHtml();
			PresentingHtml = true;
		}

		UpdateMovementFreezeState();
	}

	public void Dispose()
	{
		DestroyEntities();
		ClearHtmlNow();
		_selectionIndexByMenuKey.Clear();
		MenuActive = false; // force the removal of this menu from the active menus
	}

	private void RememberCurrentSelection(Menu? menu)
	{
		if (menu is null)
			return;

		var visibleItems = GetVisibleItemsInCurrentPage(menu);
		if (visibleItems <= 0)
			return;

		var clampedIndex = Math.Clamp(menu.SelectionIndex, 0, visibleItems - 1);
		_selectionIndexByMenuKey[BuildMenuSelectionKey(menu)] = clampedIndex;
	}

	private bool TryApplyRememberedSelection(Menu? menu)
	{
		if (menu is null)
			return false;

		var menuKey = BuildMenuSelectionKey(menu);
		if (string.IsNullOrEmpty(menuKey))
			return false;

		if (!_selectionIndexByMenuKey.TryGetValue(menuKey, out var rememberedIndex))
			return false;

		var visibleItems = GetVisibleItemsInCurrentPage(menu);
		if (visibleItems <= 0)
			return false;

		var clampedIndex = Math.Clamp(rememberedIndex, 0, visibleItems - 1);
		var changed = menu.SelectionIndex != clampedIndex;
		menu.SelectionIndex = clampedIndex;
		if (changed)
			menu.IsDirty = true;
		return changed;
	}

	private static int GetVisibleItemsInCurrentPage(Menu? menu)
	{
		if (menu is null)
			return 0;

		var itemsStart = menu.CurrentPage * Menu.ItemsPerPage;
		var itemsRemaining = menu.Items.Count - itemsStart;
		return Math.Max(0, Math.Min(Menu.ItemsPerPage, itemsRemaining));
	}

	private static string BuildMenuSelectionKey(Menu? menu)
	{
		if (menu is null)
			return string.Empty;

		var sb = new StringBuilder();
		for (int i = 0; i < menu.Parents.Count; i++)
		{
			if (i > 0)
				sb.Append('\u001F');
			sb.Append(menu.Parents[i].Title);
		}

		if (sb.Length > 0)
			sb.Append('\u001F');
		sb.Append(menu.Title);
		return sb.ToString();
	}

	private void QueueHtmlClear()
	{
		_pendingHtmlClear = true;
		var clearGeneration = ++_pendingHtmlClearGeneration;

		Server.RunOnTick(Server.TickCount + HtmlClearDelayTicks, () =>
		{
			if (!_pendingHtmlClear || _pendingHtmlClearGeneration != clearGeneration)
				return;

			if (CurrentMenu is null)
				ClearHtmlNow();
			else
				CancelPendingHtmlClear();
		});
	}

	private void CancelPendingHtmlClear()
	{
		_pendingHtmlClear = false;
		_pendingHtmlClearGeneration++;
	}

	private void ClearHtmlNow()
	{
		if (!ShowingHtmlContent && HtmlContent is null)
		{
			CancelPendingHtmlClear();
			return;
		}

		ShowingHtmlContent = false;
		Player.PrintToCenterHtml(HtmlContent ?? "<font></font>", 0);
		HtmlContent = null;
		CancelPendingHtmlClear();
	}

	private CPointWorldText? HighlightText { get; set; }
	private CPointWorldText? ForegroundText { get; set; }
	private CPointWorldText? BackgroundText { get; set; }
	private CPointWorldText? Background { get; set; }
	private void DestroyEntities()
	{
		Driver.MenuEntities.RemoveAll(x => x.target == Player);

		if (HighlightText is not null && HighlightText.IsValid)
			HighlightText?.Remove();

		if (ForegroundText is not null && ForegroundText.IsValid)
			ForegroundText?.Remove();

		if (BackgroundText is not null && BackgroundText.IsValid)
			BackgroundText?.Remove();

		if (Background is not null && Background.IsValid)
			Background?.Remove();

		ForegroundText = BackgroundText = Background = null;
	}

	private void CreateEntities()
	{
		var options = Driver.Options;
		HighlightText = CreateWorldText(textColor: options.WorldTextHighlightColor, false, 0.001f);
		ForegroundText = CreateWorldText(textColor: options.WorldTextForegroundColor, false, 0.000f);
		BackgroundText = CreateWorldText(textColor: options.WorldTextBackgroundTextColor, false, -0.001f);
		Background = CreateWorldText(textColor: options.WorldTextPanelColor, true, -0.002f);
	}

	// sometimes creating an ent for a pawn requires it to be done twice, so
	// do it twice whenever our observed entity changes
	private nint? _CreatedFor = null;
	/// <summary>
	/// The first world text isn't shown for some reason, this creates a barebones version then immediately destroys it
	/// </summary>
	private bool CreateInitialInvisibleWorldTextEntity()
	{
		return false;
		var observerInfo = Player.GetObserverInfo();

		if (_CreatedFor.HasValue && _CreatedFor.Value == observerInfo.Observing?.Handle)
			return false;

		var viewmodel = observerInfo.GetPredictedViewmodel();
		if (viewmodel is null)
			return false;

		var entity = CreateWorldText(Color.Orange, drawBackground: false, depthOffset: 0.0f);
		if (entity is null)
			return false;

		var maybeAngles = observerInfo.GetEyeAngles();
		if (!maybeAngles.HasValue)
			return false;

		UpdateEntity(entity, viewmodel, "Hey", maybeAngles.Value.Position, maybeAngles.Value.Angle, updateText: true, updateParent: true);
		entity.Remove();

		_CreatedFor = observerInfo.Observing?.Handle ?? nint.Zero;

		Console.WriteLine("CreateInitialInvisibleWorldTextEntity(): DONE");
		return true;
	}

	private CPointWorldText CreateWorldText(
		Color textColor,
		bool drawBackground,
		float depthOffset,
		string text = "")
	{
		var ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext")!;
		var options = Driver.Options;

		if (ent is not { IsValid: true })
			throw new Exception("CreateWorldText(): Failed to create entity");

		Driver.MenuEntities.Add((ent, Player));

		ent.MessageText = text; // limit of 512 chars
		ent.Enabled = true;
		ent.FontName = options.WorldTextFontName;
		ent.FontSize = options.WorldTextFontSize;
		ent.Fullbright = true;
		ent.Color = textColor;
		ent.WorldUnitsPerPx = 0.0085f;
		ent.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
		ent.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
		ent.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
		ent.RenderMode = RenderMode_t.kRenderNormal;
		ent.DrawBackground = drawBackground;
		ent.BackgroundBorderHeight = 0.1f;
		ent.BackgroundBorderWidth = 0.1f;
		ent.BackgroundWorldToUV = 0.05f;
		//ent.ForceRecreateNextTick = true; //  m_bForceRecreateNextUpdate  
		//ent.BackgroundMaterialName = ""; //  m_BackgroundMaterialName
		//ent.OwnerEntity = CBaseViewModel;
		ent.DepthOffset = depthOffset;
		ent.DispatchSpawn();
		return ent;
	}

	private static Vector _Pos = new();
	private static QAngle _Ang = new();
	private void UpdateEntity(
		CPointWorldText ent,
		CBaseEntity? viewmodel,
		string newText,
		Vector3 position,
		Vector3 angles,
		bool updateText = true,
		bool updateParent = true)
	{
		_Pos.X = position.X;
		_Pos.Y = position.Y;
		_Pos.Z = position.Z;
		_Ang.X = angles.X;
		_Ang.Y = angles.Y;
		_Ang.Z = angles.Z;

		if (updateText)
			ent.MessageText = newText;
		ent.Teleport(_Pos, _Ang, null);

		if (updateParent)
			ent.AcceptInput("SetParent", viewmodel, null, "!activator");

		if (updateText)
			Utilities.SetStateChanged(ent, "CPointWorldText", "m_messageText");
	}

	private Vector2 _MenuPosition = new(-6.55f, 0.0f);
	public Vector2 MenuPosition
	{
		get => _MenuPosition;
		set
		{
			_MenuPosition = value;
			if (CurrentMenu is not null)
			{
				CurrentMenu.IsDirty = true;
				Refresh(false);
			}
		}
	}

	private Menu? LastPresented { get; set; }
	private readonly StringBuilder HighlightTextSb = new();
	private readonly StringBuilder ForegroundTextSb = new();
	private readonly StringBuilder BackgroundTextSb = new();
	private readonly StringBuilder BackgroundSb = new();
	private nint MenuCurrentObserver { get; set; } = nint.Zero;
	private ObserverMode MenuCurrentObserverMode { get; set; }
	private CBaseEntity? MenuCurrentViewmodel { get; set; }

	public bool DrawActiveMenu()
	{
		return false; // nothing to parent to right now, further work needed

		if (ReferenceEquals(CurrentMenu, LastPresented))
		{
			if (CurrentMenu is not null && !CurrentMenu.IsDirty)
				return true;
		}
		else
		{
			// setting these here inhibit existing player inputs from being registered, requiring they at least be released first
			WasPressingForward = true;
			WasPressingBack = true;
			WasPressingLeft = true;
			WasPressingRight = true;
			WasPressingReload = true;
			WasPressingUse = true;
			WasPressingTab = true;
		}

		if (CurrentMenu is null)
		{
			if (LastPresented is not null)
				DestroyEntities();
			LastPresented = CurrentMenu;
			return true;
		}
		LastPresented = CurrentMenu;

		CurrentMenu.IsDirty = false;

		var observerInfo = Player.GetObserverInfo();
		CanUseKeybinds = observerInfo.Mode == ObserverMode.FirstPerson && observerInfo.Observing?.Index == Player.Pawn.Index;
		if (observerInfo.Mode != ObserverMode.FirstPerson)
			return false;

		var maybeEyeAngles = observerInfo.GetEyeAngles();
		if (!maybeEyeAngles.HasValue)
			return false;
		var eyeAngles = maybeEyeAngles.Value;

		var predictedViewmodel = observerInfo.GetPredictedViewmodel();
		if (predictedViewmodel is null)
			return false;

		HighlightTextSb.Clear();
		ForegroundTextSb.Clear();
		BackgroundTextSb.Clear();
		BackgroundSb.Clear();

		bool firstLine = true;
		void writeLine(string text, TextStyling style, int? selectionIndex)
		{
			if (firstLine)
				firstLine = false;
			else
			{
				HighlightTextSb.AppendLine();
				ForegroundTextSb.AppendLine();
				BackgroundTextSb.AppendLine();
				BackgroundSb.AppendLine();
			}

			int newlineCount = 0;
			for (int i = 0; i < text.Length; i++)
				if (text[i] == '\n')
					newlineCount++;
			var syncNewlines = newlineCount == 0 ? string.Empty : new string('\n', newlineCount);

			var sb = style.Foreground ? ForegroundTextSb : BackgroundTextSb;
			var formattedLine = text;

			if (selectionIndex.HasValue)
				formattedLine = FormatSelectableLineForWorld(formattedLine, style.Highlight);
			
			sb.Append(formattedLine);
			BackgroundSb.Append(formattedLine);
			if (style.Highlight)
				HighlightTextSb.Append(formattedLine);

			// keep newlines in sync with the other entities
			if (sb == ForegroundTextSb)
				BackgroundTextSb.Append(syncNewlines);
			else if (sb == BackgroundTextSb)
				ForegroundTextSb.Append(syncNewlines);
			if (!style.Highlight)
				HighlightTextSb.Append(syncNewlines);
		}

		BuildMenuStrings(CurrentMenu, writeLine);

		var position = eyeAngles.Position + eyeAngles.Forward * 7.0f + eyeAngles.Right * MenuPosition.X + eyeAngles.Up * MenuPosition.Y;
		var angle = new Vector3()
		{
			Y = eyeAngles.Angle.Y + 270.0f, // -90?
			Z = 90.0f - eyeAngles.Angle.X, // +90?
			X = 0.0f
		};

		MenuCurrentObserver = observerInfo.Observing?.Handle ?? nint.Zero;
		MenuCurrentObserverMode = observerInfo.Mode;
		MenuCurrentViewmodel = predictedViewmodel;

		bool allValid =
			(HighlightText?.IsValid ?? false) &&
			(ForegroundText?.IsValid ?? false) &&
			(BackgroundText?.IsValid ?? false) &&
			(Background?.IsValid ?? false);
		if (!allValid)
		{
			DestroyEntities();
			CreateEntities();
		}
		UpdateEntity(HighlightText!, predictedViewmodel, HighlightTextSb.ToString(), position, angle);
		UpdateEntity(ForegroundText!, predictedViewmodel, ForegroundTextSb.ToString(), position, angle);
		UpdateEntity(BackgroundText!, predictedViewmodel, BackgroundTextSb.ToString(), position, angle);
		UpdateEntity(Background!, predictedViewmodel, BackgroundSb.ToString(), position, angle);
		return true;
	}

	private readonly StringBuilder HtmlTextSb = new();
	public string? HtmlContent { get; set; } = null;

	public void DrawActiveMenuHtml()
	{
		var observerInfo = Player.GetObserverInfo();
		CanUseKeybinds = observerInfo.Mode == ObserverMode.FirstPerson && observerInfo.Observing?.Index == Player.Pawn.Index;

		if (CurrentMenu is null)
		{
			HtmlContent = null;
			return;
		}

		bool firstLine = true;
		void writeLine(string text, TextStyling style, int? selectIndex)
		{
			if (text.IndexOf('\n') >= 0)
			{
				text = text.Replace("\n", "<br>");
				while (text.Contains("<br><br>"))
					text = text.Replace("<br><br>", "<br><font color='#000'>.</font><br>");
			}

			if (firstLine)
				firstLine = false;
			else
				HtmlTextSb.Append("<br>");

			var color = style switch
			{
				{ Foreground: true, Highlight: true } => Driver.Options.HtmlForegroundHighlightColor,
				{ Foreground: true, Highlight: false } => Driver.Options.HtmlForegroundColor,
				{ Foreground: false, Highlight: true } => Driver.Options.HtmlBackgroundHighlightColor,
				{ Foreground: false, Highlight: false } => Driver.Options.HtmlBackgroundColor,
			};

			if (string.IsNullOrEmpty(text))
				HtmlTextSb.Append($"<font color='{Driver.Options.HtmlEmptyLineColor}'>.</font>");
			else
				HtmlTextSb.Append($"<font color='{color}'>{FormatSelectableLineForHtml(text, selectIndex.HasValue, style.Highlight)}</font>");
		}

		HtmlTextSb.Clear();
		HtmlTextSb.Append($"<font class='{Driver.Options.HtmlFontClass}'>");
		BuildMenuStrings(CurrentMenu, writeLine);
		HtmlTextSb.Append("</font>");

		HtmlContent = HtmlTextSb.ToString();
	}

	private struct TextStyling
	{
		public bool Foreground { get; set; }
		public bool Highlight { get; set; }
	}

	private string FormatSelectableLineForWorld(string text, bool isSelected)
	{
		if (!isSelected)
			return $"  {text}";

		var icon = GetSelectorIcon();
		return $"{icon} {text}";
	}

	private string FormatSelectableLineForHtml(string text, bool isSelectable, bool isSelected)
	{
		if (!isSelectable)
			return text;

		if (!isSelected)
			return $"&#160;&#160;{text}";

		var icon = GetSelectorIcon();
		var iconColor = Driver.Options.SelectorIconColor;
		var iconMarkup = $"<font color='{iconColor}'>{icon}</font>";
		return $"{iconMarkup} {text}";
	}

	private string GetSelectorIcon()
	{
		var icon = Driver.Options.SelectorIcon;
		if (string.IsNullOrWhiteSpace(icon))
			return "\u279C";

		icon = icon.Replace("\r", string.Empty, StringComparison.Ordinal)
			.Replace("\n", string.Empty, StringComparison.Ordinal)
			.Trim();
		return string.IsNullOrWhiteSpace(icon) ? "\u279C" : icon;
	}

	private void BuildMenuStrings(Menu currentMenu, Action<string, TextStyling, int?> writeLine)
	{
		writeLine(currentMenu.Title, default, null);

		var itemsStart = currentMenu.CurrentPage * Menu.ItemsPerPage;
		var itemsInPage = Math.Min(currentMenu.Items.Count, itemsStart + Menu.ItemsPerPage) - itemsStart;

		for (int i = 0; i < itemsInPage; i++)
		{
			var item = currentMenu.Items[itemsStart + i];

			var textStyle = new TextStyling
			{
				Highlight = !IsUsingKeybinds && i == currentMenu.SelectionIndex,
				Foreground = item.Enabled
			};

			writeLine(item.Title, textStyle, i + 1);
			if (item.Subtitle is not null)
				writeLine($"  {item.Subtitle}", default, null);
		}

		var btnStates = currentMenu.GetButtonStates();
		var previousText = Driver.Translate(Player, "nav.previous", "Previous");
		var backText = Driver.Translate(Player, "nav.back", "Back");
		var nextText = Driver.Translate(Player, "nav.next", "Next");
		var closeText = Driver.Translate(
			Player,
			"nav.close",
			Driver.Translate(Player, "nav.exit", "Close"));

		int maxItemsPerPage = Math.Min(Menu.ItemsPerPage, currentMenu.Items.Count);
		int blankLines = maxItemsPerPage - itemsInPage + 1;
		var useCompactHtmlFooter =
			PresentingHtml &&
			!string.Equals(Driver.Options.HtmlFontClass, "fontSize-s", StringComparison.OrdinalIgnoreCase);

		if (useCompactHtmlFooter)
		{
			blankLines = 0;
		}

		for (int i = 0; i < blankLines; i++)
			writeLine(string.Empty, default, null);

		var backPrevEnabled = btnStates.ShowPrevButton || btnStates.ShowBackButton;
		var backPrevStyle = new TextStyling()
		{
			Foreground = backPrevEnabled,
			Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 7,
		};
		var backPrevText = btnStates.ShowPrevButton ? previousText : btnStates.ShowBackButton ? backText : previousText;
		if (useCompactHtmlFooter)
		{
			var backPrevLabel = backPrevEnabled ? $"8. {backPrevText}" : $"8. ({backPrevText})";
			var nextLabel = btnStates.ShowNextButton ? $"9. {nextText}" : $"9. ({nextText})";
			var closeLabel = btnStates.ShowExitButton ? $"0. {closeText}" : $"0. ({closeText})";
			var compactFallback = $"{backPrevLabel}   {nextLabel}   {closeLabel}";
			var compactFooterText = string.IsNullOrWhiteSpace(Driver.Options.HtmlCompactFooterMessage)
				? Driver.Translate(Player, "nav.compact_message", compactFallback)
				: Driver.Options.HtmlCompactFooterMessage;

			if (string.IsNullOrWhiteSpace(compactFooterText))
				compactFooterText = compactFallback;

			var compactFooterFontClass = Driver.Options.HtmlCompactFooterFontClass;
			if (!string.IsNullOrWhiteSpace(compactFooterFontClass))
			{
				compactFooterFontClass = compactFooterFontClass
					.Replace("'", string.Empty, StringComparison.Ordinal)
					.Replace("\"", string.Empty, StringComparison.Ordinal)
					.Trim();
				if (!string.IsNullOrWhiteSpace(compactFooterFontClass))
				{
					compactFooterText = $"<font class='{compactFooterFontClass}'>{compactFooterText}</font>";
				}
			}

			writeLine(compactFooterText, new TextStyling { Foreground = true }, null);
			return;
		}

		writeLine(backPrevText, backPrevStyle, 8);

		var nextStyle = new TextStyling()
		{
			Foreground = btnStates.ShowNextButton,
			Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 8,
		};
		writeLine(nextText, nextStyle, 9);

		var closeStyle = new TextStyling()
		{
			Foreground = btnStates.ShowExitButton,
			Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 9,
		};
		writeLine(closeText, closeStyle, 0);
	}

	private void UpdateMovementFreezeState()
	{
		SetMovementFrozen(ShouldFreezePlayerMovement());
	}

	private bool ShouldFreezePlayerMovement()
	{
		if (CurrentMenu is null)
			return false;

		if (!Player.IsValid || Player.IsBot)
			return false;

		var pawn = Player.PlayerPawn.Value;
		return pawn is { IsValid: true };
	}

	private void SetMovementFrozen(bool freeze)
	{
		var pawn = Player.PlayerPawn.Value;
		if (pawn is not { IsValid: true })
		{
			if (!freeze)
			{
				_movementFrozen = false;
				_hasPreviousMoveType = false;
			}

			return;
		}

		if (freeze)
		{
			if (_movementFrozen)
				return;

			_previousMoveType = pawn.MoveType;
			_hasPreviousMoveType = true;

			var freezeMoveType = MoveType_t.MOVETYPE_NONE;
			pawn.MoveType = freezeMoveType;
			Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
			Schema.GetRef<MoveType_t>(pawn.Handle, "CBaseEntity", "m_nActualMoveType") = freezeMoveType;
			_movementFrozen = true;
			return;
		}

		if (!_movementFrozen)
			return;

		var restoredMoveType = _hasPreviousMoveType ? _previousMoveType : MoveType_t.MOVETYPE_WALK;
		pawn.MoveType = restoredMoveType;
		Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
		Schema.GetRef<MoveType_t>(pawn.Handle, "CBaseEntity", "m_nActualMoveType") = restoredMoveType;
		_movementFrozen = false;
		_hasPreviousMoveType = false;
	}

	public bool ForceRefresh = true;
	public bool ShowingHtmlContent;
	public void Tick()
	{
		if (CurrentMenu is null)
		{
			SetMovementFrozen(false);
			return;
		}

		if (PresentingHtml && HtmlContent is not null)
		{
			ShowingHtmlContent = true;
			Player.PrintToCenterHtml(HtmlContent, HtmlDisplayDurationTicks);
		}

		var observerInfo = Player.GetObserverInfo();
		CanUseKeybinds = observerInfo.Mode == ObserverMode.FirstPerson && observerInfo.Observing?.Index == Player.Pawn.Index;
		UpdateMovementFreezeState();

		var observerHandle = observerInfo.Observing?.Handle ?? nint.Zero;
		bool observerChanged =
			observerInfo.Mode != MenuCurrentObserverMode ||
			observerHandle != MenuCurrentObserver;
		if (observerChanged)
		{
			MenuCurrentObserverMode = observerInfo.Mode;
			MenuCurrentObserver = observerHandle;
		}

		bool refresh =
			ForceRefresh ||
			(!PresentingHtml && observerChanged);

		if (refresh)
		{
			ForceRefresh = false;
			CurrentMenu.IsDirty = true;
			Refresh(sortPriorities: false);
		}
	}
}
