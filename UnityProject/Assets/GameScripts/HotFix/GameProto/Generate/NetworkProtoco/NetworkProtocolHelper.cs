using System.Runtime.CompilerServices;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using System.Collections.Generic;
#pragma warning disable CS8618
namespace Fantasy
{
   public static class NetworkProtocolHelper
   {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<A2C_RegisterResponse> C2A_RegisterRequest(this Session session, C2A_RegisterRequest C2A_RegisterRequest_request)
		{
			return (A2C_RegisterResponse)await session.Call(C2A_RegisterRequest_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<A2C_RegisterResponse> C2A_RegisterRequest(this Session session, string userName, string password)
		{
			using var C2A_RegisterRequest_request = Fantasy.C2A_RegisterRequest.Create();
			C2A_RegisterRequest_request.UserName = userName;
			C2A_RegisterRequest_request.Password = password;
			return (A2C_RegisterResponse)await session.Call(C2A_RegisterRequest_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<A2C_LoginResponse> C2A_LoginRequest(this Session session, C2A_LoginRequest C2A_LoginRequest_request)
		{
			return (A2C_LoginResponse)await session.Call(C2A_LoginRequest_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<A2C_LoginResponse> C2A_LoginRequest(this Session session, string userName, string password, uint loginType)
		{
			using var C2A_LoginRequest_request = Fantasy.C2A_LoginRequest.Create();
			C2A_LoginRequest_request.UserName = userName;
			C2A_LoginRequest_request.Password = password;
			C2A_LoginRequest_request.LoginType = loginType;
			return (A2C_LoginResponse)await session.Call(C2A_LoginRequest_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void G2C_LoginMessage(this Session session, G2C_LoginMessage G2C_LoginMessage_message)
		{
			session.Send(G2C_LoginMessage_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void G2C_LoginMessage(this Session session, string msg)
		{
			using var G2C_LoginMessage_message = Fantasy.G2C_LoginMessage.Create();
			G2C_LoginMessage_message.Msg = msg;
			session.Send(G2C_LoginMessage_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<C2B_JoinBattleResponse> C2B_JoinBattle(this Session session, C2B_JoinBattle C2B_JoinBattle_request)
		{
			return (C2B_JoinBattleResponse)await session.Call(C2B_JoinBattle_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async FTask<C2B_JoinBattleResponse> C2B_JoinBattle(this Session session)
		{
			using var C2B_JoinBattle_request = Fantasy.C2B_JoinBattle.Create();
			return (C2B_JoinBattleResponse)await session.Call(C2B_JoinBattle_request);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_PlayerInput(this Session session, C2B_PlayerInput C2B_PlayerInput_message)
		{
			session.Send(C2B_PlayerInput_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_PlayerInput(this Session session, uint frameIndex, uint inputSeq, float dx, float dy)
		{
			using var C2B_PlayerInput_message = Fantasy.C2B_PlayerInput.Create();
			C2B_PlayerInput_message.FrameIndex = frameIndex;
			C2B_PlayerInput_message.InputSeq = inputSeq;
			C2B_PlayerInput_message.Dx = dx;
			C2B_PlayerInput_message.Dy = dy;
			session.Send(C2B_PlayerInput_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_FrameSnapshot(this Session session, S2C_FrameSnapshot S2C_FrameSnapshot_message)
		{
			session.Send(S2C_FrameSnapshot_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_FrameSnapshot(this Session session, uint frameIndex, List<PlayerSnapshot> players, List<FrameContactSnapshot> contacts)
		{
			using var S2C_FrameSnapshot_message = Fantasy.S2C_FrameSnapshot.Create();
			S2C_FrameSnapshot_message.FrameIndex = frameIndex;
			S2C_FrameSnapshot_message.Players = players;
			S2C_FrameSnapshot_message.Contacts = contacts;
			session.Send(S2C_FrameSnapshot_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_Ping(this Session session, C2B_Ping C2B_Ping_message)
		{
			session.Send(C2B_Ping_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_Ping(this Session session, ulong sendTimestampMs)
		{
			using var C2B_Ping_message = Fantasy.C2B_Ping.Create();
			C2B_Ping_message.SendTimestampMs = sendTimestampMs;
			session.Send(C2B_Ping_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_Pong(this Session session, S2C_Pong S2C_Pong_message)
		{
			session.Send(S2C_Pong_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_Pong(this Session session, ulong sendTimestampMs)
		{
			using var S2C_Pong_message = Fantasy.S2C_Pong.Create();
			S2C_Pong_message.SendTimestampMs = sendTimestampMs;
			session.Send(S2C_Pong_message);
		}

   }
}