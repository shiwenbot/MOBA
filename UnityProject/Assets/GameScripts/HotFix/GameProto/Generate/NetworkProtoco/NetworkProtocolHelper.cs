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
		public static void C2B_TestInput(this Session session, C2B_TestInput C2B_TestInput_message)
		{
			session.Send(C2B_TestInput_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_TestInput(this Session session, uint seq, long clientSendTimeMs)
		{
			using var C2B_TestInput_message = Fantasy.C2B_TestInput.Create();
			C2B_TestInput_message.Seq = seq;
			C2B_TestInput_message.ClientSendTimeMs = clientSendTimeMs;
			session.Send(C2B_TestInput_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void B2C_TestState(this Session session, B2C_TestState B2C_TestState_message)
		{
			session.Send(B2C_TestState_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void B2C_TestState(this Session session, uint seq, long clientSendTimeMs, long serverRecvTimeMs, long serverSendTimeMs)
		{
			using var B2C_TestState_message = Fantasy.B2C_TestState.Create();
			B2C_TestState_message.Seq = seq;
			B2C_TestState_message.ClientSendTimeMs = clientSendTimeMs;
			B2C_TestState_message.ServerRecvTimeMs = serverRecvTimeMs;
			B2C_TestState_message.ServerSendTimeMs = serverSendTimeMs;
			session.Send(B2C_TestState_message);
		}

   }
}