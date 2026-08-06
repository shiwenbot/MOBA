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
		public static void C2B_PlayerInput(this Session session, uint frameIndex, uint inputSeq, long dxRaw, long dyRaw, int skillId)
		{
			using var C2B_PlayerInput_message = Fantasy.C2B_PlayerInput.Create();
			C2B_PlayerInput_message.FrameIndex = frameIndex;
			C2B_PlayerInput_message.InputSeq = inputSeq;
			C2B_PlayerInput_message.DxRaw = dxRaw;
			C2B_PlayerInput_message.DyRaw = dyRaw;
			C2B_PlayerInput_message.SkillId = skillId;
			session.Send(C2B_PlayerInput_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_FrameSnapshot(this Session session, S2C_FrameSnapshot S2C_FrameSnapshot_message)
		{
			session.Send(S2C_FrameSnapshot_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_FrameSnapshot(this Session session, uint frameIndex, List<PlayerSnapshot> players, List<FrameContactSnapshot> contacts, uint targetLeadFrames)
		{
			using var S2C_FrameSnapshot_message = Fantasy.S2C_FrameSnapshot.Create();
			S2C_FrameSnapshot_message.FrameIndex = frameIndex;
			S2C_FrameSnapshot_message.Players = players;
			S2C_FrameSnapshot_message.Contacts = contacts;
			S2C_FrameSnapshot_message.TargetLeadFrames = targetLeadFrames;
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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_BandwidthStats(this Session session, S2C_BandwidthStats S2C_BandwidthStats_message)
		{
			session.Send(S2C_BandwidthStats_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_BandwidthStats(this Session session, uint frameIndex, bool measureFullSyncBaseline, bool hasSamples, long actualPayloadBytes, long fullSyncPayloadBytes, double dirtySyncSavedRatio, long dirtySyncSavedBytes)
		{
			using var S2C_BandwidthStats_message = Fantasy.S2C_BandwidthStats.Create();
			S2C_BandwidthStats_message.FrameIndex = frameIndex;
			S2C_BandwidthStats_message.MeasureFullSyncBaseline = measureFullSyncBaseline;
			S2C_BandwidthStats_message.HasSamples = hasSamples;
			S2C_BandwidthStats_message.ActualPayloadBytes = actualPayloadBytes;
			S2C_BandwidthStats_message.FullSyncPayloadBytes = fullSyncPayloadBytes;
			S2C_BandwidthStats_message.DirtySyncSavedRatio = dirtySyncSavedRatio;
			S2C_BandwidthStats_message.DirtySyncSavedBytes = dirtySyncSavedBytes;
			session.Send(S2C_BandwidthStats_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_StateHashReport(this Session session, C2B_StateHashReport C2B_StateHashReport_message)
		{
			session.Send(C2B_StateHashReport_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_StateHashReport(this Session session, uint frameIndex, ulong stateHash)
		{
			using var C2B_StateHashReport_message = Fantasy.C2B_StateHashReport.Create();
			C2B_StateHashReport_message.FrameIndex = frameIndex;
			C2B_StateHashReport_message.StateHash = stateHash;
			session.Send(C2B_StateHashReport_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_RttProbe(this Session session, S2C_RttProbe S2C_RttProbe_message)
		{
			session.Send(S2C_RttProbe_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_RttProbe(this Session session, ulong probeNonce)
		{
			using var S2C_RttProbe_message = Fantasy.S2C_RttProbe.Create();
			S2C_RttProbe_message.ProbeNonce = probeNonce;
			session.Send(S2C_RttProbe_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_RttProbeAck(this Session session, C2B_RttProbeAck C2B_RttProbeAck_message)
		{
			session.Send(C2B_RttProbeAck_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void C2B_RttProbeAck(this Session session, ulong probeNonce)
		{
			using var C2B_RttProbeAck_message = Fantasy.C2B_RttProbeAck.Create();
			C2B_RttProbeAck_message.ProbeNonce = probeNonce;
			session.Send(C2B_RttProbeAck_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_RttStats(this Session session, S2C_RttStats S2C_RttStats_message)
		{
			session.Send(S2C_RttStats_message);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void S2C_RttStats(this Session session, uint frameIndex, bool enabled, bool hasSample, double rttMinMs, double rttEmaMs, double controlRttMs, int rttSampleCount, int leadOutOfBoundsCount)
		{
			using var S2C_RttStats_message = Fantasy.S2C_RttStats.Create();
			S2C_RttStats_message.FrameIndex = frameIndex;
			S2C_RttStats_message.Enabled = enabled;
			S2C_RttStats_message.HasSample = hasSample;
			S2C_RttStats_message.RttMinMs = rttMinMs;
			S2C_RttStats_message.RttEmaMs = rttEmaMs;
			S2C_RttStats_message.ControlRttMs = controlRttMs;
			S2C_RttStats_message.RttSampleCount = rttSampleCount;
			S2C_RttStats_message.LeadOutOfBoundsCount = leadOutOfBoundsCount;
			session.Send(S2C_RttStats_message);
		}

   }
}