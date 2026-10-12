// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P04, 대상 추적 장판의 활성 전 표시다. 입력은 서버의 IBossHardAreaListener 인자와 같은 값이다
    // 서버가 알린 중심으로만 움직이고 대상을 직접 따라가지 않는다. 서버 갱신 사이만 부드럽게 잇는다. 중심 고정이 알려지면 그 자리에 바로 멈춘다
    // 활성 뒤의 장판은 기존 장판 표현(OnAreaActivated의 AreaId)이 그린다. 이 뷰는 같은 AreaId의 활성 사실을 Activated로 받아 이동 예고를 지운다
    // 활성이 끝내 오지 않아도 고정 시간이 끝난 뒤 일정 시간 지나면 스스로 지운다
    // 늦게 접속하면 서버의 가장 최근 중심으로 BeginMoving을 부르고 이미 고정됐다면 FixCenter를 이어서 부른다. 복제 계층이 최근 중심과 고정 여부를 들고 있어야 한다
    /// <summary> 대상 추적 장판의 이동 예고, 중심 고정, 취소를 장판 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossTrackingAreaView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FOLLOW_SMOOTH          = 14f;  // 1/초, 서버 갱신 사이를 부드럽게 이을 때만 쓴다
        private const float ACTIVATION_WAIT_SECONDS = 1f;  // 고정 시간이 끝난 뒤 활성 알림을 더 기다리는 시간

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public int                 AreaId;
            public int                 CastId;
            public int                 TargetCombatId;
            public float               Radius;
            public double              StartTime;
            public float               FollowSeconds;
            public float               FixedSeconds;
            public Vector3             ServerCenter;
            public Vector3             DisplayCenter;
            public double              LastUpdateTime;
            public bool                IsFixed;
            public BossGroundShapeVFX  Shape;
        }

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

        /// <summary> 이동 예고를 시작한다. 이미 있는 장판 번호나 끝난 번호면 무시한다. 늦게 받았으면 흐른 만큼 건너뛰어 그린다 </summary>
        public bool BeginMoving(int castInstanceId, int areaId, int targetCombatId, Vector3 center, float radius,
            double startServerTime, float followSeconds, float fixedSeconds)
        {
            if (IsServerBuild || IsKnown(areaId))
            {
                return false;
            }

            if (radius <= 0f)
            {
                return false;
            }

            // 고정 시간까지 모두 지나 활성을 기다릴 시간도 끝난 예고는 그리지 않고 늦게 온 알림이 되살리지도 못한다
            if (ReadServerTime() >= (startServerTime + followSeconds + fixedSeconds + ACTIVATION_WAIT_SECONDS))
            {
                _ended.Remember(areaId);
                return false;
            }

            Entry entry = new Entry
            {
                AreaId         = areaId,
                CastId         = castInstanceId,
                TargetCombatId = targetCombatId,
                Radius         = radius,
                StartTime      = startServerTime,
                FollowSeconds  = followSeconds,
                FixedSeconds   = fixedSeconds,
                ServerCenter   = center,
                DisplayCenter  = center,
                LastUpdateTime = startServerTime
            };

            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 서버가 알린 새 중심. 고정된 뒤나 이미 반영한 시각보다 오래된 갱신이면 무시한다 </summary>
        public bool UpdateMoving(int areaId, Vector3 center, double serverTime)
        {
            if ((IsServerBuild) || (!TryFind(areaId, out Entry entry)) || entry.IsFixed || (serverTime < entry.LastUpdateTime))
            {
                return false;
            }

            entry.ServerCenter   = center;
            entry.LastUpdateTime = serverTime;
            return true;
        }

        /// <summary> 중심 고정. 표시는 서버가 알린 자리로 바로 옮겨 멈춘다 </summary>
        public bool FixCenter(int areaId, Vector3 center, double fixedServerTime)
        {
            if ((IsServerBuild) || (!TryFind(areaId, out Entry entry)) || entry.IsFixed)
            {
                return false;
            }

            entry.IsFixed        = true;
            entry.ServerCenter   = center;
            entry.DisplayCenter  = center;
            entry.LastUpdateTime = fixedServerTime;
            Wake();
            return true;
        }

        /// <summary> 활성 전 취소, 이동 예고를 바로 지운다. 모르는 번호면 나중에 늦게 오는 시작이 되살아나지 않게 기억해 둔다 </summary>
        public bool Cancel(int areaId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            if (!TryFind(areaId, out Entry entry))
            {
                _ended.Remember(areaId);
                return false;
            }

            Remove(entry);
            return true;
        }

        /// <summary> 같은 번호의 장판이 활성됐다, 이동 예고를 지우고 기존 장판 표현에 넘긴다. 모르는 번호면 늦게 오는 이동 예고가 되살아나지 않게 기억해 둔다 </summary>
        public bool Activated(int areaId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            if (!TryFind(areaId, out Entry entry))
            {
                _ended.Remember(areaId);
                return false;
            }

            Remove(entry);
            return true;
        }

        /// <summary> 진행 중인 이동 예고의 상태, 시험과 진단용이다 </summary>
        public bool TryGetState(int areaId, out Vector3 displayCenter, out bool isFixed, out int targetCombatId,
            out BossGroundShapeVFX shape)
        {
            if (TryFind(areaId, out Entry entry))
            {
                displayCenter  = entry.DisplayCenter;
                isFixed        = entry.IsFixed;
                targetCombatId = entry.TargetCombatId;
                shape          = entry.Shape;
                return true;
            }

            displayCenter  = default;
            isFixed        = false;
            targetCombatId = 0;
            shape          = null;
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
                double total = entry.StartTime + entry.FollowSeconds + entry.FixedSeconds;

                // 활성 알림이 오지 않으면 이동 예고만 남기지 않고 지운다
                if (now >= (total + ACTIVATION_WAIT_SECONDS))
                {
                    Remove(entry);
                    continue;
                }

                if (!entry.IsFixed)
                {
                    float blend = 1f - Mathf.Exp(-FOLLOW_SMOOTH * Time.deltaTime);
                    entry.DisplayCenter = Vector3.Lerp(entry.DisplayCenter, entry.ServerCenter, blend);
                }

                if (entry.Shape == null)
                {
                    entry.Shape = ShapePool.Acquire();
                    if (entry.Shape == null)
                    {
                        continue;
                    }

                    entry.Shape.ShowDisc(entry.DisplayCenter, entry.Radius);
                }
                else
                {
                    entry.Shape.MoveTo(entry.DisplayCenter);
                }

                // 따라가는 동안은 경고색, 고정된 뒤에는 위험색으로 바꿔 더 피할 수 없음을 알린다
                float charge = Ratio(now, entry.StartTime, total);
                DrawCharging(entry.Shape, entry.IsFixed ? DangerColor : WarnColor, charge, 1f);
            }
        }

        private void Remove(Entry entry)
        {
            ReleaseShape(ref entry.Shape);
            _entries.Remove(entry);
            _ended.Remember(entry.AreaId);
        }

        private bool TryFind(int areaId, out Entry entry)
        {
            for (int i = 0; i < _entries.Count; ++i)
            {
                if (_entries[i].AreaId == areaId)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsKnown(int areaId)
        {
            return _ended.Contains(areaId) || TryFind(areaId, out Entry _);
        }
    }
}
