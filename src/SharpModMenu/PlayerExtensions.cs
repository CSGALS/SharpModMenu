using System.Numerics;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace SharpModMenu;

public struct EyeAngles
{
	public Vector3 Position { get; set; }
	public Vector3 Angle { get; set; }
	public Vector3 Forward { get; set; }
	public Vector3 Right { get; set; }
	public Vector3 Up { get; set; }
}

internal static class PlayerExtensions
{
	public static CCSPlayerPawn? GetPlayerPawn(this CCSPlayerController player)
	{
		if (player.Pawn.Value is not CBasePlayerPawn pawn)
			return null;

		if (pawn.LifeState == (byte)LifeState_t.LIFE_DEAD)
		{
			if (pawn.ObserverServices?.ObserverTarget.Value?.As<CBasePlayerPawn>() is not CBasePlayerPawn observer)
				return null;
			pawn = observer;
		}
		return pawn.As<CCSPlayerPawn>();
	}

	public static Vector _Forward = new(), _Right = new(), _Up = new();
	public static EyeAngles? GetEyeAngles(this CCSPlayerController player)
	{
		var playerPawn = GetPlayerPawn(player);
		if (playerPawn is null)
			return null;

		var eyeAngles = playerPawn!.EyeAngles;
		NativeAPI.AngleVectors(eyeAngles.Handle, _Forward.Handle, _Right.Handle, _Up.Handle);

		var origin = new Vector3(playerPawn.AbsOrigin!.X, playerPawn.AbsOrigin!.Y, playerPawn.AbsOrigin!.Z);
		var viewOffset = new Vector3(playerPawn.ViewOffset.X, playerPawn.ViewOffset.Y, playerPawn.ViewOffset.Z);

		return new()
		{
			Position = origin + viewOffset,
			Angle = new Vector3(eyeAngles.X, eyeAngles.Y, eyeAngles.Z),
			Forward = new Vector3(_Forward.X, _Forward.Y, _Forward.Z),
			Right = new Vector3(_Right.X, _Right.Y, _Right.Z),
			Up = new Vector3(_Up.X, _Up.Y, _Up.Z),
		};
	}

	public static CCSGOViewModel? GetPredictedViewmodel(this CCSPlayerController player)
	{
		var pawn = GetPlayerPawn(player);
		if (pawn?.ViewModelServices is null)
			return null;

		var offset = Schema.GetSchemaOffset("CCSPlayer_ViewModelServices", "m_hViewModel");
		nint viewmodelHandleAddress = pawn.ViewModelServices.Handle + offset + 4;

		var handle = new CHandle<CCSGOViewModel>(viewmodelHandleAddress);
		if (!handle.IsValid)
		{
			var viewmodel = Utilities.CreateEntityByName<CCSGOViewModel>("predicted_viewmodel")!;
			viewmodel.DispatchSpawn();
			handle.Raw = viewmodel.EntityHandle.Raw;
			Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_pViewModelServices");
		}

		return handle.Value;
	}
}
