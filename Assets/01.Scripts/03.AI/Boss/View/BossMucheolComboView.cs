// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    // 무철 1페이즈 P05 콤보의 표시다. 타마다 부채꼴 예고 하나씩이고 모든 타가 같은 방향을 쓴다. 타 i는 예고 시간 + 그 타의 지연이 지난 시각에 확정된다
    // 입력은 서버의 IBossComboTelegraphListener 인자에서 클라이언트 안전한 값만 옮긴 것이다. 타 목록은 사거리, 부채꼴 각도, 지연의 같은 길이 목록으로 받는다
    // 타격은 서버가 알린 타만 터뜨리고, 확정이 없으면 예고 표현의 기다림 시간이 지난 뒤 스스로 지운다
    // 늦게 접속하면 원래 시작 서버 시각으로 Begin을 부르고 이미 확정된 타 수를 넘긴다. 이 수는 복제 계층이 타격 알림으로 세어 들고 있어야 한다
    /// <summary> 콤보의 타별 부채꼴 예고, 조준 추적, 방향 고정, 타 확정, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolComboView : BossPresenterViewBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private sealed class Cast
        {
            public float[]  Ranges;
            public double[] StrikeTimes;
            public int      HitCount;
            public int      StruckCount;
        }

        private readonly Dictionary<int, Cast> _casts = new Dictionary<int, Cast>();

        /****************************************
        *              Properties
        ****************************************/

        public int CastCount => _casts.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 콤보 예고를 시작한다. 이미 있는 시전이거나 취소된 시전이면 무시한다. struckCount는 이미 확정된 타 수다 </summary>
        public bool Begin(int bossId, int castInstanceId, Vector3 origin, Vector3 direction, double startServerTime, float telegraphSeconds,
            IReadOnlyList<float> ranges, IReadOnlyList<float> arcAngles, IReadOnlyList<float> delaySeconds, int struckCount = 0)
        {
            if (IsServerBuild || _casts.ContainsKey(castInstanceId))
            {
                return false;
            }

            if ((ranges == null) || (arcAngles == null) || (delaySeconds == null) || (ranges.Count < 1)
                || (ranges.Count != arcAngles.Count) || (ranges.Count != delaySeconds.Count))
            {
                return false;
            }

            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter == null)
            {
                return false;
            }

            Cast cast = new Cast
            {
                Ranges      = new float[ranges.Count],
                StrikeTimes = new double[ranges.Count],
                HitCount    = ranges.Count
            };

            bool any = false;
            for (int i = 0; i < ranges.Count; ++i)
            {
                float seconds = telegraphSeconds + delaySeconds[i];
                cast.Ranges[i]      = ranges[i];
                cast.StrikeTimes[i] = startServerTime + seconds;

                BossTelegraphStart start = BossTelegraphStart.Fan(bossId, Keys.Get(castInstanceId, i), BossPatternCatalogSO.MUCHEOL_P05_ID,
                    origin, direction, ranges[i], arcAngles[i], startServerTime, seconds, castInstanceId);
                any |= presenter.BeginTelegraph(in start);
            }

            // 취소됐거나 모두 지난 시전이라 예고 하나도 받아들이지 않았으면 기록을 남기지 않는다
            if (!any)
            {
                ForgetCast(castInstanceId);
                return false;
            }

            _casts.Add(castInstanceId, cast);
            TrackGroup(castInstanceId);

            int confirmed = Mathf.Clamp(struckCount, 0, ranges.Count);
            for (int i = 0; i < confirmed; ++i)
            {
                Hit(castInstanceId, i, cast.StrikeTimes[i]);
            }

            return any;
        }

        /// <summary> 추적 중 조준 방향이 바뀌었다, 모든 타에 같은 방향을 준다 </summary>
        public bool AimChanged(int castInstanceId, Vector3 direction, double serverTime)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < cast.HitCount; ++i)
            {
                if (Keys.TryGet(castInstanceId, i, out int key))
                {
                    any |= Presenter.UpdateAim(key, direction, cast.Ranges[i], serverTime);
                }
            }

            return any;
        }

        /// <summary> 방향이 고정됐다 </summary>
        public bool DirectionLocked(int castInstanceId, Vector3 direction)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < cast.HitCount; ++i)
            {
                if (Keys.TryGet(castInstanceId, i, out int key))
                {
                    any |= Presenter.LockDirection(key, direction, cast.Ranges[i]);
                }
            }

            return any;
        }

        /// <summary> hitIndex번째 타가 확정됐다. 이미 확정된 타나 모르는 타면 무시한다 </summary>
        public bool Hit(int castInstanceId, int hitIndex, double serverTime)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            if ((hitIndex < 0) || (hitIndex >= cast.HitCount) || (!Keys.TryGet(castInstanceId, hitIndex, out int key)))
            {
                return false;
            }

            bool struck = Presenter.Strike(key, serverTime);
            if (struck)
            {
                ++cast.StruckCount;
            }

            // 마지막 타까지 확정되면 이 시전의 기록은 더 필요 없다, 남은 연출은 예고 표현이 마친다
            if (cast.StruckCount >= cast.HitCount)
            {
                _casts.Remove(castInstanceId);
                ForgetCast(castInstanceId);
            }

            return struck;
        }

        /// <summary> 중단, 아직 확정되지 않은 타의 예고를 모두 지운다. 이미 확정된 타는 연출을 마친다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            _casts.Remove(castInstanceId);
            return CancelCast(castInstanceId);
        }

        /// <summary> 한 타의 예고 표현 번호, 시험과 진단용이다 </summary>
        public bool TryGetKey(int castInstanceId, int hitIndex, out int key)
        {
            return Keys.TryGet(castInstanceId, hitIndex, out key);
        }

        public override void ClearAll()
        {
            _casts.Clear();
            base.ClearAll();
        }
    }
}
