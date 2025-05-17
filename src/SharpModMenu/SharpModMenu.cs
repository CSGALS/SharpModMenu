using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

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

	// near: "%sRecv usercmd %d.  Margin:%5.1fms net +%2d queue =%5.1f total\n"
	// https://github.com/CharlesBarone/CSSharp-Fixes/blob/9f20129bd4d17dcf7f30311ac0d24ec56895b04a/gamedata/cssharpfixes.json#L44
	private static string ProcessUserCmdsSig => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		? "48 8B C4 44 88 48 20 44 89 40 18 48 89 50 10 53"
		: "55 48 89 E5 41 57 41 56 41 89 D6 41 55 41 54 49 89 FC 53 48 83 EC 38";
	// void* FASTCALL ProcessUsercmds(CCSPlayerController* player, CUserCmdPB* cmds, int cmdCount, bool paused, float margin);
	private static MemoryFunctionVoid<nint, nint, int, bool, float, nint> ProcessUserCmdsFunc { get; } = new(ProcessUserCmdsSig, Addresses.ServerPath);

	public override void Load(bool hotReload)
	{
		DriverInstance = new();

		ProcessUserCmdsFunc.Hook(ProcessUserCmds, HookMode.Pre);

		UniversalMenu.RegisterDriver("SharpModMenu", DriverInstance);

		RegisterListener<OnTick>(OnTick);
		RegisterListener<CheckTransmit>(OnCheckTransmit);
	}

	public override void Unload(bool hotReload)
	{
		UniversalMenu.UnregisterDriver("SharpModMenu");

		ProcessUserCmdsFunc.Unhook(ProcessUserCmds, HookMode.Pre);
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

		for (ulong i = 0; i < (ulong)cmdsCount; i++)
		{
			var cmd = (CUserCmdPB*)((ulong)cmdsPtr + i * CUserCmdPB.Size);
			
			if ((nint)cmd->Base == nint.Zero)
				continue;

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

			if (menuState.IsUsingKeybinds)
			{
				cmd->Base->WeaponSelect = 0;
			}
			else
			{
				cmd->Base->SideMove = 0.0f;
				cmd->Base->ForwardMove = 0.0f;
				cmd->Base->UpMove = 0.0f;

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
			if (pressingLeft && pressingLeft != menuState.WasPressingLeft)
				menuState.HandleInput(PlayerKey.Left, false);
			if (pressingRight && pressingRight != menuState.WasPressingRight)
				menuState.HandleInput(PlayerKey.Right, false);
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

	[ConsoleCommand("css_1")]
	public void Css1(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem1, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_2")]
	public void Css2(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem2, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_3")]
	public void Css3(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem3, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_4")]
	public void Css4(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem4, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_5")]
	public void Css5(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem5, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_6")]
	public void Css6(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem6, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_7")]
	public void Css7(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem7, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_8")]
	public void Css8(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem8, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_9")]
	public void Css9(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem9, info.CallingContext == CommandCallingContext.Console);
	}

	[ConsoleCommand("css_0")]
	public void Css0(CCSPlayerController player, CommandInfo info)
	{
		if (player is null || !player.IsValid)
			return;
		var menuState = DriverInstance?.GetMenuState(player, create: true);
		if (menuState is null)
			return;
		menuState.HandleInput(PlayerKey.SelectItem0, info.CallingContext == CommandCallingContext.Console);
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
