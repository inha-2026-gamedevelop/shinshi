// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Data;

namespace Shinshi.AI
{
    // 무철 하드 2페이즈 P07 전장 공격의 표시다. 예고는 기존 부채꼴 예고(반경, 각도)로 그리고, 안전 방향은 임시 도형으로 따로 그린다
    // 공격 영역은 예고 시작 순간의 위치에서 방향 기준 arcAngle 부채꼴이고 나머지 뒤쪽 (360 - arcAngle)도가 안전지대다. 예고 시작 뒤에는 위치와 방향이 바뀌지 않는다
    // 입력은 서버의 IMucheolBattlefieldListener 인자에서 클라이언트 안전한 값만 옮긴 것이다. 타격은 서버가 알릴 때만 그리고, 확정이 없으면 예고 시간이 끝난 뒤 스스로 지운다
    // 밀림(OnPushed)은 맞은 대상이 실제로 밀려 움직이는 일이라 대상의 이동을 맡는 쪽(플레이어 이동 표현)이 다루고, 이 뷰는 보스 예고만 그린다
    // 늦게 접속하면 원래 시작 서버 시각으로 Begin을 부르고 타격 여부를 넘긴다
    /// <summary> 전장 공격의 부채꼴 예고, 안전 방향, 타격, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolBattlefieldView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN = 360f;

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public int                CastId;
            public Vector3            Origin;
            public Vector3            Direction;
            public float              Radius;
            public float              ArcAngle;
            public double             StartTime;
            public float              TelegraphSeconds;
            public bool               IsStruck;
            public double             StruckAt;
            public BossGroundShapeVFX Safe;
        }

        [SerializeField] private BossTelegraphPresenter _presenter;

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

        public void SetPresenter(BossTelegraphPresenter presenter)
        {
            _presenter = presenter;
        }

        /// <summary> 전장 공격 예고를 시작한다. 이미 있는 시전이거나 끝난 시전이면 무시한다. isStruck가 참이면 이미 타격된 시전이다 </summary>
        public bool Begin(int bossId, int castInstanceId, Vector3 origin, Vector3 direction, float radius, float arcAngle,
            double startServerTime, float telegraphSeconds, bool isStruck = false)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((radius <= 0f) || (arcAngle <= 0f) || (arcAngle >= FULL_TURN) || (_presenter == null))
            {
                return false;
            }

            direction.y = 0f;
            Entry entry = new Entry
            {
                CastId           = castInstanceId,
                Origin           = origin,
                Direction        = (direction.sqrMagnitude > (MIN_SPAN * MIN_SPAN)) ? direction.normalized : Vector3.forward,
                Radius           = radius,
                ArcAngle         = arcAngle,
                StartTime        = startServerTime,
                TelegraphSeconds = telegraphSeconds
            };

            double strikeTime = startServerTime + telegraphSeconds;
            double now        = ReadServerTime();

            // 타격 연출까지 지났거나 확정을 기다릴 시간까지 지난 시전은 그리지 않고 늦게 온 알림이 되살리지도 못한다
            if ((isStruck && (now >= (strikeTime + FLASH_SECONDS + FADE_SECONDS))) || ((!isStruck) && (now >= (strikeTime + CONFIRM_GRACE_SECONDS))))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            _presenter.enabled = true;
            BossTelegraphStart start = BossTelegraphStart.Fan(bossId, castInstanceId, BossPatternCatalogSO.MUCHEOL_HARD_P2_P07_ID, entry.Origin,
                entry.Direction, radius, arcAngle, startServerTime, telegraphSeconds);

            // 예고 표현이 받아들이지 않은 시전(이미 취소됐거나 끝난 번호)은 안전 방향만 남기지 않는다
            if (!_presenter.BeginTelegraph(in start))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            if (isStruck)
            {
                entry.IsStruck = true;
                entry.StruckAt = strikeTime;
                _presenter.Strike(castInstanceId, strikeTime);
            }

            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 타격 확정. 이미 타격한 시전이거나 모르는 시전이면 무시한다. 시각은 서버가 알린 시각이다 </summary>
        public bool Hit(int castInstanceId, double serverTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)) || entry.IsStruck)
            {
                return false;
            }

            entry.IsStruck = true;
            entry.StruckAt = serverTime;
            if (_presenter != null)
            {
                _presenter.Strike(castInstanceId, serverTime);
            }

            Wake();
            return true;
        }

        /// <summary> 중단, 예고와 안전 방향을 바로 지운다. 타격 전이면 피해가 나오지 않고 이미 타격했다면 연출을 마친다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            if (_presenter != null)
            {
                _presenter.Cancel(castInstanceId);
            }

            if (!TryFind(castInstanceId, out Entry entry))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            Remove(entry);
            return true;
        }

        /// <summary> 진행 중인 시전의 상태, 시험과 진단용이다. 안전 방향 도형이 없으면 safe는 null이다 </summary>
        public bool TryGetState(int castInstanceId, out bool isStruck, out BossGroundShapeVFX safe)
        {
            if (TryFind(castInstanceId, out Entry entry))
            {
                isStruck = entry.IsStruck;
                safe     = entry.Safe;
                return true;
            }

            isStruck = false;
            safe     = null;
            return false;
        }

        public override void ClearAll()
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                if (_presenter != null)
                {
                    _presenter.Cancel(_entries[i].CastId);
                }

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
                double strikeTime = entry.StartTime + entry.TelegraphSeconds;

                // 타격 확정이 오지 않으면 안전 방향만 남기지 않고 지운다
                if ((!entry.IsStruck) && (now >= (strikeTime + CONFIRM_GRACE_SECONDS)))
                {
                    Remove(entry);
                    continue;
                }

                float fade = 1f;
                if (entry.IsStruck)
                {
                    float age = Mathf.Max(0f, (float)(now - entry.StruckAt));
                    if (age >= (FLASH_SECONDS + FADE_SECONDS))
                    {
                        Remove(entry);
                        continue;
                    }

                    fade = (age < FLASH_SECONDS) ? 1f : (1f - Mathf.Clamp01((age - FLASH_SECONDS) / FADE_SECONDS));
                }

                if (entry.Safe == null)
                {
                    entry.Safe = ShapePool.Acquire();
                    if (entry.Safe == null)
                    {
                        continue;
                    }

                    // 안전지대는 공격 방향의 반대쪽 가운데에 놓이고 각도는 부채꼴이 남기는 나머지다
                    entry.Safe.ShowSector(entry.Origin, -entry.Direction, entry.Radius, FULL_TURN - entry.ArcAngle);
                }

                DrawCharging(entry.Safe, SafeColor, 1f, fade);
            }
        }

        private void Remove(Entry entry)
        {
            ReleaseShape(ref entry.Safe);
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
