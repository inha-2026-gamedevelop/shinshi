// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 무철 1페이즈 P06 충돌 돌진의 결과 표시다. 돌진 자체(예고, 이동, 정지)는 기존 돌진 예고가 그리고, 이 뷰는 돌진이 구조물이나 벽에 막혀 끝났을 때
    // 멈춘 자리의 충격과 회복 시간 동안의 빈틈을 임시 도형으로 보인다. 회복 시간은 실행이 끝난 경계부터 재는 값이라 서버가 알린 시각부터 센다
    // 입력은 서버의 IBossChargeOutcomeListener 인자에서 클라이언트 안전한 값만 옮긴 것이다. 충돌 종류와 구조물 식별자의 최종 전송 형식은 Net 계약이 정한다
    // 이 뷰는 충돌을 판정하거나 회복 시간을 정하지 않는다. 충돌 없이 정상 종료했으면 아무것도 그리지 않는다
    // 늦게 접속하면 원래 서버 시각으로 ChargeEnded를 부른다. 복제 계층이 충돌 위치와 시각과 회복 시간을 들고 있어야 한다
    /// <summary> 충돌 돌진의 충돌 지점 충격과 회복 구간 표시를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolChargeImpactView : BossHardTelegraphViewBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public int                CastId;
            public int                StructureId;
            public Vector3            Position;
            public float              TravelDistance;
            public double             EventTime;
            public float              RecoverySeconds;
            public BossGroundShapeVFX Impact;
            public BossGroundShapeVFX Recovery;
        }

        [Header("크기")]
        [SerializeField, Min(0.1f)] private float _impactRadius   = 3f;   // m, 충돌 순간 충격 원
        [SerializeField, Min(0.1f)] private float _recoveryRadius = 1.8f; // m, 회복 구간 표시 원

        private readonly List<Entry>        _entries = new List<Entry>();
        private readonly BossEndedIdHistory _ended   = new BossEndedIdHistory(ENDED_HISTORY);

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _entries.Count;

        protected override bool HasEntries => _entries.Count > 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 돌진 결과를 받는다. 충돌이 아니면 그릴 것이 없고, 이미 받은 시전이거나 모두 지난 시각이면 무시한다 </summary>
        public bool ChargeEnded(int castInstanceId, bool isCollision, Vector3 position, float travelDistance, int structureId,
            float recoverySeconds, double serverTime)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            _ended.Remember(castInstanceId);
            double now = ReadServerTime();
            float  span = Mathf.Max(FLASH_SECONDS, recoverySeconds) + FADE_SECONDS;
            if ((!isCollision) || (now >= (serverTime + span)))
            {
                return false;
            }

            _entries.Add(new Entry
            {
                CastId          = castInstanceId,
                StructureId     = structureId,
                Position        = position,
                TravelDistance  = travelDistance,
                EventTime       = serverTime,
                RecoverySeconds = Mathf.Max(0f, recoverySeconds)
            });
            Wake();
            return true;
        }

        /// <summary> 중단이나 사망으로 충돌 표시를 바로 지운다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            if (!TryFind(castInstanceId, out Entry entry))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            Remove(entry);
            return true;
        }

        /// <summary> 진행 중인 충돌 표시의 상태, 시험과 진단용이다 </summary>
        public bool TryGetState(int castInstanceId, out Vector3 position, out BossGroundShapeVFX impact, out BossGroundShapeVFX recovery)
        {
            if (TryFind(castInstanceId, out Entry entry))
            {
                position = entry.Position;
                impact   = entry.Impact;
                recovery = entry.Recovery;
                return true;
            }

            position = default;
            impact   = null;
            recovery = null;
            return false;
        }

        public override void ClearAll()
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                Remove(_entries[i]);
            }
        }

        public override void ResetNetworkSession()
        {
            ClearAll();
            _ended.Clear();
        }

        protected override void Tick(double now)
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                Entry entry = _entries[i];
                float age   = Mathf.Max(0f, (float)(now - entry.EventTime));
                float span  = Mathf.Max(FLASH_SECONDS, entry.RecoverySeconds);
                if (age >= (span + FADE_SECONDS))
                {
                    Remove(entry);
                    continue;
                }

                TickImpact(entry, age);
                TickRecovery(entry, age);
            }
        }

        // 충돌 순간만 번쩍이는 충격 원
        private void TickImpact(Entry entry, float age)
        {
            if (age >= FLASH_SECONDS)
            {
                ReleaseShape(ref entry.Impact);
                return;
            }

            if (entry.Impact == null)
            {
                entry.Impact = ShapePool.Acquire();
                if (entry.Impact == null)
                {
                    return;
                }

                entry.Impact.ShowDisc(entry.Position, _impactRadius);
            }

            DrawFlash(entry.Impact, age, FLASH_SECONDS);
        }

        // 회복 시간 동안 남는 빈틈 표시, 시간이 차오르고 끝나면 옅어진다
        private void TickRecovery(Entry entry, float age)
        {
            if (entry.RecoverySeconds <= 0f)
            {
                return;
            }

            if (entry.Recovery == null)
            {
                entry.Recovery = ShapePool.Acquire();
                if (entry.Recovery == null)
                {
                    return;
                }

                entry.Recovery.ShowDisc(entry.Position, _recoveryRadius);
            }

            float fade = (age <= entry.RecoverySeconds) ? 1f : (1f - Mathf.Clamp01((age - entry.RecoverySeconds) / FADE_SECONDS));
            DrawCharging(entry.Recovery, WarnColor, Mathf.Clamp01(age / entry.RecoverySeconds), fade);
        }

        private void Remove(Entry entry)
        {
            ReleaseShape(ref entry.Impact);
            ReleaseShape(ref entry.Recovery);
            _entries.Remove(entry);
            _ended.Remember(entry.CastId);
        }

        private bool TryFind(int castId, out Entry entry)
        {
            for (int i = 0; i < _entries.Count; ++i)
            {
                if (_entries[i].CastId == castId)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsKnown(int castId)
        {
            return _ended.Contains(castId) || TryFind(castId, out Entry _);
        }
    }
}
