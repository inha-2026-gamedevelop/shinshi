// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 월백 체력 기믹 사건과 복원 값의 Mirror 직렬화, Weaver가 Rpc 인자에 이 메서드를 쓴다 </summary>
    public static class BossWolbaekNetSerializer
    {
        // 규칙 구조체는 읽기 전용 속성뿐이라 Weaver가 자동으로 쓰지 못한다(공개 필드가 없어 아무것도 쓰지 않는다). 여기서 순서를 고정한다.
        // 순서를 바꾸면 서버와 클라이언트가 다른 값을 읽으므로 PROTOCOL_VERSION을 함께 올린다.
        // 목록은 바이트 길이 하나 뒤에 값을 싣는다. 상한을 넘는 목록은 서버가 쓰지 않으므로 쓰기에서 막고, 읽는 쪽은 프로토콜 불일치로 보고 멈춘다.
        // 범위를 벗어난 열거형 값도 같다. Mirror가 해당 메시지를 버리고 오류를 남긴다.
        // 비어 있는 목록(null)은 길이 0으로 쓰고 읽을 때는 빈 배열이 된다

        /****************************************
        *              Constants
        ****************************************/

        private const byte MAX_TALISMAN_STATE  = (byte)EWolbaekTalismanState.Stopped;
        private const byte MAX_TALISMAN_COLOR  = (byte)EWolbaekTalismanColor.Green;
        private const byte MAX_ATTACK_KIND     = (byte)EWolbaekTalismanAttackKind.Line;
        private const byte MAX_HOLD_RESET      = (byte)EWolbaekHoldReset.Disconnected;
        private const byte MAX_WARD_STATE      = (byte)EWolbaekWardState.Stopped;
        private const byte MAX_WARD_COLOR      = (byte)EWolbaekWardColor.Blue;
        private const byte MAX_SEAL_STATE      = (byte)EWolbaekSealState.Stopped;
        private const byte MAX_SEAL_STEP       = (byte)EWolbaekSealStep.Cleanup;
        private const byte MAX_FINAL_STEP      = (byte)EWolbaekFinalStep.Hunt;

        /****************************************
        *            75% Talisman
        ****************************************/

        public static void WriteWolbaekTalismanStructure(this NetworkWriter writer, WolbaekTalismanStructure value)
        {
            writer.WriteInt(value.DirectionIndex);
            writer.WriteByte((byte)value.Color);
            writer.WriteInt(value.SymbolId);
            writer.WriteVector3(value.Center);
            writer.WriteFloat(value.Radius);
            writer.WriteInt(value.AssignedCombatId);
        }

        public static WolbaekTalismanStructure ReadWolbaekTalismanStructure(this NetworkReader reader)
        {
            int                   direction = reader.ReadInt();
            EWolbaekTalismanColor color     = (EWolbaekTalismanColor)ReadEnumByte(reader, MAX_TALISMAN_COLOR);
            int                   symbolId  = reader.ReadInt();
            Vector3               center    = reader.ReadVector3();
            float                 radius    = reader.ReadFloat();
            int                   assigned  = reader.ReadInt();
            return new WolbaekTalismanStructure(direction, color, symbolId, center, radius, assigned);
        }

        public static void WriteWolbaekTalismanStart(this NetworkWriter writer, WolbaekTalismanStart value)
        {
            int count = WriteCount(writer, value.Structures, Constants.Network.BOSS_WOLBAEK_MAX_STRUCTURES, "부적진 구조물");
            for (int i = 0; i < count; ++i)
            {
                writer.WriteWolbaekTalismanStructure(value.Structures[i]);
            }

            writer.WriteInt(value.BossCombatId);
            writer.WriteInt(value.CastInstanceId);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.TransitionEndServerTime);
            writer.WriteFloat(value.HoldSeconds);
            writer.WriteVector3(value.Center);
        }

        public static WolbaekTalismanStart ReadWolbaekTalismanStart(this NetworkReader reader)
        {
            int count      = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_STRUCTURES);
            var structures = new WolbaekTalismanStructure[count];
            for (int i = 0; i < count; ++i)
            {
                structures[i] = reader.ReadWolbaekTalismanStructure();
            }

            int     bossId        = reader.ReadInt();
            int     castId        = reader.ReadInt();
            double  startTime     = reader.ReadDouble();
            double  transitionEnd = reader.ReadDouble();
            float   holdSeconds   = reader.ReadFloat();
            Vector3 center        = reader.ReadVector3();
            return new WolbaekTalismanStart(bossId, castId, startTime, transitionEnd, holdSeconds, center, structures);
        }

        public static void WriteWolbaekTalismanAttack(this NetworkWriter writer, WolbaekTalismanAttack value)
        {
            writer.WriteByte((byte)value.Kind);
            writer.WriteInt(value.AttackId);
            writer.WriteDouble(value.TelegraphServerTime);
            writer.WriteDouble(value.StrikeServerTime);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.InnerRadius);
            writer.WriteFloat(value.OuterRadius);
            writer.WriteFloat(value.Length);
            writer.WriteFloat(value.Width);
            writer.WriteInt(value.TargetCombatId);
        }

        // 생성자가 막혀 있어 종류별 생성 메서드로 만든다. 종류에 쓰지 않는 값은 생성 메서드가 0으로 둔다
        public static WolbaekTalismanAttack ReadWolbaekTalismanAttack(this NetworkReader reader)
        {
            var     kind      = (EWolbaekTalismanAttackKind)ReadEnumByte(reader, MAX_ATTACK_KIND);
            int     attackId  = reader.ReadInt();
            double  telegraph = reader.ReadDouble();
            double  strike    = reader.ReadDouble();
            Vector3 origin    = reader.ReadVector3();
            Vector3 direction = reader.ReadVector3();
            float   inner     = reader.ReadFloat();
            float   outer     = reader.ReadFloat();
            float   length    = reader.ReadFloat();
            float   width     = reader.ReadFloat();
            int     target    = reader.ReadInt();

            if (kind == EWolbaekTalismanAttackKind.Line)
            {
                return WolbaekTalismanAttack.Line(attackId, telegraph, strike, origin, direction, length, width, target);
            }

            return WolbaekTalismanAttack.Ring(attackId, telegraph, strike, origin, inner, outer);
        }

        public static void WriteWolbaekTalismanSnapshot(this NetworkWriter writer, WolbaekTalismanSnapshot value)
        {
            writer.WriteByte((byte)value.State);
            writer.WriteWolbaekTalismanStart(value.Start);
            writer.WriteBool(value.HasHold);
            writer.WriteDouble(value.HoldStartServerTime);
            writer.WriteBool(value.HasAttack);
            writer.WriteWolbaekTalismanAttack(value.Attack);
            writer.WriteDouble(value.CounterEndServerTime);
        }

        public static WolbaekTalismanSnapshot ReadWolbaekTalismanSnapshot(this NetworkReader reader)
        {
            var                   state      = (EWolbaekTalismanState)ReadEnumByte(reader, MAX_TALISMAN_STATE);
            WolbaekTalismanStart  start      = reader.ReadWolbaekTalismanStart();
            bool                  hasHold    = reader.ReadBool();
            double                holdStart  = reader.ReadDouble();
            bool                  hasAttack  = reader.ReadBool();
            WolbaekTalismanAttack attack     = reader.ReadWolbaekTalismanAttack();
            double                counterEnd = reader.ReadDouble();
            return new WolbaekTalismanSnapshot(state, in start, hasHold, holdStart, hasAttack, in attack, counterEnd);
        }

        public static void WriteBossWolbaekTalismanRestore(this NetworkWriter writer, BossWolbaekTalismanRestore value)
        {
            writer.WriteWolbaekTalismanSnapshot(value.Snapshot);
            writer.WriteDouble(value.SuccessServerTime);
            writer.WriteBool(value.HasLastReset);
            writer.WriteDouble(value.LastResetServerTime);
            writer.WriteByte((byte)value.LastResetReason);
            writer.WriteInt(value.LastResetCombatId);
        }

        public static BossWolbaekTalismanRestore ReadBossWolbaekTalismanRestore(this NetworkReader reader)
        {
            return new BossWolbaekTalismanRestore
            {
                Snapshot            = reader.ReadWolbaekTalismanSnapshot(),
                SuccessServerTime   = reader.ReadDouble(),
                HasLastReset        = reader.ReadBool(),
                LastResetServerTime = reader.ReadDouble(),
                LastResetReason     = (EWolbaekHoldReset)ReadEnumByte(reader, MAX_HOLD_RESET),
                LastResetCombatId   = reader.ReadInt()
            };
        }

        /****************************************
        *            50% Color Ward
        ****************************************/

        public static void WriteWolbaekWardMember(this NetworkWriter writer, WolbaekWardMember value)
        {
            writer.WriteInt(value.CombatId);
            writer.WriteByte((byte)value.Color);
            writer.WriteInt(value.SymbolId);
        }

        public static WolbaekWardMember ReadWolbaekWardMember(this NetworkReader reader)
        {
            int               combatId = reader.ReadInt();
            EWolbaekWardColor color    = (EWolbaekWardColor)ReadEnumByte(reader, MAX_WARD_COLOR);
            int               symbolId = reader.ReadInt();
            return new WolbaekWardMember(combatId, color, symbolId);
        }

        public static void WriteWolbaekWardStart(this NetworkWriter writer, WolbaekWardStart value)
        {
            int count = WriteCount(writer, value.Members, Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS, "색 결계 명단");
            for (int i = 0; i < count; ++i)
            {
                writer.WriteWolbaekWardMember(value.Members[i]);
            }

            writer.WriteInt(value.BossCombatId);
            writer.WriteInt(value.CastInstanceId);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteVector3(value.GatherCenter);
            writer.WriteFloat(value.GatherRadius);
            writer.WriteFloat(value.GatherHoldSeconds);
            writer.WriteFloat(value.WardRadius);
            writer.WriteFloat(value.LinkWarnDistance);
            writer.WriteFloat(value.LinkDamageDistance);
        }

        public static WolbaekWardStart ReadWolbaekWardStart(this NetworkReader reader)
        {
            int count   = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS);
            var members = new WolbaekWardMember[count];
            for (int i = 0; i < count; ++i)
            {
                members[i] = reader.ReadWolbaekWardMember();
            }

            int     bossId       = reader.ReadInt();
            int     castId       = reader.ReadInt();
            double  startTime    = reader.ReadDouble();
            Vector3 gatherCenter = reader.ReadVector3();
            float   gatherRadius = reader.ReadFloat();
            float   gatherHold   = reader.ReadFloat();
            float   wardRadius   = reader.ReadFloat();
            float   linkWarn     = reader.ReadFloat();
            float   linkDamage   = reader.ReadFloat();
            return new WolbaekWardStart(bossId, castId, startTime, gatherCenter, gatherRadius, gatherHold, wardRadius, linkWarn,
                linkDamage, members);
        }

        public static void WriteWolbaekWardStage(this NetworkWriter writer, WolbaekWardStage value)
        {
            writer.WriteInt(value.Index);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.FirstStrikeServerTime);
            writer.WriteFloat(value.StrikeIntervalSeconds);
            writer.WriteInt(value.StrikeCount);
            writer.WriteVector3(value.RedCenter);
            writer.WriteVector3(value.BlueCenter);
            writer.WriteFloat(value.Radius);
        }

        public static WolbaekWardStage ReadWolbaekWardStage(this NetworkReader reader)
        {
            int     index       = reader.ReadInt();
            double  startTime   = reader.ReadDouble();
            double  firstStrike = reader.ReadDouble();
            float   interval    = reader.ReadFloat();
            int     strikeCount = reader.ReadInt();
            Vector3 redCenter   = reader.ReadVector3();
            Vector3 blueCenter  = reader.ReadVector3();
            float   radius      = reader.ReadFloat();
            return new WolbaekWardStage(index, startTime, firstStrike, interval, strikeCount, redCenter, blueCenter, radius);
        }

        public static void WriteWolbaekWardStrike(this NetworkWriter writer, WolbaekWardStrike value)
        {
            writer.WriteInt(value.StageIndex);
            writer.WriteInt(value.StrikeIndex);
            writer.WriteDouble(value.TelegraphServerTime);
            writer.WriteDouble(value.StrikeServerTime);
            writer.WriteVector3(value.Origin);
            writer.WriteFloat(value.PushDistance);
        }

        public static WolbaekWardStrike ReadWolbaekWardStrike(this NetworkReader reader)
        {
            int     stageIndex  = reader.ReadInt();
            int     strikeIndex = reader.ReadInt();
            double  telegraph   = reader.ReadDouble();
            double  strike      = reader.ReadDouble();
            Vector3 origin      = reader.ReadVector3();
            float   push        = reader.ReadFloat();
            return new WolbaekWardStrike(stageIndex, strikeIndex, telegraph, strike, origin, push);
        }

        public static void WriteWolbaekWardSnapshot(this NetworkWriter writer, WolbaekWardSnapshot value)
        {
            writer.WriteByte((byte)value.State);
            writer.WriteWolbaekWardStart(value.Start);
            writer.WriteBool(value.HasHold);
            writer.WriteDouble(value.HoldStartServerTime);
            writer.WriteWolbaekWardStage(value.Stage);
            writer.WriteInt(value.NextStrikeIndex);

            int count = WriteCount(writer, value.GraceEndServerTimes, Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS, "색 결계 합류 유예");
            for (int i = 0; i < count; ++i)
            {
                writer.WriteDouble(value.GraceEndServerTimes[i]);
            }
        }

        public static WolbaekWardSnapshot ReadWolbaekWardSnapshot(this NetworkReader reader)
        {
            var              state      = (EWolbaekWardState)ReadEnumByte(reader, MAX_WARD_STATE);
            WolbaekWardStart start      = reader.ReadWolbaekWardStart();
            bool             hasHold    = reader.ReadBool();
            double           holdStart  = reader.ReadDouble();
            WolbaekWardStage stage      = reader.ReadWolbaekWardStage();
            int              nextStrike = reader.ReadInt();
            int              count      = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS);

            var graceEnds = new double[count];
            for (int i = 0; i < count; ++i)
            {
                graceEnds[i] = reader.ReadDouble();
            }

            return new WolbaekWardSnapshot(state, in start, hasHold, holdStart, in stage, nextStrike, graceEnds);
        }

        public static void WriteBossWolbaekWardRestore(this NetworkWriter writer, BossWolbaekWardRestore value)
        {
            writer.WriteWolbaekWardSnapshot(value.Snapshot);
            writer.WriteBool(value.HasPendingStrike);
            writer.WriteWolbaekWardStrike(value.PendingStrike);
        }

        public static BossWolbaekWardRestore ReadBossWolbaekWardRestore(this NetworkReader reader)
        {
            return new BossWolbaekWardRestore
            {
                Snapshot         = reader.ReadWolbaekWardSnapshot(),
                HasPendingStrike = reader.ReadBool(),
                PendingStrike    = reader.ReadWolbaekWardStrike()
            };
        }

        /****************************************
        *            25% Foxfire Seal
        ****************************************/

        public static void WriteWolbaekSealStart(this NetworkWriter writer, WolbaekSealStart value)
        {
            writer.WriteInt(value.BossCombatId);
            writer.WriteInt(value.CastInstanceId);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.LimitEndServerTime);
            writer.WriteInt(value.RequiredSuccesses);
            writer.WriteFloat(value.PrepareSeconds);
            writer.WriteFloat(value.TrackSeconds);
            writer.WriteFloat(value.EscapeSeconds);
            writer.WriteFloat(value.CleanupSeconds);
            writer.WriteFloat(value.BlastRadius);
            writer.WriteFloat(value.CoreHitRadius);
            writer.WriteVector3(value.ArenaCenter);
        }

        public static WolbaekSealStart ReadWolbaekSealStart(this NetworkReader reader)
        {
            int     bossId     = reader.ReadInt();
            int     castId     = reader.ReadInt();
            double  startTime  = reader.ReadDouble();
            double  limitEnd   = reader.ReadDouble();
            int     required   = reader.ReadInt();
            float   prepare    = reader.ReadFloat();
            float   track      = reader.ReadFloat();
            float   escape     = reader.ReadFloat();
            float   cleanup    = reader.ReadFloat();
            float   blast      = reader.ReadFloat();
            float   coreHit    = reader.ReadFloat();
            Vector3 center     = reader.ReadVector3();
            return new WolbaekSealStart(bossId, castId, startTime, limitEnd, required, prepare, track, escape, cleanup, blast,
                coreHit, center);
        }

        public static void WriteWolbaekSealTurn(this NetworkWriter writer, WolbaekSealTurn value)
        {
            writer.WriteInt(value.Index);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.TrackStartServerTime);
            writer.WriteDouble(value.LockServerTime);
            writer.WriteDouble(value.ResolveServerTime);
            writer.WriteDouble(value.EndServerTime);

            int coreCount = WriteCount(writer, value.Cores, Constants.Network.BOSS_WOLBAEK_MAX_CORES, "봉인 핵");
            for (int i = 0; i < coreCount; ++i)
            {
                WolbaekSealCore core = value.Cores[i];
                writer.WriteInt(core.Index);
                writer.WriteVector3(core.Center);
            }

            int foxfireCount = WriteCount(writer, value.Foxfires, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES, "봉인 여우불");
            for (int i = 0; i < foxfireCount; ++i)
            {
                WolbaekSealFoxfire foxfire = value.Foxfires[i];
                writer.WriteInt(foxfire.Index);
                writer.WriteInt(foxfire.TargetCombatId);
            }
        }

        public static WolbaekSealTurn ReadWolbaekSealTurn(this NetworkReader reader)
        {
            int    index      = reader.ReadInt();
            double startTime  = reader.ReadDouble();
            double trackStart = reader.ReadDouble();
            double lockTime   = reader.ReadDouble();
            double resolve    = reader.ReadDouble();
            double endTime    = reader.ReadDouble();

            int coreCount = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_CORES);
            var cores     = new WolbaekSealCore[coreCount];
            for (int i = 0; i < coreCount; ++i)
            {
                int     coreIndex = reader.ReadInt();
                Vector3 center    = reader.ReadVector3();
                cores[i] = new WolbaekSealCore(coreIndex, center);
            }

            int foxfireCount = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES);
            var foxfires     = new WolbaekSealFoxfire[foxfireCount];
            for (int i = 0; i < foxfireCount; ++i)
            {
                int foxfireIndex = reader.ReadInt();
                int target       = reader.ReadInt();
                foxfires[i] = new WolbaekSealFoxfire(foxfireIndex, target);
            }

            return new WolbaekSealTurn(index, startTime, trackStart, lockTime, resolve, endTime, cores, foxfires);
        }

        public static void WriteWolbaekSealSweep(this NetworkWriter writer, WolbaekSealSweep value)
        {
            writer.WriteInt(value.TurnIndex);
            writer.WriteInt(value.SweepIndex);
            writer.WriteDouble(value.TelegraphServerTime);
            writer.WriteDouble(value.StrikeServerTime);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Range);
            writer.WriteFloat(value.HalfAngleDegrees);
            writer.WriteFloat(value.PushDistance);
        }

        public static WolbaekSealSweep ReadWolbaekSealSweep(this NetworkReader reader)
        {
            int     turnIndex  = reader.ReadInt();
            int     sweepIndex = reader.ReadInt();
            double  telegraph  = reader.ReadDouble();
            double  strike     = reader.ReadDouble();
            Vector3 origin     = reader.ReadVector3();
            Vector3 direction  = reader.ReadVector3();
            float   range      = reader.ReadFloat();
            float   halfAngle  = reader.ReadFloat();
            float   push       = reader.ReadFloat();
            return new WolbaekSealSweep(turnIndex, sweepIndex, telegraph, strike, origin, direction, range, halfAngle, push);
        }

        public static void WriteWolbaekSealSnapshot(this NetworkWriter writer, WolbaekSealSnapshot value)
        {
            writer.WriteByte((byte)value.State);
            writer.WriteByte((byte)value.Step);
            writer.WriteWolbaekSealStart(value.Start);
            writer.WriteWolbaekSealTurn(value.Turn);
            writer.WriteInt(value.SuccessCount);
            WritePositions(writer, value.FoxfirePositions, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES, "봉인 여우불 위치");
            WriteFlags(writer, value.FoxfireDetached, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES, "봉인 여우불 분리");
            writer.WriteBool(value.FoxfiresLocked);
            writer.WriteBool(value.HasPendingSweep);
            writer.WriteWolbaekSealSweep(value.PendingSweep);
            writer.WriteDouble(value.CounterEndServerTime);
        }

        public static WolbaekSealSnapshot ReadWolbaekSealSnapshot(this NetworkReader reader)
        {
            var              state        = (EWolbaekSealState)ReadEnumByte(reader, MAX_SEAL_STATE);
            var              step         = (EWolbaekSealStep)ReadEnumByte(reader, MAX_SEAL_STEP);
            WolbaekSealStart start        = reader.ReadWolbaekSealStart();
            WolbaekSealTurn  turn         = reader.ReadWolbaekSealTurn();
            int              successCount = reader.ReadInt();
            Vector3[]        positions    = ReadPositions(reader, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES);
            bool[]           detached     = ReadFlags(reader, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES);
            bool             locked       = reader.ReadBool();
            bool             hasSweep     = reader.ReadBool();
            WolbaekSealSweep sweep        = reader.ReadWolbaekSealSweep();
            double           counterEnd   = reader.ReadDouble();
            return new WolbaekSealSnapshot(state, step, in start, in turn, successCount, positions, detached, locked, hasSweep,
                in sweep, counterEnd);
        }

        public static void WriteBossWolbaekSealRestore(this NetworkWriter writer, BossWolbaekSealRestore value)
        {
            writer.WriteWolbaekSealSnapshot(value.Snapshot);
            writer.WriteByte(value.DestroyedCoreMask);
        }

        public static BossWolbaekSealRestore ReadBossWolbaekSealRestore(this NetworkReader reader)
        {
            return new BossWolbaekSealRestore
            {
                Snapshot          = reader.ReadWolbaekSealSnapshot(),
                DestroyedCoreMask = reader.ReadByte()
            };
        }

        /****************************************
        *          Hard 0% Final Area
        ****************************************/

        public static void WriteWolbaekFinalStart(this NetworkWriter writer, WolbaekFinalStart value)
        {
            writer.WriteInt(value.BossCombatId);
            writer.WriteInt(value.CastInstanceId);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.CollapseServerTime);
            writer.WriteFloat(value.CentralRadius);
            writer.WriteInt(value.YellowCount);
            writer.WriteFloat(value.YellowMaxHealth);
            writer.WriteFloat(value.RedTrackSeconds);
            writer.WriteFloat(value.RedFuseSeconds);
            writer.WriteFloat(value.RedBlastRadius);
            writer.WriteVector3(value.ArenaCenter);
        }

        public static WolbaekFinalStart ReadWolbaekFinalStart(this NetworkReader reader)
        {
            int     bossId      = reader.ReadInt();
            int     castId      = reader.ReadInt();
            double  startTime   = reader.ReadDouble();
            double  collapse    = reader.ReadDouble();
            float   central     = reader.ReadFloat();
            int     yellowCount = reader.ReadInt();
            float   yellowMax   = reader.ReadFloat();
            float   redTrack    = reader.ReadFloat();
            float   redFuse     = reader.ReadFloat();
            float   redBlast    = reader.ReadFloat();
            Vector3 center      = reader.ReadVector3();
            return new WolbaekFinalStart(bossId, castId, startTime, collapse, central, yellowCount, yellowMax, redTrack, redFuse,
                redBlast, center);
        }

        public static void WriteWolbaekFinalYellow(this NetworkWriter writer, WolbaekFinalYellow value)
        {
            writer.WriteInt(value.Index);
            writer.WriteInt(value.CombatId);
            writer.WriteVector3(value.Position);
            writer.WriteFloat(value.MaxHealth);
        }

        public static WolbaekFinalYellow ReadWolbaekFinalYellow(this NetworkReader reader)
        {
            int     index     = reader.ReadInt();
            int     combatId  = reader.ReadInt();
            Vector3 position  = reader.ReadVector3();
            float   maxHealth = reader.ReadFloat();
            return new WolbaekFinalYellow(index, combatId, position, maxHealth);
        }

        // Rpc 인자로 쓰는 배열이다. Weaver의 기본 배열 쓰기 대신 이 메서드를 써서 상한을 건다
        public static void WriteWolbaekFinalYellowArray(this NetworkWriter writer, WolbaekFinalYellow[] value)
        {
            int count = WriteCount(writer, value, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS, "노란 여우불");
            for (int i = 0; i < count; ++i)
            {
                writer.WriteWolbaekFinalYellow(value[i]);
            }
        }

        public static WolbaekFinalYellow[] ReadWolbaekFinalYellowArray(this NetworkReader reader)
        {
            int count   = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS);
            var yellows = new WolbaekFinalYellow[count];
            for (int i = 0; i < count; ++i)
            {
                yellows[i] = reader.ReadWolbaekFinalYellow();
            }

            return yellows;
        }

        public static void WriteWolbaekFinalBundle(this NetworkWriter writer, WolbaekFinalBundle value)
        {
            writer.WriteInt(value.Index);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteDouble(value.LockServerTime);
            writer.WriteDouble(value.BlastServerTime);

            int count = WriteCount(writer, value.Reds, Constants.Network.BOSS_WOLBAEK_MAX_REDS, "빨간 여우불");
            for (int i = 0; i < count; ++i)
            {
                WolbaekFinalRed red = value.Reds[i];
                writer.WriteInt(red.Index);
                writer.WriteInt(red.TargetCombatId);
            }
        }

        public static WolbaekFinalBundle ReadWolbaekFinalBundle(this NetworkReader reader)
        {
            int    index     = reader.ReadInt();
            double startTime = reader.ReadDouble();
            double lockTime  = reader.ReadDouble();
            double blastTime = reader.ReadDouble();
            int    count     = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_REDS);

            var reds = new WolbaekFinalRed[count];
            for (int i = 0; i < count; ++i)
            {
                int redIndex = reader.ReadInt();
                int target   = reader.ReadInt();
                reds[i] = new WolbaekFinalRed(redIndex, target);
            }

            return new WolbaekFinalBundle(index, startTime, lockTime, blastTime, reds);
        }

        public static void WriteWolbaekFinalSnapshot(this NetworkWriter writer, WolbaekFinalSnapshot value)
        {
            writer.WriteByte((byte)value.Step);
            writer.WriteWolbaekFinalStart(value.Start);
            writer.WriteBool(value.OuterFloorRemoved);

            int yellowCount = WriteCount(writer, value.Yellows, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS, "노란 여우불");
            for (int i = 0; i < yellowCount; ++i)
            {
                writer.WriteWolbaekFinalYellow(value.Yellows[i]);
            }

            int healthCount = WriteCount(writer, value.YellowHealth, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS, "노란 여우불 체력");
            for (int i = 0; i < healthCount; ++i)
            {
                writer.WriteFloat(value.YellowHealth[i]);
            }

            writer.WriteBool(value.HasBundle);
            writer.WriteWolbaekFinalBundle(value.Bundle);
            WritePositions(writer, value.RedPositions, Constants.Network.BOSS_WOLBAEK_MAX_REDS, "빨간 여우불 위치");
            WriteFlags(writer, value.RedDetached, Constants.Network.BOSS_WOLBAEK_MAX_REDS, "빨간 여우불 분리");
            writer.WriteBool(value.RedsLocked);
        }

        public static WolbaekFinalSnapshot ReadWolbaekFinalSnapshot(this NetworkReader reader)
        {
            var               step         = (EWolbaekFinalStep)ReadEnumByte(reader, MAX_FINAL_STEP);
            WolbaekFinalStart start        = reader.ReadWolbaekFinalStart();
            bool              floorRemoved = reader.ReadBool();

            int yellowCount = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS);
            var yellows     = new WolbaekFinalYellow[yellowCount];
            for (int i = 0; i < yellowCount; ++i)
            {
                yellows[i] = reader.ReadWolbaekFinalYellow();
            }

            int healthCount = ReadCount(reader, Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS);
            var health      = new float[healthCount];
            for (int i = 0; i < healthCount; ++i)
            {
                health[i] = reader.ReadFloat();
            }

            bool               hasBundle = reader.ReadBool();
            WolbaekFinalBundle bundle    = reader.ReadWolbaekFinalBundle();
            Vector3[]          positions = ReadPositions(reader, Constants.Network.BOSS_WOLBAEK_MAX_REDS);
            bool[]             detached  = ReadFlags(reader, Constants.Network.BOSS_WOLBAEK_MAX_REDS);
            bool               locked    = reader.ReadBool();
            return new WolbaekFinalSnapshot(step, in start, floorRemoved, yellows, health, hasBundle, in bundle, positions, detached,
                locked);
        }

        public static void WriteBossWolbaekFinalRestore(this NetworkWriter writer, BossWolbaekFinalRestore value)
        {
            writer.WriteWolbaekFinalSnapshot(value.Snapshot);
        }

        public static BossWolbaekFinalRestore ReadBossWolbaekFinalRestore(this NetworkReader reader)
        {
            return new BossWolbaekFinalRestore
            {
                Snapshot = reader.ReadWolbaekFinalSnapshot()
            };
        }

        /****************************************
        *               Shared
        ****************************************/

        // 쓰기가 상한을 넘는 목록에서 예외를 내면 서버 틱이나 스폰 직렬화가 멈춘다. 보내는 쪽과 기록은 이 검사로 먼저 거른다
        /// <summary> 위치 배열이 비어 있지 않고 상한 안인가 </summary>
        public static bool IsWithin(Vector3[] values, int max)
        {
            return (values != null) && (values.Length <= max);
        }

        /// <summary> 부적진 시작 값을 쓸 수 있는가 </summary>
        public static bool CanWrite(in WolbaekTalismanStart start)
        {
            return CountOf(start.Structures) <= Constants.Network.BOSS_WOLBAEK_MAX_STRUCTURES;
        }

        /// <summary> 색 결계 시작 값을 쓸 수 있는가 </summary>
        public static bool CanWrite(in WolbaekWardStart start)
        {
            return CountOf(start.Members) <= Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS;
        }

        /// <summary> 봉인 차례 값을 쓸 수 있는가 </summary>
        public static bool CanWrite(in WolbaekSealTurn turn)
        {
            return (CountOf(turn.Cores) <= Constants.Network.BOSS_WOLBAEK_MAX_CORES)
                && (CountOf(turn.Foxfires) <= Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES);
        }

        /// <summary> 빨간 여우불 묶음을 쓸 수 있는가 </summary>
        public static bool CanWrite(in WolbaekFinalBundle bundle)
        {
            return CountOf(bundle.Reds) <= Constants.Network.BOSS_WOLBAEK_MAX_REDS;
        }

        /// <summary> 노란 여우불 목록을 쓸 수 있는가 </summary>
        public static bool CanWrite(WolbaekFinalYellow[] yellows)
        {
            return (yellows != null) && (yellows.Length <= Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS);
        }

        private static int CountOf<T>(IReadOnlyList<T> values)
        {
            return (values != null) ? values.Count : 0;
        }

        private static int WriteCount<T>(NetworkWriter writer, IReadOnlyList<T> values, int max, string name)
        {
            int count = (values != null) ? values.Count : 0;
            if (count > max)
            {
                throw new InvalidDataException($"월백 기믹 {name} 수가 상한을 넘는다, count={count}, max={max}");
            }

            writer.WriteByte((byte)count);
            return count;
        }

        private static void WritePositions(NetworkWriter writer, IReadOnlyList<Vector3> values, int max, string name)
        {
            int count = WriteCount(writer, values, max, name);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteVector3(values[i]);
            }
        }

        private static Vector3[] ReadPositions(NetworkReader reader, int max)
        {
            int count     = ReadCount(reader, max);
            var positions = new Vector3[count];
            for (int i = 0; i < count; ++i)
            {
                positions[i] = reader.ReadVector3();
            }

            return positions;
        }

        private static void WriteFlags(NetworkWriter writer, IReadOnlyList<bool> values, int max, string name)
        {
            int count = WriteCount(writer, values, max, name);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteBool(values[i]);
            }
        }

        private static bool[] ReadFlags(NetworkReader reader, int max)
        {
            int count = ReadCount(reader, max);
            var flags = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                flags[i] = reader.ReadBool();
            }

            return flags;
        }

        private static int ReadCount(NetworkReader reader, int max)
        {
            byte count = reader.ReadByte();
            if (count > max)
            {
                throw new InvalidDataException($"월백 기믹 목록 수가 상한을 넘는다, count={count}, max={max}");
            }

            return count;
        }

        private static byte ReadEnumByte(NetworkReader reader, byte max)
        {
            byte value = reader.ReadByte();
            if (value > max)
            {
                throw new InvalidDataException($"월백 기믹 열거형 값이 범위를 벗어났다, value={value}, max={max}");
            }

            return value;
        }
    }
}
