// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P06, 동시 원형 폭발의 표시다. 입력은 서버의 IBossCircleBurstListener 인자와 같은 값이다
    // 원 여러 개(서버 배치는 13개)가 시작 알림 하나로 함께 오므로 모든 원의 예고를 같은 순간에 시작하고, 같은 서버 시각에 한꺼번에 폭발한다. 폭발 뒤에 남는 위험은 없다
    // 원의 중심과 반경은 서버가 확정한 배치 그대로 받는다. 클라이언트가 사분면 규칙으로 다시 계산하지 않는다
    // 폭발은 서버 확정이 있어야만 그리고, 확정이 없으면 스스로 터뜨리지 않고 예고 시간이 끝난 뒤 일정 시간 지나면 지운다
    // 늦게 접속하면 Begin에 폭발 여부를 넘긴다. 이 값은 복제 계층이 폭발 알림으로 들고 있어야 한다
    /// <summary> 동시 원형 폭발의 예고, 폭발, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossCircleBurstView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int MAX_CIRCLES = 16;

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public int                  CastId;
            public Vector3[]            Centers;
            public float[]              Radii;
            public double               CastStart;
            public float                StrikeSeconds;
            public bool                 IsStruck;
            public double               StruckAt;
            public BossGroundShapeVFX[] Shapes;
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

        /// <summary> 시전을 시작한다. 이미 있는 시전 번호나 끝난 번호면 무시한다. centers와 radii는 같은 길이이고 복사해 둔다. isStruck가 참이면 이미 폭발한 시전이다 </summary>
        public bool Begin(int castInstanceId, IReadOnlyList<Vector3> centers, IReadOnlyList<float> radii, double castStartServerTime,
            float strikeSeconds, bool isStruck = false)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((centers == null) || (radii == null) || (centers.Count < 1) || (centers.Count != radii.Count) || (centers.Count > MAX_CIRCLES))
            {
                return false;
            }

            Entry entry = new Entry
            {
                CastId        = castInstanceId,
                Centers       = new Vector3[centers.Count],
                Radii         = new float[centers.Count],
                CastStart     = castStartServerTime,
                StrikeSeconds = strikeSeconds,
                Shapes        = new BossGroundShapeVFX[centers.Count]
            };

            for (int i = 0; i < centers.Count; ++i)
            {
                entry.Centers[i] = centers[i];
                entry.Radii[i]   = radii[i];
            }

            double strike = castStartServerTime + strikeSeconds;
            double now    = ReadServerTime();

            // 폭발 연출까지 지났거나 확정을 기다릴 시간까지 지난 시전은 그리지 않고 늦게 온 알림이 되살리지도 못한다
            if ((isStruck && (now >= (strike + FLASH_SECONDS))) || ((!isStruck) && (now >= (strike + CONFIRM_GRACE_SECONDS))))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            entry.IsStruck = isStruck;
            entry.StruckAt = strike;
            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 원 전체의 폭발 확정. 이미 폭발했거나 모르는 시전이면 무시한다. 시각은 서버가 알린 예정 시각이다 </summary>
        public bool Struck(int castInstanceId, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)) || entry.IsStruck)
            {
                return false;
            }

            entry.IsStruck = true;
            entry.StruckAt = scheduledServerTime;
            Wake();
            return true;
        }

        /// <summary> 중단, 표시는 바로 지운다. 모르는 시전이면 나중에 늦게 오는 시작이 되살아나지 않게 기억해 둔다 </summary>
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

        /// <summary> 진행 중인 시전의 상태, 시험과 진단용이다 </summary>
        public bool TryGetState(int castInstanceId, out bool isStruck, out int shownCount)
        {
            if (TryFind(castInstanceId, out Entry entry))
            {
                isStruck = entry.IsStruck;
                shownCount = 0;
                for (int i = 0; i < entry.Shapes.Length; ++i)
                {
                    shownCount += (entry.Shapes[i] != null) ? 1 : 0;
                }

                return true;
            }

            isStruck   = false;
            shownCount = 0;
            return false;
        }

        /// <summary> 원 하나의 도형, 시험과 진단용이다. 없으면 null </summary>
        public BossGroundShapeVFX GetCircle(int castInstanceId, int index)
        {
            return (TryFind(castInstanceId, out Entry entry) && (index >= 0) && (index < entry.Shapes.Length)) ? entry.Shapes[index] : null;
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
                Entry  entry  = _entries[i];
                double strike = entry.CastStart + entry.StrikeSeconds;

                if (entry.IsStruck)
                {
                    float age = Mathf.Max(0f, (float)(now - entry.StruckAt));
                    if (age >= FLASH_SECONDS)
                    {
                        Remove(entry);
                        continue;
                    }

                    for (int k = 0; k < entry.Shapes.Length; ++k)
                    {
                        if (EnsureCircle(entry, k))
                        {
                            DrawFlash(entry.Shapes[k], age, FLASH_SECONDS);
                        }
                    }

                    continue;
                }

                // 폭발 확정이 오지 않으면 예고만 남기지 않고 지운다
                if (now >= (strike + CONFIRM_GRACE_SECONDS))
                {
                    Remove(entry);
                    continue;
                }

                float charge = Ratio(now, entry.CastStart, strike);
                for (int k = 0; k < entry.Shapes.Length; ++k)
                {
                    if (EnsureCircle(entry, k))
                    {
                        DrawCharging(entry.Shapes[k], charge, 1f);
                    }
                }
            }
        }

        // 도형이 없는 원은 빌려 중심에 놓는다, 풀이 모자라면 그 원은 건너뛴다
        private bool EnsureCircle(Entry entry, int index)
        {
            if (entry.Shapes[index] != null)
            {
                return true;
            }

            BossGroundShapeVFX shape = ShapePool.Acquire();
            if (shape == null)
            {
                return false;
            }

            shape.ShowDisc(entry.Centers[index], entry.Radii[index]);
            entry.Shapes[index] = shape;
            return true;
        }

        private void Remove(Entry entry)
        {
            ReleaseShapes(entry.Shapes);
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
