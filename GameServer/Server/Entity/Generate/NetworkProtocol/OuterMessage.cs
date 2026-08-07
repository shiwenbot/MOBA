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
            XRaw = default;
            YRaw = default;
            ServerFrameIndex = default;
            MessageObjectPool<C2B_JoinBattleResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_JoinBattleResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
        [ProtoMember(2)]
        public long PlayerId { get; set; }
        [ProtoMember(3)]
        public long XRaw { get; set; }
        [ProtoMember(4)]
        public long YRaw { get; set; }
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
            DxRaw = default;
            DyRaw = default;
            SkillId = default;
            MessageObjectPool<C2B_PlayerInput>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_PlayerInput; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public uint InputSeq { get; set; }
        [ProtoMember(3)]
        public long DxRaw { get; set; }
        [ProtoMember(4)]
        public long DyRaw { get; set; }
        [ProtoMember(5)]
        public int SkillId { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class BuffSnapshot : AMessage, IDisposable
    {
        public static BuffSnapshot Create(bool autoReturn = true)
        {
            var buffSnapshot = MessageObjectPool<BuffSnapshot>.Rent();
            buffSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                buffSnapshot.SetIsPool(false);
            }
            
            return buffSnapshot;
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
            RuntimeBuffId = default;
            BuffId = default;
            CasterId = default;
            TargetId = default;
            StackCount = default;
            RemainingFrames = default;
            AppliedFrame = default;
            Flags = default;
            DirtyFlags = default;
            MessageObjectPool<BuffSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public long RuntimeBuffId { get; set; }
        [ProtoMember(2)]
        public int BuffId { get; set; }
        [ProtoMember(3)]
        public long CasterId { get; set; }
        [ProtoMember(4)]
        public long TargetId { get; set; }
        [ProtoMember(5)]
        public int StackCount { get; set; }
        [ProtoMember(6)]
        public int RemainingFrames { get; set; }
        [ProtoMember(7)]
        public uint AppliedFrame { get; set; }
        [ProtoMember(8)]
        public uint Flags { get; set; }
        [ProtoMember(9)]
        public uint DirtyFlags { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class NumericModifierSnapshot : AMessage, IDisposable
    {
        public static NumericModifierSnapshot Create(bool autoReturn = true)
        {
            var numericModifierSnapshot = MessageObjectPool<NumericModifierSnapshot>.Rent();
            numericModifierSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                numericModifierSnapshot.SetIsPool(false);
            }
            
            return numericModifierSnapshot;
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
            SourceBuffId = default;
            ValueType = default;
            AttributeKind = default;
            Value = default;
            MessageObjectPool<NumericModifierSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public long SourceBuffId { get; set; }
        [ProtoMember(2)]
        public uint ValueType { get; set; }
        [ProtoMember(3)]
        public uint AttributeKind { get; set; }
        [ProtoMember(4)]
        public int Value { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class NumericSnapshot : AMessage, IDisposable
    {
        public static NumericSnapshot Create(bool autoReturn = true)
        {
            var numericSnapshot = MessageObjectPool<NumericSnapshot>.Rent();
            numericSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                numericSnapshot.SetIsPool(false);
            }
            
            return numericSnapshot;
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
            BaseHealth = default;
            BaseMaxHealth = default;
            BaseMana = default;
            BaseMaxMana = default;
            BaseAttack = default;
            Modifiers.Clear();
            BaseStamina = default;
            BaseMaxStamina = default;
            MessageObjectPool<NumericSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public int BaseHealth { get; set; }
        [ProtoMember(2)]
        public int BaseMaxHealth { get; set; }
        [ProtoMember(3)]
        public int BaseMana { get; set; }
        [ProtoMember(4)]
        public int BaseMaxMana { get; set; }
        [ProtoMember(5)]
        public int BaseAttack { get; set; }
        [ProtoMember(6)]
        public List<NumericModifierSnapshot> Modifiers { get; set; } = new List<NumericModifierSnapshot>();
        [ProtoMember(7)]
        public int BaseStamina { get; set; }
        [ProtoMember(8)]
        public int BaseMaxStamina { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class SkillStringValueSnapshot : AMessage, IDisposable
    {
        public static SkillStringValueSnapshot Create(bool autoReturn = true)
        {
            var skillStringValueSnapshot = MessageObjectPool<SkillStringValueSnapshot>.Rent();
            skillStringValueSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                skillStringValueSnapshot.SetIsPool(false);
            }
            
            return skillStringValueSnapshot;
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
            Key = default;
            Value = default;
            MessageObjectPool<SkillStringValueSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public string Key { get; set; }
        [ProtoMember(2)]
        public string Value { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class SkillFloatValueSnapshot : AMessage, IDisposable
    {
        public static SkillFloatValueSnapshot Create(bool autoReturn = true)
        {
            var skillFloatValueSnapshot = MessageObjectPool<SkillFloatValueSnapshot>.Rent();
            skillFloatValueSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                skillFloatValueSnapshot.SetIsPool(false);
            }
            
            return skillFloatValueSnapshot;
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
            Key = default;
            Value = default;
            MessageObjectPool<SkillFloatValueSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public string Key { get; set; }
        [ProtoMember(2)]
        public float Value { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class SkillIntValueSnapshot : AMessage, IDisposable
    {
        public static SkillIntValueSnapshot Create(bool autoReturn = true)
        {
            var skillIntValueSnapshot = MessageObjectPool<SkillIntValueSnapshot>.Rent();
            skillIntValueSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                skillIntValueSnapshot.SetIsPool(false);
            }
            
            return skillIntValueSnapshot;
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
            Key = default;
            Value = default;
            MessageObjectPool<SkillIntValueSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public string Key { get; set; }
        [ProtoMember(2)]
        public int Value { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class SkillBoolValueSnapshot : AMessage, IDisposable
    {
        public static SkillBoolValueSnapshot Create(bool autoReturn = true)
        {
            var skillBoolValueSnapshot = MessageObjectPool<SkillBoolValueSnapshot>.Rent();
            skillBoolValueSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                skillBoolValueSnapshot.SetIsPool(false);
            }
            
            return skillBoolValueSnapshot;
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
            Key = default;
            Value = default;
            MessageObjectPool<SkillBoolValueSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public string Key { get; set; }
        [ProtoMember(2)]
        public bool Value { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class SkillDelaySnapshot : AMessage, IDisposable
    {
        public static SkillDelaySnapshot Create(bool autoReturn = true)
        {
            var skillDelaySnapshot = MessageObjectPool<SkillDelaySnapshot>.Rent();
            skillDelaySnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                skillDelaySnapshot.SetIsPool(false);
            }
            
            return skillDelaySnapshot;
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
            NodeId = default;
            RemainingFrames = default;
            MessageObjectPool<SkillDelaySnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public int NodeId { get; set; }
        [ProtoMember(2)]
        public int RemainingFrames { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class ActiveSkillExecutionSnapshot : AMessage, IDisposable
    {
        public static ActiveSkillExecutionSnapshot Create(bool autoReturn = true)
        {
            var activeSkillExecutionSnapshot = MessageObjectPool<ActiveSkillExecutionSnapshot>.Rent();
            activeSkillExecutionSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                activeSkillExecutionSnapshot.SetIsPool(false);
            }
            
            return activeSkillExecutionSnapshot;
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
            CasterId = default;
            TargetId = default;
            SkillId = default;
            CurrentNodeId = default;
            Status = default;
            ExecutedSteps = default;
            FrameIndex = default;
            Message = default;
            Strings.Clear();
            Floats.Clear();
            Ints.Clear();
            Bools.Clear();
            DelayRemainingFrames.Clear();
            DirectionXRaw = default;
            DirectionYRaw = default;
            MessageObjectPool<ActiveSkillExecutionSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public long CasterId { get; set; }
        [ProtoMember(2)]
        public long TargetId { get; set; }
        [ProtoMember(3)]
        public int SkillId { get; set; }
        [ProtoMember(4)]
        public int CurrentNodeId { get; set; }
        [ProtoMember(5)]
        public uint Status { get; set; }
        [ProtoMember(6)]
        public int ExecutedSteps { get; set; }
        [ProtoMember(7)]
        public int FrameIndex { get; set; }
        [ProtoMember(8)]
        public string Message { get; set; }
        [ProtoMember(9)]
        public List<SkillStringValueSnapshot> Strings { get; set; } = new List<SkillStringValueSnapshot>();
        [ProtoMember(10)]
        public List<SkillFloatValueSnapshot> Floats { get; set; } = new List<SkillFloatValueSnapshot>();
        [ProtoMember(11)]
        public List<SkillIntValueSnapshot> Ints { get; set; } = new List<SkillIntValueSnapshot>();
        [ProtoMember(12)]
        public List<SkillBoolValueSnapshot> Bools { get; set; } = new List<SkillBoolValueSnapshot>();
        [ProtoMember(13)]
        public List<SkillDelaySnapshot> DelayRemainingFrames { get; set; } = new List<SkillDelaySnapshot>();
        [ProtoMember(14)]
        public long DirectionXRaw { get; set; }
        [ProtoMember(15)]
        public long DirectionYRaw { get; set; }
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
            XRaw = default;
            YRaw = default;
            LatestAcceptedInputFrame = default;
            ReservedField5 = default;
            LinearVelocityXRaw = default;
            LinearVelocityYRaw = default;
            AngularVelocityRaw = default;
            IsAsleep = default;
            IsDisabled = default;
            AttributeDirtyMask = default;
            Health = default;
            MaxHealth = default;
            Mana = default;
            MaxMana = default;
            Attack = default;
            ActiveBuffs.Clear();
            NextRuntimeBuffId = default;
            if (Numeric != null)
            {
                Numeric.Dispose();
                Numeric = null;
            }
            BuffDirtyMask = default;
            BuffSnapshotFrameIndex = default;
            IsBuffFullSync = default;
            RotationRadiansRaw = default;
            AttributeBaselineFrameIndex = default;
            Stamina = default;
            MaxStamina = default;
            StaminaRegenCounterFrames = default;
            DashVelocityXRaw = default;
            DashVelocityYRaw = default;
            DashRemainingFrames = default;
            DashRuntimeBuffId = default;
            KnockbackVelocityXRaw = default;
            KnockbackVelocityYRaw = default;
            KnockbackRemainingFrames = default;
            KnockbackRuntimeBuffId = default;
            SkillExecutions.Clear();
            MessageObjectPool<PlayerSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public long PlayerId { get; set; }
        [ProtoMember(2)]
        public long XRaw { get; set; }
        [ProtoMember(3)]
        public long YRaw { get; set; }
        [ProtoMember(4)]
        public uint LatestAcceptedInputFrame { get; set; }
        [ProtoMember(5)]
        public uint ReservedField5 { get; set; }
        [ProtoMember(6)]
        public long LinearVelocityXRaw { get; set; }
        [ProtoMember(7)]
        public long LinearVelocityYRaw { get; set; }
        [ProtoMember(8)]
        public long AngularVelocityRaw { get; set; }
        [ProtoMember(9)]
        public bool IsAsleep { get; set; }
        [ProtoMember(10)]
        public bool IsDisabled { get; set; }
        [ProtoMember(11)]
        public uint AttributeDirtyMask { get; set; }
        [ProtoMember(12)]
        public int Health { get; set; }
        [ProtoMember(13)]
        public int MaxHealth { get; set; }
        [ProtoMember(14)]
        public int Mana { get; set; }
        [ProtoMember(15)]
        public int MaxMana { get; set; }
        [ProtoMember(16)]
        public int Attack { get; set; }
        [ProtoMember(17)]
        public List<BuffSnapshot> ActiveBuffs { get; set; } = new List<BuffSnapshot>();
        [ProtoMember(18)]
        public long NextRuntimeBuffId { get; set; }
        [ProtoMember(19)]
        public NumericSnapshot Numeric { get; set; }
        [ProtoMember(20)]
        public uint BuffDirtyMask { get; set; }
        [ProtoMember(21)]
        public uint BuffSnapshotFrameIndex { get; set; }
        [ProtoMember(22)]
        public bool IsBuffFullSync { get; set; }
        [ProtoMember(23)]
        public long RotationRadiansRaw { get; set; }
        [ProtoMember(24)]
        public uint AttributeBaselineFrameIndex { get; set; }
        [ProtoMember(25)]
        public int Stamina { get; set; }
        [ProtoMember(26)]
        public int MaxStamina { get; set; }
        [ProtoMember(27)]
        public int StaminaRegenCounterFrames { get; set; }
        [ProtoMember(28)]
        public long DashVelocityXRaw { get; set; }
        [ProtoMember(29)]
        public long DashVelocityYRaw { get; set; }
        [ProtoMember(30)]
        public int DashRemainingFrames { get; set; }
        [ProtoMember(31)]
        public long DashRuntimeBuffId { get; set; }
        [ProtoMember(32)]
        public long KnockbackVelocityXRaw { get; set; }
        [ProtoMember(33)]
        public long KnockbackVelocityYRaw { get; set; }
        [ProtoMember(34)]
        public int KnockbackRemainingFrames { get; set; }
        [ProtoMember(35)]
        public long KnockbackRuntimeBuffId { get; set; }
        [ProtoMember(36)]
        public List<ActiveSkillExecutionSnapshot> SkillExecutions { get; set; } = new List<ActiveSkillExecutionSnapshot>();
    }
    [Serializable]
    [ProtoContract]
    public partial class FrameContactSnapshot : AMessage, IDisposable
    {
        public static FrameContactSnapshot Create(bool autoReturn = true)
        {
            var frameContactSnapshot = MessageObjectPool<FrameContactSnapshot>.Rent();
            frameContactSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                frameContactSnapshot.SetIsPool(false);
            }
            
            return frameContactSnapshot;
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
            BodyAId = default;
            BodyBId = default;
            IsTouching = default;
            MessageObjectPool<FrameContactSnapshot>.Return(this);
        }
        [ProtoMember(1)]
        public int BodyAId { get; set; }
        [ProtoMember(2)]
        public int BodyBId { get; set; }
        [ProtoMember(3)]
        public bool IsTouching { get; set; }
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
            Contacts.Clear();
            TargetLeadFrames = default;
            MessageObjectPool<S2C_FrameSnapshot>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_FrameSnapshot; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public List<PlayerSnapshot> Players { get; set; } = new List<PlayerSnapshot>();
        [ProtoMember(3)]
        public List<FrameContactSnapshot> Contacts { get; set; } = new List<FrameContactSnapshot>();
        [ProtoMember(4)]
        public uint TargetLeadFrames { get; set; }
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
    [Serializable]
    [ProtoContract]
    public partial class S2C_BandwidthStats : AMessage, IMessage
    {
        public static S2C_BandwidthStats Create(bool autoReturn = true)
        {
            var s2C_BandwidthStats = MessageObjectPool<S2C_BandwidthStats>.Rent();
            s2C_BandwidthStats.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                s2C_BandwidthStats.SetIsPool(false);
            }
            
            return s2C_BandwidthStats;
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
            MeasureFullSyncBaseline = default;
            HasSamples = default;
            ActualPayloadBytes = default;
            FullSyncPayloadBytes = default;
            DirtySyncSavedRatio = default;
            DirtySyncSavedBytes = default;
            MessageObjectPool<S2C_BandwidthStats>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_BandwidthStats; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public bool MeasureFullSyncBaseline { get; set; }
        [ProtoMember(3)]
        public bool HasSamples { get; set; }
        [ProtoMember(4)]
        public long ActualPayloadBytes { get; set; }
        [ProtoMember(5)]
        public long FullSyncPayloadBytes { get; set; }
        [ProtoMember(6)]
        public double DirtySyncSavedRatio { get; set; }
        [ProtoMember(7)]
        public long DirtySyncSavedBytes { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_StateHashReport : AMessage, IMessage
    {
        public static C2B_StateHashReport Create(bool autoReturn = true)
        {
            var c2B_StateHashReport = MessageObjectPool<C2B_StateHashReport>.Rent();
            c2B_StateHashReport.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_StateHashReport.SetIsPool(false);
            }
            
            return c2B_StateHashReport;
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
            StateHash = default;
            MessageObjectPool<C2B_StateHashReport>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_StateHashReport; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public ulong StateHash { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class S2C_RttProbe : AMessage, IMessage
    {
        public static S2C_RttProbe Create(bool autoReturn = true)
        {
            var s2C_RttProbe = MessageObjectPool<S2C_RttProbe>.Rent();
            s2C_RttProbe.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                s2C_RttProbe.SetIsPool(false);
            }
            
            return s2C_RttProbe;
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
            ProbeNonce = default;
            MessageObjectPool<S2C_RttProbe>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_RttProbe; } 
        [ProtoMember(1)]
        public ulong ProbeNonce { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2B_RttProbeAck : AMessage, IMessage
    {
        public static C2B_RttProbeAck Create(bool autoReturn = true)
        {
            var c2B_RttProbeAck = MessageObjectPool<C2B_RttProbeAck>.Rent();
            c2B_RttProbeAck.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2B_RttProbeAck.SetIsPool(false);
            }
            
            return c2B_RttProbeAck;
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
            ProbeNonce = default;
            MessageObjectPool<C2B_RttProbeAck>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2B_RttProbeAck; } 
        [ProtoMember(1)]
        public ulong ProbeNonce { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class S2C_RttStats : AMessage, IMessage
    {
        public static S2C_RttStats Create(bool autoReturn = true)
        {
            var s2C_RttStats = MessageObjectPool<S2C_RttStats>.Rent();
            s2C_RttStats.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                s2C_RttStats.SetIsPool(false);
            }
            
            return s2C_RttStats;
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
            Enabled = default;
            HasSample = default;
            RttMinMs = default;
            RttEmaMs = default;
            ControlRttMs = default;
            RttSampleCount = default;
            LeadOutOfBoundsCount = default;
            MessageObjectPool<S2C_RttStats>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.S2C_RttStats; } 
        [ProtoMember(1)]
        public uint FrameIndex { get; set; }
        [ProtoMember(2)]
        public bool Enabled { get; set; }
        [ProtoMember(3)]
        public bool HasSample { get; set; }
        [ProtoMember(4)]
        public double RttMinMs { get; set; }
        [ProtoMember(5)]
        public double RttEmaMs { get; set; }
        [ProtoMember(6)]
        public double ControlRttMs { get; set; }
        [ProtoMember(7)]
        public int RttSampleCount { get; set; }
        [ProtoMember(8)]
        public int LeadOutOfBoundsCount { get; set; }
    }
}