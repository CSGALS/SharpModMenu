using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Numerics;
using System.Text;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
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

	public void HandleInput(PlayerKey key, bool fromBind)
	{
		if (key is >= PlayerKey.SelectItem1 and <= PlayerKey.SelectItem0 && fromBind)
			HasKeyBinds = true;

		if (CurrentMenu is null)
			return;

		var itemsStart = CurrentMenu.CurrentPage * Menu.ItemsPerPage;
		var buttonState = CurrentMenu.GetButtonStates();

		switch (key)
		{
			case >= PlayerKey.SelectItem1 and <= PlayerKey.SelectItem7:
				int index = key - PlayerKey.SelectItem1;
				if ((itemsStart + index) < CurrentMenu.Items.Count && CurrentMenu.Items[itemsStart + index] is MenuItem { Enabled: true } menuItem)
					menuItem.RaiseSelected();
				break;
			case PlayerKey.SelectItem8:
			case PlayerKey.Left:
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
			case PlayerKey.SelectItem9:
			case PlayerKey.Right:
				if (buttonState.ShowNextButton)
				{
					CurrentMenu.CurrentPage++;
					CurrentMenu.IsDirty = true;
					Refresh(sortPriorities: false);
				}
				break;
			case PlayerKey.SelectItem0:
			case PlayerKey.Close:
				if (buttonState.ShowExitButton)
					(CurrentMenu as IMenu).Exit();
				break;
			case PlayerKey.Up:
				CurrentMenu.SelectionIndex = CurrentMenu.PrevSelectionIndex;
				CurrentMenu.IsDirty = true;
				Refresh(sortPriorities: false);
				break;
			case PlayerKey.Down:
				CurrentMenu.SelectionIndex = CurrentMenu.NextSelectionIndex;
				CurrentMenu.IsDirty = true;
				Refresh(sortPriorities: false);
				break;
			case PlayerKey.Select:
				var selectedIndex = CurrentMenu.SelectionIndex;
				var hoveringItem =
					selectedIndex < Menu.ItemsPerPage &&
					(itemsStart + selectedIndex) < CurrentMenu.Items.Count;

				if (hoveringItem && CurrentMenu.Items[itemsStart + selectedIndex] is MenuItem { Enabled: true } selectedMenuItem)
					selectedMenuItem.RaiseSelected();
				else if (selectedIndex == 7)
					goto case PlayerKey.Left;
				else if (selectedIndex == 8)
					goto case PlayerKey.Right;
				else if (selectedIndex == 9)
					goto case PlayerKey.Close;
				break;
			default:
				break;
		}
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
				Driver.ActiveMenuStates.Add(this);
			else
				Driver.ActiveMenuStates.Remove(this);
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
		MenuActive = CurrentMenu is not null;

		if (CreateInitialInvisibleWorldTextEntity())
		{
			ForceRefresh = true;
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
	}

	public void Dispose()
	{
		DestroyEntities();
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

	private static readonly Color HighlightTextColor = Color.FromArgb(247, 72, 67);
	private static readonly Color ForegroundTextColor = Color.FromArgb(229, 150, 32); // 245, 177, 103 with a white bg, maybe 240, 160, 30 at 95% opacity?
	private static readonly Color BackgroundTextColor = Color.FromArgb(234, 209, 175);

	private void CreateEntities()
	{
		HighlightText = CreateWorldText(textColor: HighlightTextColor, false, -0.000f);
		ForegroundText = CreateWorldText(textColor: ForegroundTextColor, false, -0.000f);
		BackgroundText = CreateWorldText(textColor: BackgroundTextColor, false, -0.001f);
		Background = CreateWorldText(textColor: Color.FromArgb(200, 127, 127, 127), true, -0.002f);
	}

	// sometimes creating an ent for a pawn requires it to be done twice, so
	// do it twice whenever our observed entity changes
	private nint? _CreatedFor = null;
	/// <summary>
	/// The first world text isn't shown for some reason, this creates a barebones version then immediately destroys it
	/// </summary>
	private bool CreateInitialInvisibleWorldTextEntity()
	{
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
		string text = "",
		int fontSize = 25,
		string fontName = "Tahoma Bold")
	{
		var ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext")!;

		if (ent is not { IsValid: true })
			throw new Exception("CreateWorldText(): Failed to create entity");

		Driver.MenuEntities.Add((ent, Player));

		ent.MessageText = text; // limit of 512 chars
		ent.Enabled = true;
		ent.FontName = fontName;
		ent.FontSize = fontSize;
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
		CCSGOViewModel? viewmodel,
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

	private readonly Vector MenuPosition = new(-6.9f, 0.0f);

	private Menu? LastPresented { get; set; }
	private readonly StringBuilder HighlightTextSb = new();
	private readonly StringBuilder ForegroundTextSb = new();
	private readonly StringBuilder BackgroundTextSb = new();
	private readonly StringBuilder BackgroundSb = new();
	private nint MenuCurrentObserver { get; set; } = nint.Zero;
	private ObserverMode MenuCurrentObserverMode { get; set; }
	private CCSGOViewModel? MenuCurrentViewmodel { get; set; }
	public bool DrawActiveMenu()
	{
		if (ReferenceEquals(CurrentMenu, LastPresented))
		{
			if (CurrentMenu is not null && !CurrentMenu.IsDirty)
				return true;
		}
		WasPressingForward = WasPressingBack = WasPressingLeft = WasPressingRight = WasPressingReload = WasPressingUse = WasPressingTab = true;

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
		if (observerInfo.Mode != ObserverMode.FirstPerson)
		{
			CanUseKeybinds = false;
			return false;
		}
		CanUseKeybinds = observerInfo.Mode == ObserverMode.FirstPerson && observerInfo.Observing?.Index == Player.Pawn.Index;

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
		int linesWrote = 0;
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

			StringBuilder? sb = null;

			if (style.Highlight)
				HighlightTextSb.Append("[　]");

			sb = style.Foreground ? ForegroundTextSb : BackgroundTextSb;
			if (selectionIndex.HasValue)
			{
				sb.Append($"{selectionIndex}. ");
				BackgroundSb.Append($"{selectionIndex}. ");
			}
			sb.Append(text);
			BackgroundSb.Append(text);

			linesWrote++;
		}

		BuildMenuStrings(CurrentMenu, writeLine);

		var position = eyeAngles.Position + eyeAngles.Forward * 7.0f + eyeAngles.Right * MenuPosition.X + eyeAngles.Up * MenuPosition.Y;
		var highlightPosition = position + eyeAngles.Right * -0.055f;
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
		UpdateEntity(HighlightText!, predictedViewmodel, HighlightTextSb.ToString(), highlightPosition, angle);
		UpdateEntity(ForegroundText!, predictedViewmodel, ForegroundTextSb.ToString(), position, angle);
		UpdateEntity(BackgroundText!, predictedViewmodel, BackgroundTextSb.ToString(), position, angle);
		UpdateEntity(Background!, predictedViewmodel, BackgroundSb.ToString(), position, angle);
		return true;
	}

	private readonly StringBuilder HtmlTextSb = new();
	public string? HtmlContent { get; set; } = null;

	public void DrawActiveMenuHtml()
	{
		if (CurrentMenu is null)
		{
			HtmlContent = null;
			return;
		}

		bool firstLine = true;
		int linesWrote = 0;
		void writeLine(string text, TextStyling style, int? selectIndex)
		{
			if (string.IsNullOrEmpty(text))
				return;

			if (firstLine)
				firstLine = false;
			else
				HtmlTextSb.Append("<br>");

			var color = style switch
			{
				{ Highlight: true } => "#F74843",
				{ Foreground: true } => "#E28B12",
				_ => "#E7CCA5",
			};

			var selectionPrefix = selectIndex.HasValue ? $"/{selectIndex.Value} " : string.Empty;
			HtmlTextSb.Append($"<font color='{color}'>{selectionPrefix}{text}</font>");
			linesWrote++;
		}

		HtmlTextSb.Clear();
		HtmlTextSb.Append("<font class='fontSize-s'>");
		BuildMenuStrings(CurrentMenu, writeLine);
		HtmlTextSb.Append("</font>");

		HtmlContent = HtmlTextSb.ToString();
	}

	private struct TextStyling
	{
		public bool Foreground { get; set; }
		public bool Highlight { get; set; }
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
		if (btnStates.ShowNavigation || btnStates.ShowExitButton)
		{
			int maxItemsPerPage = Math.Min(Menu.ItemsPerPage, currentMenu.Items.Count);
			int blankLines = maxItemsPerPage - itemsInPage + 1;

			for (int i = 0; i < blankLines; i++)
				writeLine(string.Empty, default, null);

			if (btnStates.ShowNavigation)
			{
				var backPrevStyle = new TextStyling()
				{
					Foreground = true,
					Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 7,
				};

				if (btnStates.ShowPrevButton)
					writeLine("Previous", backPrevStyle, 8);
				else if (btnStates.ShowBackButton)
					writeLine("Back", backPrevStyle, 8);
				else
					writeLine(string.Empty, backPrevStyle, null);

				var nextStyle = new TextStyling()
				{
					Foreground = true,
					Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 8,
				};
				if (btnStates.ShowNextButton)
					writeLine("Next", nextStyle, 9);
				else
					writeLine(string.Empty, nextStyle, null);
			}

			if (btnStates.ShowExitButton)
			{
				var exitStyle = new TextStyling()
				{
					Foreground = false,
					Highlight = !IsUsingKeybinds && currentMenu.SelectionIndex == 9,
				};
				bool highlightBackPrev = !IsUsingKeybinds && currentMenu.SelectionIndex == 9;
				writeLine("Exit", exitStyle, 0);
			}
		}
	}

	public bool ForceRefresh = true;
	public void Tick()
	{
		if (CurrentMenu is null)
			return;

		if (PresentingHtml && HtmlContent is not null)
			Player.PrintToCenterHtml(HtmlContent);

		var observerInfo = Player.GetObserverInfo();

		bool refresh =
			ForceRefresh ||
			observerInfo.Mode != MenuCurrentObserverMode ||
			observerInfo.Observing?.Handle != MenuCurrentObserver;

		if (refresh)
		{
			ForceRefresh = false;
			CurrentMenu.IsDirty = true;
			Refresh(sortPriorities: false);
		}
	}
}
