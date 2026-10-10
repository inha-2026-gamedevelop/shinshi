// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버가 가진 위치만으로 계산한다. 클라이언트 물리 접촉 결과는 읽지 않으므로 같은 입력은 같은 결과를 낸다.
    // 움직이는 개체만 비켜 선다. 이미 서 있는 개체를 밀면 한 마리의 접근이 줄 전체를 흩어 놓고,
    // 예고 중인 개체의 기준 위치가 흔들린다. 비켜 서는 거리는 틱당 상한이 있어 겹쳐 스폰된 무리도 순간 이동 없이 퍼진다.
    // 누가 개체를 등록하고 해제하는지는 서버 부트스트랩의 몫이며 아직 연결하지 않았다
    /// <summary> 일반 적의 몸체 간격 계산, 수평 거리가 두 간격 반경의 합보다 가까워지지 않게 이동 결과를 보정한다 </summary>
    public sealed class EnemyCrowd
    {
        /****************************************
        *              Constants
        ****************************************/

        // 이동 속도와 별개로 이미 겹친 개체가 벌어지는 속도, 프로토타입 제안값이라 EnemyCatalogSO에 넣지 않는다
        public const float PROTOTYPE_DEPENETRATION_SPEED = 3f; // m/s

        private const int   INITIAL_CAPACITY        = 64;
        private const int   RESOLVE_PASSES          = 4;
        private const float MIN_DISTANCE            = 0.0001f; // m, 이보다 가까우면 방향을 정할 수 없다
        private const float PENETRATION_EPSILON     = 0.0005f; // m, 부동소수 오차 흡수
        private const float GOLDEN_ANGLE_RAD        = 2.3999632f;
        private const float BLOCKED_PROGRESS        = 0.05f;   // 가려던 거리 대비 이 비율보다 못 나아가면 막힌 틱으로 센다
        private const float FREE_PROGRESS           = 0.5f;    // 가려던 거리 대비 이 비율 이상 나아가면 확실히 풀린 것으로 본다
        private const float SIDESTEP_PROGRESS       = 0.5f;    // 옆걸음이 가려던 거리 대비 이 비율 이상 나아가야 받아들인다
        private const float SIDESTEP_FORWARD        = 0.5f;    // 옆걸음에 섞는 전진 성분, 옆으로 돌면서 조금씩 파고든다
        private const float SIDESTEP_DELAY_SECONDS  = 0.3f;    // 막힌 채 이만큼 지나야 옆걸음을 시작한다
        private const float SIDESTEP_COMMIT_SECONDS = 0.5f;    // 옆걸음을 시작하면 막힘이 풀려도 이만큼은 같은 쪽으로 이어 돈다
        private const float SIDESTEP_FLIP_SECONDS   = 0.6f;    // 선호하는 쪽이 이만큼 막혀 있어야 반대쪽을 시도한다
        private const int   SIDESTEP_MAX_EPISODES   = 2;       // 확실히 나아가지 못한 채 이만큼 돌았으면 포기하고 쉰다
        private const float SIDESTEP_HOLD_SECONDS   = 2f;      // 처음 포기한 뒤 제자리에서 쉬는 시간, 포기를 거듭할 때마다 두 배가 된다
        private const int   SIDESTEP_MAX_HOLD_SHIFT = 3;       // 쉬는 시간이 늘어나는 한도, 2초의 8배까지

        /****************************************
        *              Types
        ****************************************/

        /// <summary> 개체마다 두뇌가 들고 있는 옆걸음 기억 </summary>
        public struct SidestepState
        {
            public int   Side;           // 1 또는 -1, 0이면 1로 본다
            public float CommitSeconds;  // 남은 이어 돌기 시간
            public float HoldSeconds;    // 남은 제자리 휴식 시간
            public int   Episodes;       // 확실히 나아가기 전까지 마친 옆걸음 횟수
            public int   GiveUps;        // 확실히 나아가기 전까지 포기한 횟수
            public float BlockedSeconds; // 선호하는 쪽이 막힌 채 지난 시간
            public bool  Flipped;        // 이번에 막힌 사건에서 이미 반대쪽으로 바꿨는지
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<EnemyServer> _members = new List<EnemyServer>(INITIAL_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _members.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 간격 계산에 참여시킨다, 같은 개체를 두 번 넣지 않는 것은 호출자의 몫 </summary>
        public void Add(EnemyServer enemy)
        {
            if (enemy == null)
            {
                throw new ArgumentNullException(nameof(enemy));
            }

            _members.Add(enemy);
        }

        /// <summary> 간격 계산에서 뺀다, 없으면 false </summary>
        public bool Remove(EnemyServer enemy)
        {
            int index = _members.IndexOf(enemy);
            if (index < 0)
            {
                return false;
            }

            int last = _members.Count - 1;
            _members[index] = _members[last];
            _members.RemoveAt(last);
            return true;
        }

        // desired는 두뇌가 이번 틱에 가려던 위치다. 돌려주는 값은 그 위치에서 이웃 몸체 밖으로 밀어낸 위치이고 높이는 desired를 따른다.
        // 지금 위치에서 벌어지는 거리는 maxStep을 넘지 않는다. 같은 자리에 겹쳐 태어난 무리가 한 틱에 흩어지지 않고 퍼지게 하는 상한이다.
        // 보정한 위치의 총 겹침이 지금보다 늘어나면 제자리에 둔다. 좁은 틈에서 두 이웃이 서로 반대로 밀 때 더 깊이 끼어드는 쪽으로
        // 틱마다 튀는 것을 막는다
        /// <summary> self가 desired로 가려 할 때 이웃과 겹치지 않는 위치를 돌려준다, 위치를 직접 옮기지는 않는다 </summary>
        public Vector3 Resolve(EnemyServer self, Vector3 desired, float maxStep)
        {
            if (self == null)
            {
                throw new ArgumentNullException(nameof(self));
            }

            float selfRadius = self.SeparationRadius;
            if ((selfRadius <= 0f) || (!(maxStep > 0f)))
            {
                return desired;
            }

            Vector3 resolved = desired;
            for (int pass = 0; pass < RESOLVE_PASSES; ++pass)
            {
                if (!PushOutOnce(self, selfRadius, ref resolved))
                {
                    break;
                }
            }

            Vector3 current = self.Position;
            float   stepX   = resolved.x - current.x;
            float   stepZ   = resolved.z - current.z;
            float   stepSqr = (stepX * stepX) + (stepZ * stepZ);
            if (stepSqr > (maxStep * maxStep))
            {
                float scale = maxStep / Mathf.Sqrt(stepSqr);
                resolved.x = current.x + (stepX * scale);
                resolved.z = current.z + (stepZ * scale);
            }

            float before = TotalPenetration(self, selfRadius, current);
            float after  = TotalPenetration(self, selfRadius, resolved);
            if (after > (before + PENETRATION_EPSILON))
            {
                return new Vector3(current.x, desired.y, current.z);
            }

            return resolved;
        }

        // 정면이 이웃에 막혀 한동안 거의 못 나아가면 옆으로 돌아 들어가 본다. 앞줄 뒤에서 줄지어 선 개체가 아치처럼
        // 서로를 받쳐 영영 못 들어가는 교착을 푸는 장치다. 이 장치 자체가 떨림을 만들지 않게 지키는 것이 있다.
        // 느리게라도 파고드는 중이면 끼어들지 않는다. 옆 방향은 state에 기억해 같은 쪽으로 돌고, 시작하면 막힘이 풀려도 잠깐 이어 돈다.
        // 반대쪽 전환은 막힌 채 더 오래 지난 뒤에만 막힌 사건마다 최대 한 번이다. 확실히 나아가지 못한 채 몇 번 돌았으면
        // 포기하고 제자리에서 쉰다. 벗어났다가 같은 막힌 자리로 되돌아오는 순환이 남더라도 이렇게 느린 주기로 만든다.
        // allowSidestep이 거짓이면 이미 제자리에서 할 일이 있는 개체라 막혀도 더 파고들지 않고 선 채로 둔다
        /// <summary> Resolve와 같되 막히면 옆걸음을 시도한다, state는 호출자가 개체마다 들고 있는 옆걸음 기억 </summary>
        public Vector3 ResolveWithSidestep(EnemyServer self, Vector3 desired, float maxStep, float deltaSeconds,
            bool allowSidestep, ref SidestepState state)
        {
            Vector3 resolved = Resolve(self, desired, maxStep);

            Vector3 current        = self.Position;
            float   intendX        = desired.x - current.x;
            float   intendZ        = desired.z - current.z;
            float   intendedLength = Mathf.Sqrt((intendX * intendX) + (intendZ * intendZ));
            if (intendedLength <= MIN_DISTANCE)
            {
                return resolved;
            }

            // 겹쳐 있으면 쉬지 않고 먼저 벗어난다
            Vector3 stay = new Vector3(current.x, desired.y, current.z);
            if ((state.HoldSeconds > 0f) && (TotalPenetration(self, self.SeparationRadius, current) <= PENETRATION_EPSILON))
            {
                state.HoldSeconds -= deltaSeconds;
                return stay;
            }

            float forwardX = intendX / intendedLength;
            float forwardZ = intendZ / intendedLength;

            if ((state.CommitSeconds > 0f) && (allowSidestep))
            {
                state.CommitSeconds -= deltaSeconds;
                bool continues = TrySidestep(self, current, forwardX, forwardZ, intendedLength, desired.y, maxStep,
                    (state.Side < 0) ? -1 : 1, out Vector3 continued);

                if ((!continues) || (state.CommitSeconds <= 0f))
                {
                    state.CommitSeconds = 0f;
                    FinishEpisode(ref state);
                }

                if (continues)
                {
                    return continued;
                }

                if (state.HoldSeconds > 0f)
                {
                    return stay;
                }
            }

            float achieved = ((resolved.x - current.x) * forwardX) + ((resolved.z - current.z) * forwardZ);
            if (achieved >= (intendedLength * FREE_PROGRESS))
            {
                state.BlockedSeconds = 0f;
                state.Flipped        = false;
                state.Episodes       = 0;
                state.GiveUps        = 0;
                return resolved;
            }

            // 느리게나마 파고드는 중이면 막힌 것이 아니고 막힌 시간도 처음부터 센다
            if (achieved >= (intendedLength * BLOCKED_PROGRESS))
            {
                state.BlockedSeconds = 0f;
                return resolved;
            }

            state.BlockedSeconds += deltaSeconds;
            if ((!allowSidestep) || (state.BlockedSeconds < SIDESTEP_DELAY_SECONDS))
            {
                return resolved;
            }

            int preferred = (state.Side < 0) ? -1 : 1;
            if (TrySidestep(self, current, forwardX, forwardZ, intendedLength, desired.y, maxStep, preferred, out Vector3 moved))
            {
                state.Side          = preferred;
                state.CommitSeconds = SIDESTEP_COMMIT_SECONDS;
                return moved;
            }

            if ((state.Flipped) || (state.BlockedSeconds < SIDESTEP_FLIP_SECONDS))
            {
                return resolved;
            }

            if (TrySidestep(self, current, forwardX, forwardZ, intendedLength, desired.y, maxStep, -preferred, out moved))
            {
                state.Side          = -preferred;
                state.Flipped       = true;
                state.CommitSeconds = SIDESTEP_COMMIT_SECONDS;
                return moved;
            }

            return resolved;
        }

        private static void FinishEpisode(ref SidestepState state)
        {
            ++state.Episodes;
            if (state.Episodes >= SIDESTEP_MAX_EPISODES)
            {
                // 자리가 없는 개체가 무리 둘레를 영영 돌지 않게 포기할 때마다 쉬는 시간을 늘려 결국 가만히 서게 한다
                state.Episodes    = 0;
                state.HoldSeconds = SIDESTEP_HOLD_SECONDS * (1 << Mathf.Min(state.GiveUps, SIDESTEP_MAX_HOLD_SHIFT));
                ++state.GiveUps;
            }
        }

        private bool TrySidestep(EnemyServer self, Vector3 current, float forwardX, float forwardZ, float length, float height,
            float maxStep, int side, out Vector3 moved)
        {
            float sideX     = forwardZ * side;
            float sideZ     = -forwardX * side;
            var   candidate = new Vector3(
                current.x + ((sideX + (forwardX * SIDESTEP_FORWARD)) * length),
                height,
                current.z + ((sideZ + (forwardZ * SIDESTEP_FORWARD)) * length));

            moved = Resolve(self, candidate, maxStep);
            float along = ((moved.x - current.x) * sideX) + ((moved.z - current.z) * sideZ);
            return along >= (length * SIDESTEP_PROGRESS);
        }

        /// <summary> 살아 있는 두 개체 쌍 가운데 가장 깊이 겹친 길이, 겹침이 없으면 0 </summary>
        public float MeasureWorstOverlap()
        {
            float worst = 0f;
            for (int i = 0; i < _members.Count; ++i)
            {
                EnemyServer a = _members[i];
                if (!IsParticipant(a))
                {
                    continue;
                }

                for (int j = i + 1; j < _members.Count; ++j)
                {
                    EnemyServer b = _members[j];
                    if (!IsParticipant(b))
                    {
                        continue;
                    }

                    float overlap = (a.SeparationRadius + b.SeparationRadius) - HorizontalDistance(a.Position, b.Position);
                    worst = Mathf.Max(worst, overlap);
                }
            }

            return worst;
        }

        // 한 번의 통과에서 겹친 이웃마다 그 이웃 몸체 경계로 밀어낸다. 밀린 결과가 다른 이웃과 겹칠 수 있어 호출자가 몇 번 되풀이한다
        private bool PushOutOnce(EnemyServer self, float selfRadius, ref Vector3 position)
        {
            bool moved = false;

            for (int i = 0; i < _members.Count; ++i)
            {
                EnemyServer other = _members[i];
                if ((other == self) || (!IsParticipant(other)))
                {
                    continue;
                }

                Vector3 otherPosition = other.Position;
                float   minDistance   = selfRadius + other.SeparationRadius;
                float   offsetX       = position.x - otherPosition.x;
                float   offsetZ       = position.z - otherPosition.z;
                float   sqrDistance   = (offsetX * offsetX) + (offsetZ * offsetZ);

                if (sqrDistance >= (minDistance * minDistance))
                {
                    continue;
                }

                float directionX;
                float directionZ;
                float distance = Mathf.Sqrt(sqrDistance);
                if (distance > MIN_DISTANCE)
                {
                    directionX = offsetX / distance;
                    directionZ = offsetZ / distance;
                }
                else
                {
                    // 완전히 같은 자리면 전투 번호마다 다른 방향으로 갈라서, 같은 자리에 겹쳐 태어난 무리가 한 줄로 서지 않게 한다
                    float angle = self.CombatId * GOLDEN_ANGLE_RAD;
                    directionX = Mathf.Sin(angle);
                    directionZ = Mathf.Cos(angle);
                }

                position.x = otherPosition.x + (directionX * minDistance);
                position.z = otherPosition.z + (directionZ * minDistance);
                moved      = true;
            }

            return moved;
        }

        private float TotalPenetration(EnemyServer self, float selfRadius, Vector3 position)
        {
            float total = 0f;
            for (int i = 0; i < _members.Count; ++i)
            {
                EnemyServer other = _members[i];
                if ((other == self) || (!IsParticipant(other)))
                {
                    continue;
                }

                float depth = (selfRadius + other.SeparationRadius) - HorizontalDistance(position, other.Position);
                if (depth > 0f)
                {
                    total += depth;
                }
            }

            return total;
        }

        private static bool IsParticipant(EnemyServer enemy)
        {
            return (enemy.LifeState == EParticipantLifeState.Alive) && (enemy.SeparationRadius > 0f);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }
    }
}
