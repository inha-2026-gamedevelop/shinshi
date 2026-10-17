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
    /// <summary> 보스 사건과 복원 값의 Mirror 직렬화, Weaver가 Rpc 인자에 이 메서드를 쓴다 </summary>
    public static class BossNetSerializer
    {
        // 시작 값 구조체는 바꿀 수 없게 private readonly 필드로 되어 있어 Weaver가 자동으로 쓰지 못한다. 여기서 순서를 고정한다.
        // 순서를 바꾸면 서버와 클라이언트가 다른 값을 읽으므로 둘을 함께 배포한다(PROTOCOL_VERSION이 같아야 접속된다).
        // 범위를 벗어난 열거형 값은 프로토콜 불일치이므로 예외로 읽기를 멈춘다. Mirror가 해당 메시지를 버리고 오류를 남긴다

        /****************************************
        *              Constants
        ****************************************/

        private const byte MAX_SHAPE            = (byte)EBossTelegraphShape.Projectile;
        private const byte MAX_TELEGRAPH_PHASE  = (byte)EBossTelegraphPhase.Cancelled;
        private const byte MAX_PROJECTILE_FATE  = (byte)EBossProjectileFate.Cancelled;
        private const byte MAX_AREA_FATE        = (byte)EBossAreaFate.Cancelled;

        // 타 목록의 형태는 한 번에 터지는 셋뿐이다. 돌진과 투사체는 받지 않는다
        private const byte MAX_STRIKE_SHAPE      = (byte)EBossTelegraphShape.Line;
        private const byte MAX_STRIKE_CHAIN_KIND = (byte)EBossStrikeChainKind.Sequence;

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteBossTelegraphStart(this NetworkWriter writer, BossTelegraphStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.GroupId);
            writer.WriteString(value.PatternId);
            writer.WriteByte((byte)value.Shape);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Length);
            writer.WriteFloat(value.Width);
            writer.WriteFloat(value.ArcAngle);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
        }

        public static BossTelegraphStart ReadBossTelegraphStart(this NetworkReader reader)
        {
            int                 bossId    = reader.ReadInt();
            int                 castId    = reader.ReadInt();
            int                 groupId   = reader.ReadInt();
            string              patternId = reader.ReadString();
            EBossTelegraphShape shape     = (EBossTelegraphShape)ReadEnumByte(reader, MAX_SHAPE);
            Vector3             origin    = reader.ReadVector3();
            Vector3             direction = reader.ReadVector3();
            float               length    = reader.ReadFloat();
            float               width     = reader.ReadFloat();
            float               arcAngle  = reader.ReadFloat();
            double              startTime = reader.ReadDouble();
            float               seconds   = reader.ReadFloat();
            return new BossTelegraphStart(bossId, castId, groupId, patternId, shape, origin, direction, length, width,
                arcAngle, startTime, seconds);
        }

        // 시전 번호도 싣는다. 한 시전이 여러 발을 쏘면 투사체 번호와 달라지고 표현은 시전 번호로 예고를 찾아 마무리한다
        public static void WriteBossProjectileStart(this NetworkWriter writer, BossProjectileStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.ProjectileId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Speed);
            writer.WriteFloat(value.Width);
            writer.WriteFloat(value.PathLength);
            writer.WriteDouble(value.LaunchServerTime);
        }

        public static BossProjectileStart ReadBossProjectileStart(this NetworkReader reader)
        {
            int     bossId       = reader.ReadInt();
            int     projectileId = reader.ReadInt();
            int     castId       = reader.ReadInt();
            Vector3 origin       = reader.ReadVector3();
            Vector3 direction    = reader.ReadVector3();
            float   speed        = reader.ReadFloat();
            float   width        = reader.ReadFloat();
            float   pathLength   = reader.ReadFloat();
            double  launchTime   = reader.ReadDouble();
            return new BossProjectileStart(bossId, projectileId, castId, origin, direction, speed, width, pathLength,
                launchTime);
        }

        public static void WriteBossAreaStart(this NetworkWriter writer, BossAreaStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.AreaId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Center);
            writer.WriteFloat(value.Radius);
            writer.WriteFloat(value.DurationSeconds);
            writer.WriteFloat(value.TickInterval);
            writer.WriteDouble(value.ActivateServerTime);
        }

        public static BossAreaStart ReadBossAreaStart(this NetworkReader reader)
        {
            int     bossId       = reader.ReadInt();
            int     areaId       = reader.ReadInt();
            int     castId       = reader.ReadInt();
            Vector3 center       = reader.ReadVector3();
            float   radius       = reader.ReadFloat();
            float   duration     = reader.ReadFloat();
            float   tickInterval = reader.ReadFloat();
            double  activateTime = reader.ReadDouble();
            return new BossAreaStart(bossId, areaId, castId, center, radius, duration, tickInterval, activateTime);
        }

        public static void WriteBossTelegraphSnapshot(this NetworkWriter writer, BossTelegraphSnapshot value)
        {
            writer.WriteBossTelegraphStart(value.Start);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Length);
            writer.WriteBool(value.IsDirectionLocked);
            writer.WriteByte((byte)value.Phase);
            writer.WriteDouble(value.ExecuteServerTime);
            writer.WriteFloat(value.DashDistance);
            writer.WriteFloat(value.DashSeconds);
            writer.WriteFloat(value.DashSpeed);
            writer.WriteFloat(value.DashStoppedDistance);
            writer.WriteBool(value.DashStoppedByWall);
            writer.WriteDouble(value.DashStoppedServerTime);
        }

        public static BossTelegraphSnapshot ReadBossTelegraphSnapshot(this NetworkReader reader)
        {
            return new BossTelegraphSnapshot
            {
                Start                 = reader.ReadBossTelegraphStart(),
                Direction             = reader.ReadVector3(),
                Length                = reader.ReadFloat(),
                IsDirectionLocked     = reader.ReadBool(),
                Phase                 = (EBossTelegraphPhase)ReadEnumByte(reader, MAX_TELEGRAPH_PHASE),
                ExecuteServerTime     = reader.ReadDouble(),
                DashDistance          = reader.ReadFloat(),
                DashSeconds           = reader.ReadFloat(),
                DashSpeed             = reader.ReadFloat(),
                DashStoppedDistance   = reader.ReadFloat(),
                DashStoppedByWall     = reader.ReadBool(),
                DashStoppedServerTime = reader.ReadDouble()
            };
        }

        public static void WriteBossProjectileSnapshot(this NetworkWriter writer, BossProjectileSnapshot value)
        {
            writer.WriteBossProjectileStart(value.Start);
            writer.WriteByte((byte)value.Fate);
            writer.WriteFloat(value.EndDistance);
        }

        public static BossProjectileSnapshot ReadBossProjectileSnapshot(this NetworkReader reader)
        {
            return new BossProjectileSnapshot
            {
                Start       = reader.ReadBossProjectileStart(),
                Fate        = (EBossProjectileFate)ReadEnumByte(reader, MAX_PROJECTILE_FATE),
                EndDistance = reader.ReadFloat()
            };
        }

        public static void WriteBossAreaSnapshot(this NetworkWriter writer, BossAreaSnapshot value)
        {
            writer.WriteBossAreaStart(value.Start);
            writer.WriteByte((byte)value.Fate);
            writer.WriteInt(value.LastTickIndex);
            writer.WriteDouble(value.LastTickServerTime);
        }

        public static BossAreaSnapshot ReadBossAreaSnapshot(this NetworkReader reader)
        {
            return new BossAreaSnapshot
            {
                Start              = reader.ReadBossAreaStart(),
                Fate               = (EBossAreaFate)ReadEnumByte(reader, MAX_AREA_FATE),
                LastTickIndex      = reader.ReadInt(),
                LastTickServerTime = reader.ReadDouble()
            };
        }

        public static void WriteBossSweepStart(this NetworkWriter writer, BossSweepStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.BaseDirection);
            writer.WriteBool(value.IsRight);
            writer.WriteFloat(value.Radius);
            writer.WriteFloat(value.TotalArcAngle);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteFloat(value.BlastSpanSeconds);
            writer.WriteDouble(value.StartServerTime);
        }

        public static BossSweepStart ReadBossSweepStart(this NetworkReader reader)
        {
            int     bossId        = reader.ReadInt();
            int     castId        = reader.ReadInt();
            Vector3 origin        = reader.ReadVector3();
            Vector3 baseDirection = reader.ReadVector3();
            bool    isRight       = reader.ReadBool();
            float   radius        = reader.ReadFloat();
            float   totalArc      = reader.ReadFloat();
            float   telegraph     = reader.ReadFloat();
            float   blastSpan     = reader.ReadFloat();
            double  startTime     = reader.ReadDouble();
            return new BossSweepStart(bossId, castId, origin, baseDirection, isRight, radius, totalArc, telegraph, blastSpan,
                startTime);
        }

        public static void WriteBossBeamShape(this NetworkWriter writer, BossBeamShape value)
        {
            writer.WriteVector3(value.ArenaCenter);
            writer.WriteFloat(value.ArenaRadius);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.LateralOffset);
            writer.WriteFloat(value.Width);
        }

        public static BossBeamShape ReadBossBeamShape(this NetworkReader reader)
        {
            Vector3 arenaCenter   = reader.ReadVector3();
            float   arenaRadius   = reader.ReadFloat();
            Vector3 direction     = reader.ReadVector3();
            float   lateralOffset = reader.ReadFloat();
            float   width         = reader.ReadFloat();
            return new BossBeamShape(arenaCenter, arenaRadius, direction, lateralOffset, width);
        }

        public static void WriteBossBeamStart(this NetworkWriter writer, BossBeamStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.BeamId);
            writer.WriteInt(value.CastId);
            writer.WriteBossBeamShape(value.Shape);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteFloat(value.DurationSeconds);
            writer.WriteFloat(value.TickInterval);
        }

        public static BossBeamStart ReadBossBeamStart(this NetworkReader reader)
        {
            int           bossId       = reader.ReadInt();
            int           beamId       = reader.ReadInt();
            int           castId       = reader.ReadInt();
            BossBeamShape shape        = reader.ReadBossBeamShape();
            double        startTime    = reader.ReadDouble();
            float         telegraph    = reader.ReadFloat();
            float         duration     = reader.ReadFloat();
            float         tickInterval = reader.ReadFloat();
            return new BossBeamStart(bossId, beamId, castId, in shape, startTime, telegraph, duration, tickInterval);
        }

        public static void WriteBossStarShape(this NetworkWriter writer, BossStarShape value)
        {
            writer.WriteVector3(value.ArenaCenter);
            writer.WriteFloat(value.ArenaRadius);
            writer.WriteVector3(value.BaseDirection);
            writer.WriteFloat(value.RingRadius);
            writer.WriteInt(value.VertexCount);
            writer.WriteInt(value.ConnectStep);
            writer.WriteFloat(value.BeamWidth);
            writer.WriteFloat(value.SignedDegreesPerSecond);
        }

        public static BossStarShape ReadBossStarShape(this NetworkReader reader)
        {
            Vector3 arenaCenter      = reader.ReadVector3();
            float   arenaRadius      = reader.ReadFloat();
            Vector3 baseDirection    = reader.ReadVector3();
            float   ringRadius       = reader.ReadFloat();
            int     vertexCount      = reader.ReadInt();
            int     connectStep      = reader.ReadInt();
            float   beamWidth        = reader.ReadFloat();
            float   degreesPerSecond = reader.ReadFloat();
            return new BossStarShape(arenaCenter, arenaRadius, baseDirection, ringRadius, vertexCount, connectStep, beamWidth,
                degreesPerSecond);
        }

        public static void WriteBossStarStart(this NetworkWriter writer, BossStarStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.StarId);
            writer.WriteInt(value.CastId);
            writer.WriteBossStarShape(value.Shape);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteFloat(value.DurationSeconds);
            writer.WriteFloat(value.TickInterval);
        }

        public static BossStarStart ReadBossStarStart(this NetworkReader reader)
        {
            int           bossId       = reader.ReadInt();
            int           starId       = reader.ReadInt();
            int           castId       = reader.ReadInt();
            BossStarShape shape        = reader.ReadBossStarShape();
            double        startTime    = reader.ReadDouble();
            float         telegraph    = reader.ReadFloat();
            float         duration     = reader.ReadFloat();
            float         tickInterval = reader.ReadFloat();
            return new BossStarStart(bossId, starId, castId, in shape, startTime, telegraph, duration, tickInterval);
        }

        public static void WriteBossBarrierStart(this NetworkWriter writer, BossBarrierStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.BarrierId);
            writer.WriteVector3(value.Center);
            writer.WriteFloat(value.OuterRadius);
            writer.WriteFloat(value.InnerRadius);
            writer.WriteFloat(value.TickInterval);
            writer.WriteDouble(value.TelegraphServerTime);
            writer.WriteDouble(value.ActivateServerTime);
            writer.WriteDouble(value.CompleteServerTime);
        }

        public static BossBarrierStart ReadBossBarrierStart(this NetworkReader reader)
        {
            int     bossId        = reader.ReadInt();
            int     barrierId     = reader.ReadInt();
            Vector3 center        = reader.ReadVector3();
            float   outerRadius   = reader.ReadFloat();
            float   innerRadius   = reader.ReadFloat();
            float   tickInterval  = reader.ReadFloat();
            double  telegraphTime = reader.ReadDouble();
            double  activateTime  = reader.ReadDouble();
            double  completeTime  = reader.ReadDouble();
            return new BossBarrierStart(bossId, barrierId, center, outerRadius, innerRadius, tickInterval, telegraphTime,
                activateTime, completeTime);
        }

        public static void WriteBossBeamSnapshot(this NetworkWriter writer, BossBeamSnapshot value)
        {
            writer.WriteBossBeamStart(value.Start);
            writer.WriteBool(value.IsActivated);
            writer.WriteDouble(value.ActivateServerTime);
            writer.WriteByte((byte)value.Fate);
            writer.WriteInt(value.LastTickIndex);
            writer.WriteDouble(value.LastTickServerTime);
        }

        public static BossBeamSnapshot ReadBossBeamSnapshot(this NetworkReader reader)
        {
            return new BossBeamSnapshot
            {
                Start              = reader.ReadBossBeamStart(),
                IsActivated        = reader.ReadBool(),
                ActivateServerTime = reader.ReadDouble(),
                Fate               = (EBossAreaFate)ReadEnumByte(reader, MAX_AREA_FATE),
                LastTickIndex      = reader.ReadInt(),
                LastTickServerTime = reader.ReadDouble()
            };
        }

        public static void WriteBossStarSnapshot(this NetworkWriter writer, BossStarSnapshot value)
        {
            writer.WriteBossStarStart(value.Start);
            writer.WriteBool(value.IsActivated);
            writer.WriteDouble(value.ActivateServerTime);
            writer.WriteByte((byte)value.Fate);
            writer.WriteInt(value.LastTickIndex);
            writer.WriteDouble(value.LastTickServerTime);
        }

        public static BossStarSnapshot ReadBossStarSnapshot(this NetworkReader reader)
        {
            return new BossStarSnapshot
            {
                Start              = reader.ReadBossStarStart(),
                IsActivated        = reader.ReadBool(),
                ActivateServerTime = reader.ReadDouble(),
                Fate               = (EBossAreaFate)ReadEnumByte(reader, MAX_AREA_FATE),
                LastTickIndex      = reader.ReadInt(),
                LastTickServerTime = reader.ReadDouble()
            };
        }

        /****************************************
        *               Mucheol
        ****************************************/

        // 타 목록은 길이 하나와 같은 길이의 값 묶음으로 보낸다. 형태는 한 번에 터지는 Fan, Circle, Line만 받는다
        public static void WriteBossStrikeChainStart(this NetworkWriter writer, BossStrikeChainStart value)
        {
            int stepCount = value.StepCount;
            if ((stepCount > Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS) || (!HasStepCount(value.Ranges, stepCount))
                || (!HasStepCount(value.ArcAngles, stepCount)) || (!HasStepCount(value.Widths, stepCount))
                || (!HasStepCount(value.AimOffsetDegrees, stepCount)) || (!HasStepCount(value.DelaySeconds, stepCount)))
            {
                throw new InvalidDataException($"보스 타 목록의 길이가 어긋났거나 상한을 넘는다, count={stepCount}");
            }

            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteString(value.PatternId);
            writer.WriteByte((byte)value.Kind);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteByte((byte)stepCount);
            for (int i = 0; i < stepCount; ++i)
            {
                writer.WriteByte((byte)value.Shapes[i]);
                writer.WriteFloat(value.Ranges[i]);
                writer.WriteFloat(value.ArcAngles[i]);
                writer.WriteFloat(value.Widths[i]);
                writer.WriteFloat(value.AimOffsetDegrees[i]);
                writer.WriteFloat(value.DelaySeconds[i]);
            }
        }

        public static BossStrikeChainStart ReadBossStrikeChainStart(this NetworkReader reader)
        {
            int                  bossId    = reader.ReadInt();
            int                  castId    = reader.ReadInt();
            string               patternId = reader.ReadString();
            EBossStrikeChainKind kind      = (EBossStrikeChainKind)ReadEnumByte(reader, MAX_STRIKE_CHAIN_KIND);
            Vector3              origin    = reader.ReadVector3();
            Vector3              direction = reader.ReadVector3();
            double               startTime = reader.ReadDouble();
            float                seconds   = reader.ReadFloat();
            int                  stepCount = ReadCount(reader, Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS);

            var shapes    = new EBossTelegraphShape[stepCount];
            var ranges    = new float[stepCount];
            var arcAngles = new float[stepCount];
            var widths    = new float[stepCount];
            var offsets   = new float[stepCount];
            var delays    = new float[stepCount];
            for (int i = 0; i < stepCount; ++i)
            {
                shapes[i]    = (EBossTelegraphShape)ReadEnumByte(reader, MAX_STRIKE_SHAPE);
                ranges[i]    = reader.ReadFloat();
                arcAngles[i] = reader.ReadFloat();
                widths[i]    = reader.ReadFloat();
                offsets[i]   = reader.ReadFloat();
                delays[i]    = reader.ReadFloat();
            }

            return new BossStrikeChainStart(bossId, castId, patternId, kind, origin, direction, startTime, seconds, shapes,
                ranges, arcAngles, widths, offsets, delays);
        }

        public static void WriteBossStrikeChainSnapshot(this NetworkWriter writer, BossStrikeChainSnapshot value)
        {
            writer.WriteBossStrikeChainStart(value.Start);
            writer.WriteVector3(value.Direction);
            writer.WriteBool(value.IsDirectionLocked);
            writer.WriteInt(value.StruckCount);
        }

        public static BossStrikeChainSnapshot ReadBossStrikeChainSnapshot(this NetworkReader reader)
        {
            return new BossStrikeChainSnapshot
            {
                Start             = reader.ReadBossStrikeChainStart(),
                Direction         = reader.ReadVector3(),
                IsDirectionLocked = reader.ReadBool(),
                StruckCount       = reader.ReadInt()
            };
        }

        public static void WriteBossMultiDashStart(this NetworkWriter writer, BossMultiDashStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteString(value.PatternId);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Length);
            writer.WriteFloat(value.Width);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteInt(value.DashCount);
        }

        public static BossMultiDashStart ReadBossMultiDashStart(this NetworkReader reader)
        {
            int     bossId    = reader.ReadInt();
            int     castId    = reader.ReadInt();
            string  patternId = reader.ReadString();
            Vector3 origin    = reader.ReadVector3();
            Vector3 direction = reader.ReadVector3();
            float   length    = reader.ReadFloat();
            float   width     = reader.ReadFloat();
            double  startTime = reader.ReadDouble();
            float   seconds   = reader.ReadFloat();
            int     dashCount = reader.ReadInt();
            return new BossMultiDashStart(bossId, castId, patternId, origin, direction, length, width, startTime, seconds,
                dashCount);
        }

        public static void WriteBossMultiDashReaim(this NetworkWriter writer, BossMultiDashReaim value)
        {
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.DashIndex);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Length);
            writer.WriteFloat(value.Width);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.ReaimSeconds);
        }

        public static BossMultiDashReaim ReadBossMultiDashReaim(this NetworkReader reader)
        {
            int     castId    = reader.ReadInt();
            int     dashIndex = reader.ReadInt();
            Vector3 origin    = reader.ReadVector3();
            Vector3 direction = reader.ReadVector3();
            float   length    = reader.ReadFloat();
            float   width     = reader.ReadFloat();
            double  startTime = reader.ReadDouble();
            float   seconds   = reader.ReadFloat();
            return new BossMultiDashReaim(castId, dashIndex, origin, direction, length, width, startTime, seconds);
        }

        public static void WriteBossMultiDashSnapshot(this NetworkWriter writer, BossMultiDashSnapshot value)
        {
            writer.WriteBossMultiDashStart(value.Start);
            writer.WriteInt(value.DashIndex);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Length);
            writer.WriteFloat(value.Width);
            writer.WriteBool(value.IsDirectionLocked);
            writer.WriteDouble(value.TelegraphStartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteBool(value.HasDashStarted);
            writer.WriteDouble(value.DashStartServerTime);
            writer.WriteFloat(value.DashDistance);
            writer.WriteFloat(value.DashSeconds);
            writer.WriteFloat(value.DashSpeed);
            writer.WriteFloat(value.StoppedDistance);
            writer.WriteBool(value.StoppedByWall);
            writer.WriteDouble(value.StoppedServerTime);
            writer.WriteBool(value.IsTruncated);
        }

        public static BossMultiDashSnapshot ReadBossMultiDashSnapshot(this NetworkReader reader)
        {
            return new BossMultiDashSnapshot
            {
                Start                    = reader.ReadBossMultiDashStart(),
                DashIndex                = reader.ReadInt(),
                Origin                   = reader.ReadVector3(),
                Direction                = reader.ReadVector3(),
                Length                   = reader.ReadFloat(),
                Width                    = reader.ReadFloat(),
                IsDirectionLocked        = reader.ReadBool(),
                TelegraphStartServerTime = reader.ReadDouble(),
                TelegraphSeconds         = reader.ReadFloat(),
                HasDashStarted           = reader.ReadBool(),
                DashStartServerTime      = reader.ReadDouble(),
                DashDistance             = reader.ReadFloat(),
                DashSeconds              = reader.ReadFloat(),
                DashSpeed                = reader.ReadFloat(),
                StoppedDistance          = reader.ReadFloat(),
                StoppedByWall            = reader.ReadBool(),
                StoppedServerTime        = reader.ReadDouble(),
                IsTruncated              = reader.ReadBool()
            };
        }

        public static void WriteBossBattlefieldStart(this NetworkWriter writer, BossBattlefieldStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.Radius);
            writer.WriteFloat(value.ArcAngle);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.TelegraphSeconds);
        }

        public static BossBattlefieldStart ReadBossBattlefieldStart(this NetworkReader reader)
        {
            int     bossId    = reader.ReadInt();
            int     castId    = reader.ReadInt();
            Vector3 origin    = reader.ReadVector3();
            Vector3 direction = reader.ReadVector3();
            float   radius    = reader.ReadFloat();
            float   arcAngle  = reader.ReadFloat();
            double  startTime = reader.ReadDouble();
            float   seconds   = reader.ReadFloat();
            return new BossBattlefieldStart(bossId, castId, origin, direction, radius, arcAngle, startTime, seconds);
        }

        public static void WriteBossBattlefieldSnapshot(this NetworkWriter writer, BossBattlefieldSnapshot value)
        {
            writer.WriteBossBattlefieldStart(value.Start);
            writer.WriteBool(value.IsStruck);
            writer.WriteDouble(value.StruckServerTime);
        }

        public static BossBattlefieldSnapshot ReadBossBattlefieldSnapshot(this NetworkReader reader)
        {
            return new BossBattlefieldSnapshot
            {
                Start            = reader.ReadBossBattlefieldStart(),
                IsStruck         = reader.ReadBool(),
                StruckServerTime = reader.ReadDouble()
            };
        }

        public static void WriteBossChargeOutcome(this NetworkWriter writer, BossChargeOutcome value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteBool(value.IsCollision);
            writer.WriteVector3(value.Position);
            writer.WriteFloat(value.TravelDistance);
            writer.WriteInt(value.StructureId);
            writer.WriteFloat(value.RecoverySeconds);
            writer.WriteDouble(value.ServerTime);
        }

        public static BossChargeOutcome ReadBossChargeOutcome(this NetworkReader reader)
        {
            int     bossId      = reader.ReadInt();
            int     castId      = reader.ReadInt();
            bool    isCollision = reader.ReadBool();
            Vector3 position    = reader.ReadVector3();
            float   travel      = reader.ReadFloat();
            int     structureId = reader.ReadInt();
            float   recovery    = reader.ReadFloat();
            double  serverTime  = reader.ReadDouble();
            return new BossChargeOutcome(bossId, castId, isCollision, position, travel, structureId, recovery, serverTime);
        }

        public static void WriteBossChargeGroggy(this NetworkWriter writer, BossChargeGroggy value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.StructureId);
            writer.WriteDouble(value.CollisionServerTime);
            writer.WriteDouble(value.GroggyStartServerTime);
            writer.WriteDouble(value.GroggyEndServerTime);
        }

        public static BossChargeGroggy ReadBossChargeGroggy(this NetworkReader reader)
        {
            int    bossId      = reader.ReadInt();
            int    castId      = reader.ReadInt();
            int    structureId = reader.ReadInt();
            double collision   = reader.ReadDouble();
            double start       = reader.ReadDouble();
            double end         = reader.ReadDouble();
            return new BossChargeGroggy(bossId, castId, structureId, collision, start, end);
        }

        /****************************************
        *          Wolbaek Hard Phase 2
        ****************************************/

        public static void WriteBossStarStageStart(this NetworkWriter writer, BossStarStageStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.StarId);
            writer.WriteBossStarShape(value.Shape);
            writer.WriteFloat(value.StepDegrees);
            writer.WriteInt(value.StageCount);
            writer.WriteDouble(value.CastStartServerTime);
            writer.WriteFloat(value.FirstStrikeSeconds);
            writer.WriteFloat(value.StrikeIntervalSeconds);
        }

        public static BossStarStageStart ReadBossStarStageStart(this NetworkReader reader)
        {
            int           bossId      = reader.ReadInt();
            int           castId      = reader.ReadInt();
            int           starId      = reader.ReadInt();
            BossStarShape shape       = reader.ReadBossStarShape();
            float         stepDegrees = reader.ReadFloat();
            int           stageCount  = reader.ReadInt();
            double        startTime   = reader.ReadDouble();
            float         firstStrike = reader.ReadFloat();
            float         interval    = reader.ReadFloat();
            return new BossStarStageStart(bossId, castId, starId, shape, stepDegrees, stageCount, startTime, firstStrike,
                interval);
        }

        public static void WriteBossStarStageSnapshot(this NetworkWriter writer, BossStarStageSnapshot value)
        {
            writer.WriteBossStarStageStart(value.Start);
            writer.WriteInt(value.StruckCount);
        }

        public static BossStarStageSnapshot ReadBossStarStageSnapshot(this NetworkReader reader)
        {
            return new BossStarStageSnapshot
            {
                Start       = reader.ReadBossStarStageStart(),
                StruckCount = reader.ReadInt()
            };
        }

        public static void WriteBossGridBeamStart(this NetworkWriter writer, BossGridBeamStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.ArenaCenter);
            writer.WriteFloat(value.ArenaRadius);
            writer.WriteInt(value.LineCount);
            writer.WriteFloat(value.LineWidth);
            writer.WriteFloat(value.LineSpacing);
            writer.WriteDouble(value.CastStartServerTime);
            writer.WriteFloat(value.FirstStrikeSeconds);
            writer.WriteFloat(value.StrikeIntervalSeconds);
            writer.WriteFloat(value.AxisGapSeconds);
            writer.WriteInt(value.FirstLineId);
        }

        public static BossGridBeamStart ReadBossGridBeamStart(this NetworkReader reader)
        {
            int     bossId      = reader.ReadInt();
            int     castId      = reader.ReadInt();
            Vector3 arenaCenter = reader.ReadVector3();
            float   arenaRadius = reader.ReadFloat();
            int     lineCount   = reader.ReadInt();
            float   lineWidth   = reader.ReadFloat();
            float   lineSpacing = reader.ReadFloat();
            double  startTime   = reader.ReadDouble();
            float   firstStrike = reader.ReadFloat();
            float   interval    = reader.ReadFloat();
            float   axisGap     = reader.ReadFloat();
            int     firstLineId = reader.ReadInt();
            return new BossGridBeamStart(bossId, castId, arenaCenter, arenaRadius, lineCount, lineWidth, lineSpacing,
                startTime, firstStrike, interval, axisGap, firstLineId);
        }

        public static void WriteBossGridBeamSnapshot(this NetworkWriter writer, BossGridBeamSnapshot value)
        {
            writer.WriteBossGridBeamStart(value.Start);
            writer.WriteInt(value.TelegraphedCount);
            writer.WriteInt(value.StruckCount);
        }

        public static BossGridBeamSnapshot ReadBossGridBeamSnapshot(this NetworkReader reader)
        {
            return new BossGridBeamSnapshot
            {
                Start            = reader.ReadBossGridBeamStart(),
                TelegraphedCount = reader.ReadInt(),
                StruckCount      = reader.ReadInt()
            };
        }

        public static void WriteBossChainFanStart(this NetworkWriter writer, BossChainFanStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Origin);
            writer.WriteVector3(value.SafeDirection);
            writer.WriteFloat(value.Range);
            writer.WriteInt(value.ZoneCount);
            writer.WriteFloat(value.ZoneArc);
            writer.WriteDouble(value.CastStartServerTime);
            writer.WriteFloat(value.SafeRevealSeconds);
            writer.WriteFloat(value.FirstBlastSeconds);
            writer.WriteFloat(value.BlastIntervalSeconds);
            writer.WriteFloat(value.LingerSeconds);
        }

        public static BossChainFanStart ReadBossChainFanStart(this NetworkReader reader)
        {
            int     bossId        = reader.ReadInt();
            int     castId        = reader.ReadInt();
            Vector3 origin        = reader.ReadVector3();
            Vector3 safeDirection = reader.ReadVector3();
            float   range         = reader.ReadFloat();
            int     zoneCount     = reader.ReadInt();
            float   zoneArc       = reader.ReadFloat();
            double  startTime     = reader.ReadDouble();
            float   safeReveal    = reader.ReadFloat();
            float   firstBlast    = reader.ReadFloat();
            float   interval      = reader.ReadFloat();
            float   linger        = reader.ReadFloat();
            return new BossChainFanStart(bossId, castId, origin, safeDirection, range, zoneCount, zoneArc, startTime,
                safeReveal, firstBlast, interval, linger);
        }

        public static void WriteBossChainFanSnapshot(this NetworkWriter writer, BossChainFanSnapshot value)
        {
            writer.WriteBossChainFanStart(value.Start);
            writer.WriteInt(value.BlastedCount);
            writer.WriteBool(value.IsDangerEnded);
        }

        public static BossChainFanSnapshot ReadBossChainFanSnapshot(this NetworkReader reader)
        {
            return new BossChainFanSnapshot
            {
                Start         = reader.ReadBossChainFanStart(),
                BlastedCount  = reader.ReadInt(),
                IsDangerEnded = reader.ReadBool()
            };
        }

        public static void WriteBossTrackingAreaStart(this NetworkWriter writer, BossTrackingAreaStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.AreaId);
            writer.WriteInt(value.TargetCombatId);
            writer.WriteVector3(value.Center);
            writer.WriteFloat(value.Radius);
            writer.WriteDouble(value.StartServerTime);
            writer.WriteFloat(value.FollowSeconds);
            writer.WriteFloat(value.FixedSeconds);
        }

        public static BossTrackingAreaStart ReadBossTrackingAreaStart(this NetworkReader reader)
        {
            int     bossId    = reader.ReadInt();
            int     castId    = reader.ReadInt();
            int     areaId    = reader.ReadInt();
            int     targetId  = reader.ReadInt();
            Vector3 center    = reader.ReadVector3();
            float   radius    = reader.ReadFloat();
            double  startTime = reader.ReadDouble();
            float   follow    = reader.ReadFloat();
            float   fixedTime = reader.ReadFloat();
            return new BossTrackingAreaStart(bossId, castId, areaId, targetId, center, radius, startTime, follow, fixedTime);
        }

        public static void WriteBossTrackingAreaSnapshot(this NetworkWriter writer, BossTrackingAreaSnapshot value)
        {
            writer.WriteBossTrackingAreaStart(value.Start);
            writer.WriteVector3(value.Center);
            writer.WriteBool(value.IsFixed);
            writer.WriteDouble(value.FixedServerTime);
        }

        public static BossTrackingAreaSnapshot ReadBossTrackingAreaSnapshot(this NetworkReader reader)
        {
            return new BossTrackingAreaSnapshot
            {
                Start           = reader.ReadBossTrackingAreaStart(),
                Center          = reader.ReadVector3(),
                IsFixed         = reader.ReadBool(),
                FixedServerTime = reader.ReadDouble()
            };
        }

        public static void WriteBossBarrageStart(this NetworkWriter writer, BossBarrageStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteVector3(value.Origin);
            writer.WriteDouble(value.CastStartServerTime);
            writer.WriteInt(value.ShotCount);
            writer.WriteFloat(value.TelegraphSeconds);
            writer.WriteFloat(value.ShotIntervalSeconds);
            writer.WriteFloat(value.Width);
        }

        public static BossBarrageStart ReadBossBarrageStart(this NetworkReader reader)
        {
            int     bossId    = reader.ReadInt();
            int     castId    = reader.ReadInt();
            Vector3 origin    = reader.ReadVector3();
            double  startTime = reader.ReadDouble();
            int     shotCount = reader.ReadInt();
            float   telegraph = reader.ReadFloat();
            float   interval  = reader.ReadFloat();
            float   width     = reader.ReadFloat();
            return new BossBarrageStart(bossId, castId, origin, startTime, shotCount, telegraph, interval, width);
        }

        public static void WriteBossBarrageShot(this NetworkWriter writer, BossBarrageShot value)
        {
            writer.WriteInt(value.CastId);
            writer.WriteInt(value.ShotIndex);
            writer.WriteInt(value.SectorIndex);
            writer.WriteVector3(value.Direction);
            writer.WriteFloat(value.PathLength);
            writer.WriteDouble(value.ScheduledServerTime);
        }

        public static BossBarrageShot ReadBossBarrageShot(this NetworkReader reader)
        {
            int     castId      = reader.ReadInt();
            int     shotIndex   = reader.ReadInt();
            int     sectorIndex = reader.ReadInt();
            Vector3 direction   = reader.ReadVector3();
            float   pathLength  = reader.ReadFloat();
            double  scheduled   = reader.ReadDouble();
            return new BossBarrageShot(castId, shotIndex, sectorIndex, direction, pathLength, scheduled);
        }

        public static void WriteBossBarrageSnapshot(this NetworkWriter writer, BossBarrageSnapshot value)
        {
            int shotCount = (value.Shots != null) ? value.Shots.Length : 0;
            if (shotCount > Constants.Network.BOSS_BARRAGE_MAX_SHOTS)
            {
                throw new InvalidDataException($"보스 난사의 예고한 발 수가 상한을 넘는다, count={shotCount}");
            }

            writer.WriteBossBarrageStart(value.Start);
            writer.WriteInt(value.FiredCount);
            writer.WriteByte((byte)shotCount);
            for (int i = 0; i < shotCount; ++i)
            {
                writer.WriteBossBarrageShot(value.Shots[i]);
            }
        }

        public static BossBarrageSnapshot ReadBossBarrageSnapshot(this NetworkReader reader)
        {
            BossBarrageStart start      = reader.ReadBossBarrageStart();
            int              firedCount = reader.ReadInt();
            int              shotCount  = ReadCount(reader, Constants.Network.BOSS_BARRAGE_MAX_SHOTS);

            var shots = new BossBarrageShot[shotCount];
            for (int i = 0; i < shotCount; ++i)
            {
                shots[i] = reader.ReadBossBarrageShot();
            }

            return new BossBarrageSnapshot
            {
                Start      = start,
                FiredCount = firedCount,
                Shots      = shots
            };
        }

        public static void WriteBossCircleBurstStart(this NetworkWriter writer, BossCircleBurstStart value)
        {
            int circleCount = value.CircleCount;
            if ((circleCount > Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES) || (value.Radii == null)
                || (value.Radii.Count != circleCount))
            {
                throw new InvalidDataException($"보스 동시 폭발 원 목록의 길이가 어긋났거나 상한을 넘는다, count={circleCount}");
            }

            writer.WriteInt(value.BossId);
            writer.WriteInt(value.CastId);
            writer.WriteDouble(value.CastStartServerTime);
            writer.WriteFloat(value.StrikeSeconds);
            writer.WriteByte((byte)circleCount);
            for (int i = 0; i < circleCount; ++i)
            {
                writer.WriteVector3(value.Centers[i]);
                writer.WriteFloat(value.Radii[i]);
            }
        }

        public static BossCircleBurstStart ReadBossCircleBurstStart(this NetworkReader reader)
        {
            int    bossId      = reader.ReadInt();
            int    castId      = reader.ReadInt();
            double startTime   = reader.ReadDouble();
            float  strike      = reader.ReadFloat();
            int    circleCount = ReadCount(reader, Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES);

            var centers = new Vector3[circleCount];
            var radii   = new float[circleCount];
            for (int i = 0; i < circleCount; ++i)
            {
                centers[i] = reader.ReadVector3();
                radii[i]   = reader.ReadFloat();
            }

            return new BossCircleBurstStart(bossId, castId, centers, radii, startTime, strike);
        }

        public static void WriteBossCircleBurstSnapshot(this NetworkWriter writer, BossCircleBurstSnapshot value)
        {
            writer.WriteBossCircleBurstStart(value.Start);
            writer.WriteBool(value.IsStruck);
        }

        public static BossCircleBurstSnapshot ReadBossCircleBurstSnapshot(this NetworkReader reader)
        {
            return new BossCircleBurstSnapshot
            {
                Start    = reader.ReadBossCircleBurstStart(),
                IsStruck = reader.ReadBool()
            };
        }

        private static bool HasStepCount(IReadOnlyList<float> values, int stepCount)
        {
            return (values != null) && (values.Count == stepCount);
        }

        private static int ReadCount(NetworkReader reader, int max)
        {
            byte count = reader.ReadByte();
            if (count > max)
            {
                throw new InvalidDataException($"보스 사건 목록 수가 상한을 넘는다, count={count}, max={max}");
            }

            return count;
        }

        private static byte ReadEnumByte(NetworkReader reader, byte max)
        {
            byte value = reader.ReadByte();
            if (value > max)
            {
                throw new InvalidDataException($"보스 사건 열거형 값이 범위를 벗어났다, value={value}, max={max}");
            }

            return value;
        }
    }
}
