using LightProto;
using System;
using MemoryPack;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Pool;
using Fantasy.Network.Interface;
using Fantasy.Serialize;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618
// ReSharper disable InconsistentNaming
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable RedundantTypeArgumentsOfMethod
// ReSharper disable PartialTypeWithSinglePart
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable PreferConcreteValueOverDefault
// ReSharper disable RedundantNameQualifier
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable CheckNamespace
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable RedundantUsingDirective
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
namespace Fantasy
{
    [Serializable]
    [ProtoContract]
    public partial class C2A_RegisterRequest : AMessage, IRequest
    {
        public static C2A_RegisterRequest Create(bool autoReturn = true)
        {
            var c2A_RegisterRequest = MessageObjectPool<C2A_RegisterRequest>.Rent();
            c2A_RegisterRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2A_RegisterRequest.SetIsPool(false);
            }
            
            return c2A_RegisterRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            UserName = default;
            Password = default;
            MessageObjectPool<C2A_RegisterRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2A_RegisterRequest; } 
        [ProtoIgnore]
        public A2C_RegisterResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string UserName { get; set; }
        [ProtoMember(2)]
        public string Password { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class A2C_RegisterResponse : AMessage, IResponse
    {
        public static A2C_RegisterResponse Create(bool autoReturn = true)
        {
            var a2C_RegisterResponse = MessageObjectPool<A2C_RegisterResponse>.Rent();
            a2C_RegisterResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                a2C_RegisterResponse.SetIsPool(false);
            }
            
            return a2C_RegisterResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<A2C_RegisterResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.A2C_RegisterResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2A_LoginRequest : AMessage, IRequest
    {
        public static C2A_LoginRequest Create(bool autoReturn = true)
        {
            var c2A_LoginRequest = MessageObjectPool<C2A_LoginRequest>.Rent();
            c2A_LoginRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2A_LoginRequest.SetIsPool(false);
            }
            
            return c2A_LoginRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            UserName = default;
            Password = default;
            LoginType = default;
            MessageObjectPool<C2A_LoginRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2A_LoginRequest; } 
        [ProtoIgnore]
        public A2C_LoginResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string UserName { get; set; }
        [ProtoMember(2)]
        public string Password { get; set; }
        [ProtoMember(3)]
        public uint LoginType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class A2C_LoginResponse : AMessage, IResponse
    {
        public static A2C_LoginResponse Create(bool autoReturn = true)
        {
            var a2C_LoginResponse = MessageObjectPool<A2C_LoginResponse>.Rent();
            a2C_LoginResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                a2C_LoginResponse.SetIsPool(false);
            }
            
            return a2C_LoginResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            Token = default;
            MessageObjectPool<A2C_LoginResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.A2C_LoginResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
        [ProtoMember(2)]
        public string Token { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_LoginMessage : AMessage, IMessage
    {
        public static G2C_LoginMessage Create(bool autoReturn = true)
        {
            var g2C_LoginMessage = MessageObjectPool<G2C_LoginMessage>.Rent();
            g2C_LoginMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_LoginMessage.SetIsPool(false);
            }
            
            return g2C_LoginMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Msg = default;
            MessageObjectPool<G2C_LoginMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_LoginMessage; } 
        [ProtoMember(1)]
        public string Msg { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_JoinBattle : AMessage, IRequest
    {
        public static C2B_JoinBattle Create(bool autoReturn = true)
        {
            var c2B_JoinBattle = MessageObjectPool<C2B_JoinBattle>.Rent();
            c2B_JoinBattle.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_JoinBattle.SetIsPool(false);
            }
            
            return c2B_JoinBattle;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2B_JoinBattle>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_JoinBattle; } 
        [ProtoIgnore]
        public C2B_JoinBattleResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_JoinBattleResponse : AMessage, IResponse
    {
        public static C2B_JoinBattleResponse Create(bool autoReturn = true)
        {
            var c2B_JoinBattleResponse = MessageObjectPool<C2B_JoinBattleResponse>.Rent();
            c2B_JoinBattleResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_JoinBattleResponse.SetIsPool(false);
            }
            
            return c2B_JoinBattleResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            PlayerId = default;
            X = default;
            Y = default;
            ServerFrameIndex = default;
            MessageObjectPool<C2B_JoinBattleResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_JoinBattleResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
        [ProtoMember(2)]
        public long PlayerId { get; set; }
        [ProtoMember(3)]
        public float X { get; set; }
        [ProtoMember(4)]
        public float Y { get; set; }
        [ProtoMember(5)]
        public uint ServerFrameIndex { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_PlayerInput : AMessage, IMessage
    {
        public static C2B_PlayerInput Create(bool autoReturn = true)
        {
            var c2B_PlayerInput = MessageObjectPool<C2B_PlayerInput>.Rent();
            c2B_PlayerInput.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_PlayerInput.SetIsPool(false);
            }
            
            return c2B_PlayerInput;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            FrameIndex = default;
            InputSeq = default;
            Dx = default;
            Dy = default;
            MessageObjectPool<C2B_PlayerInput>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_PlayerInput; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public uint InputSeq { get; set; }
        [ProtoMember(3)]
        public float Dx { get; set; }
        [ProtoMember(4)]
        public float Dy { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class PlayerSnapshot : AMessage, IDisposable
    {
        public static PlayerSnapshot Create(bool autoReturn = true)
        {
            var playerSnapshot = MessageObjectPool<PlayerSnapshot>.Rent();
            playerSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                playerSnapshot.SetIsPool(false);
            }
            
            return playerSnapshot;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            PlayerId = default;
            X = default;
            Y = default;
            LatestAcceptedInputFrame = default;
            MessageObjectPool<PlayerSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public long PlayerId { get; set; }
        [ProtoMember(2)]
        public float X { get; set; }
        [ProtoMember(3)]
        public float Y { get; set; }
        [ProtoMember(4)]
        public uint LatestAcceptedInputFrame { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class S2C_FrameSnapshot : AMessage, IMessage
    {
        public static S2C_FrameSnapshot Create(bool autoReturn = true)
        {
            var s2C_FrameSnapshot = MessageObjectPool<S2C_FrameSnapshot>.Rent();
            s2C_FrameSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                s2C_FrameSnapshot.SetIsPool(false);
            }
            
            return s2C_FrameSnapshot;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            FrameIndex = default;
            Players.Clear();
            MessageObjectPool<S2C_FrameSnapshot>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_FrameSnapshot; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public List<PlayerSnapshot> Players { get; set; } = new List<PlayerSnapshot>();
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_Ping : AMessage, IMessage
    {
        public static C2B_Ping Create(bool autoReturn = true)
        {
            var c2B_Ping = MessageObjectPool<C2B_Ping>.Rent();
            c2B_Ping.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_Ping.SetIsPool(false);
            }
            
            return c2B_Ping;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            SendTimestampMs = default;
            MessageObjectPool<C2B_Ping>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_Ping; } 
        [ProtoMember(1)]
        public ulong SendTimestampMs { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class S2C_Pong : AMessage, IMessage
    {
        public static S2C_Pong Create(bool autoReturn = true)
        {
            var s2C_Pong = MessageObjectPool<S2C_Pong>.Rent();
            s2C_Pong.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                s2C_Pong.SetIsPool(false);
            }
            
            return s2C_Pong;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            SendTimestampMs = default;
            MessageObjectPool<S2C_Pong>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_Pong; } 
        [ProtoMember(1)]
        public ulong SendTimestampMs { get; set; }
    }
}
