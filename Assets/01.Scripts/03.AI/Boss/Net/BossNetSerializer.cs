// System
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

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

        public static void WriteBossProjectileStart(this NetworkWriter writer, BossProjectileStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.ProjectileId);
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
            Vector3 origin       = reader.ReadVector3();
            Vector3 direction    = reader.ReadVector3();
            float   speed        = reader.ReadFloat();
            float   width        = reader.ReadFloat();
            float   pathLength   = reader.ReadFloat();
            double  launchTime   = reader.ReadDouble();
            return new BossProjectileStart(bossId, projectileId, origin, direction, speed, width, pathLength, launchTime);
        }

        public static void WriteBossAreaStart(this NetworkWriter writer, BossAreaStart value)
        {
            writer.WriteInt(value.BossId);
            writer.WriteInt(value.AreaId);
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
            Vector3 center       = reader.ReadVector3();
            float   radius       = reader.ReadFloat();
            float   duration     = reader.ReadFloat();
            float   tickInterval = reader.ReadFloat();
            double  activateTime = reader.ReadDouble();
            return new BossAreaStart(bossId, areaId, center, radius, duration, tickInterval, activateTime);
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
        }

        public static BossTelegraphSnapshot ReadBossTelegraphSnapshot(this NetworkReader reader)
        {
            return new BossTelegraphSnapshot
            {
                Start               = reader.ReadBossTelegraphStart(),
                Direction           = reader.ReadVector3(),
                Length              = reader.ReadFloat(),
                IsDirectionLocked   = reader.ReadBool(),
                Phase               = (EBossTelegraphPhase)ReadEnumByte(reader, MAX_TELEGRAPH_PHASE),
                ExecuteServerTime   = reader.ReadDouble(),
                DashDistance        = reader.ReadFloat(),
                DashSeconds         = reader.ReadFloat(),
                DashSpeed           = reader.ReadFloat(),
                DashStoppedDistance = reader.ReadFloat(),
                DashStoppedByWall   = reader.ReadBool()
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
