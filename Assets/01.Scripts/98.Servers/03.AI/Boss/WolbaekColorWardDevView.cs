// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 도형 표시다. 중앙 결계와 색 결계는 납작한 원판, 광역 공격은 전장 크기의 납작한 원판, 같은 색 연결 띠는
    // 선으로 그린다. 최종 아트가 아니고 문양 그림도 확정하지 않았다. 서버 알림(IWolbaekColorWardListener)을 그대로 받아 그리며
    // 판정이나 상태는 만들지 않는다. 연결 띠의 거리 경고는 알림이 아니라 매 프레임 참가자 위치를 읽어 표시만 한다.
    // 운영 클라이언트의 표시는 복제 계층이 연결된 뒤 다른 뷰가 맡는다
    /// <summary> 색 결계 기믹의 임시 도형 표시, 시험 장면에서만 쓴다 </summary>
    public sealed class WolbaekColorWardDevView : IWolbaekColorWardListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  DISC_THICKNESS   = 0.05f;
        private const float  DISC_HEIGHT      = 0.03f;
        private const float  STRIKE_HEIGHT    = 0.02f;
        private const float  BAND_HEIGHT      = 0.2f;
        private const float  BAND_WIDTH       = 0.15f;
        private const float  STRIKE_FLASH     = 0.3f;
        private const int    LOG_LENGTH       = 12;
        private const int    COLOR_COUNT      = 2;
        private const int    PAIR_SIZE        = 2;
        private const float  DIAMETER_FACTOR  = 2f;

        private static readonly Color GatherColor    = new Color(0.6f, 0.9f, 1f);
        private static readonly Color TelegraphColor = new Color(1f, 0.55f, 0f);
        private static readonly Color StrikeColor    = Color.red;
        private static readonly Color BandNormal     = Color.white;
        private static readonly Color BandWarning    = Color.red;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 참가자의 지금 위치를 읽는다, 쓰러졌거나 없으면 거짓이다 </summary>
        public delegate bool PositionReader(int combatId, out Vector3 position);

        private readonly Transform      _parent;
        private readonly Func<double>   _clock;
        private readonly PositionReader _positions;
        private readonly float          _arenaRadius;
        private readonly Vector3        _arenaCenter;
        private readonly List<string>   _log      = new List<string>();
        private readonly GameObject[]   _wardDiscs = new GameObject[COLOR_COUNT];
        private readonly LineRenderer[] _bands     = new LineRenderer[COLOR_COUNT];
        private readonly int[]          _bandA     = new int[COLOR_COUNT];
        private readonly int[]          _bandB     = new int[COLOR_COUNT];
        private GameObject              _gatherDisc;
        private GameObject              _strikeDisc;
        private Material                _material;
        private double                  _hideAt;
        private bool                    _isFlashing;
        private WolbaekWardStart        _start;
        private WolbaekWardStage        _stage;
        private bool                    _hasStart;
        private bool                    _hasStage;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 최근 알림 기록, 오래된 것이 앞이다 </summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary> 지금 보이는 지점, 표시 중이 아니면 거짓이다 </summary>
        public bool TryGetStage(out WolbaekWardStage stage)
        {
            stage = _stage;
            return _hasStage;
        }

        /// <summary> 지금 보이는 시작 값, 표시 중이 아니면 거짓이다 </summary>
        public bool TryGetStart(out WolbaekWardStart start)
        {
            start = _start;
            return _hasStart;
        }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekColorWardDevView(Transform parent, Func<double> clock, PositionReader positions, Vector3 arenaCenter,
            float arenaRadius)
        {
            _parent      = parent;
            _clock       = clock;
            _positions   = positions;
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _bandA[0] = _bandB[0] = _bandA[1] = _bandB[1] = -1;
        }

        /// <summary> 플래시 종료와 연결 띠 위치를 갱신한다, 매 프레임 불러야 한다 </summary>
        public void Update()
        {
            if ((_isFlashing) && (_clock() >= _hideAt))
            {
                HideStrike();
            }

            UpdateBands();
        }

        /// <summary> 도형과 기록을 모두 치운다 </summary>
        public void ClearAll()
        {
            ClearDisplay();
            _log.Clear();
        }

        public void OnWardStarted(in WolbaekWardStart start)
        {
            ClearDisplay();
            _start    = start;
            _hasStart = true;

            _gatherDisc = CreateDisc("WardGather", start.GatherCenter, start.GatherRadius, GatherColor);
            BuildBands(start);
            Add("진입, 필수 " + start.Members.Count + "명");
        }

        public void OnGatherHoldStarted(int castInstanceId, double serverTime)
        {
            Add("전원 중앙 도착, 연속 집결 시작");
        }

        public void OnGatherHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            Add("집결 초기화 - " + reason + " (참가자 " + combatId + ")");
        }

        public void OnGatherCompleted(int castInstanceId, double serverTime)
        {
            DestroyObject(ref _gatherDisc);
            Add("집결 완료, 결계가 두 색으로 갈라진다");
        }

        public void OnStageStarted(int castInstanceId, in WolbaekWardStage stage)
        {
            _stage    = stage;
            _hasStage = true;
            DestroyWardDiscs();

            _wardDiscs[(int)EWolbaekWardColor.Red]  = CreateDisc("WardRed", stage.RedCenter, stage.Radius, Color.red);
            _wardDiscs[(int)EWolbaekWardColor.Blue] = CreateDisc("WardBlue", stage.BlueCenter, stage.Radius, Color.blue);
            Add("지점 " + stage.Index + " 시작, 탐색 " + (stage.FirstStrikeServerTime - stage.StartServerTime) + "초");
        }

        public void OnStrikeTelegraphed(int castInstanceId, in WolbaekWardStrike strike)
        {
            ShowStrike(TelegraphColor);
            Add("광역 공격 " + strike.StrikeIndex + " 예고");
        }

        public void OnStrikeResolved(int castInstanceId, int stageIndex, int strikeIndex, double strikeServerTime, int hitCount,
            int protectedCount)
        {
            ShowStrike(StrikeColor);
            _isFlashing = true;
            _hideAt     = _clock() + STRIKE_FLASH;
            Add("광역 공격 " + stageIndex + "/" + strikeIndex + " 판정, 적중 " + hitCount + " 방어 " + protectedCount);
        }

        public void OnLinkDamaged(int castInstanceId, double serverTime, int combatIdA, int combatIdB, float distance, float rate)
        {
            Add("연결 거리 피해 " + distance.ToString("F1") + "m " + (rate * 100f).ToString("F0") + "%");
        }

        public void OnGraceStarted(int castInstanceId, int combatId, double graceEndServerTime)
        {
            Add("합류 유예 시작 - 참가자 " + combatId);
        }

        public void OnGraceEnded(int castInstanceId, double serverTime, int combatId, EWolbaekGraceEnd reason)
        {
            Add("합류 유예 끝 - 참가자 " + combatId + " " + reason);
        }

        public void OnReviveWindowOpened(int castInstanceId, double serverTime)
        {
            Add("부활과 재접속 복귀 가능");
        }

        public void OnWardSucceeded(int castInstanceId, double serverTime)
        {
            ClearDisplay();
            Add("성공, 무적 해제");
        }

        public void OnWardFailed(int castInstanceId, double serverTime, EWolbaekWardFail reason)
        {
            ClearDisplay();
            Add("실패 - " + reason);
        }

        public void OnWardHalted(int castInstanceId, double serverTime, EWolbaekWardHaltReason reason)
        {
            ClearDisplay();
            Add("정지 - " + reason);
        }

        public void OnWardCleared(int castInstanceId, EBossInterruptReason reason)
        {
            ClearDisplay();
            Add("정리 - " + reason);
        }

        private void Add(string message)
        {
            _log.Add(message);
            if (_log.Count > LOG_LENGTH)
            {
                _log.RemoveAt(0);
            }
        }

        private GameObject CreateDisc(string name, Vector3 center, float radius, Color color)
        {
            GameObject disc = TestRigDisplay.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            UnityEngine.Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_parent, false);
            disc.transform.position   = center + (Vector3.up * DISC_HEIGHT);
            disc.transform.localScale = new Vector3(radius * DIAMETER_FACTOR, DISC_THICKNESS, radius * DIAMETER_FACTOR);
            disc.GetComponent<Renderer>().material.color = color;
            return disc;
        }

        private void ShowStrike(Color color)
        {
            if (_strikeDisc == null)
            {
                _strikeDisc = CreateDisc("WardStrike", _arenaCenter, _arenaRadius, color);
                _strikeDisc.transform.position = _arenaCenter + (Vector3.up * STRIKE_HEIGHT);
            }

            _strikeDisc.GetComponent<Renderer>().material.color = color;
            _strikeDisc.SetActive(true);
        }

        private void HideStrike()
        {
            if (_strikeDisc != null)
            {
                _strikeDisc.SetActive(false);
            }

            _isFlashing = false;
        }

        // 같은 색이 정확히 두 명일 때만 띠를 만든다
        private void BuildBands(in WolbaekWardStart start)
        {
            for (int color = 0; color < COLOR_COUNT; ++color)
            {
                int first  = -1;
                int second = -1;
                int total  = 0;
                for (int i = 0; i < start.Members.Count; ++i)
                {
                    if ((int)start.Members[i].Color != color)
                    {
                        continue;
                    }

                    ++total;
                    if (first < 0)
                    {
                        first = start.Members[i].CombatId;
                    }
                    else
                    {
                        second = start.Members[i].CombatId;
                    }
                }

                if (total != PAIR_SIZE)
                {
                    continue;
                }

                _bandA[color] = first;
                _bandB[color] = second;
                _bands[color] = CreateBand(color);
            }
        }

        private LineRenderer CreateBand(int color)
        {
            if (_material == null)
            {
                GameObject sample = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
                _material = sample.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.Destroy(sample);
            }

            GameObject holder = new GameObject("WardBand" + color);
            holder.transform.SetParent(_parent, false);
            LineRenderer band = holder.AddComponent<LineRenderer>();
            band.useWorldSpace   = true;
            band.positionCount   = PAIR_SIZE;
            band.widthMultiplier = BAND_WIDTH;
            band.sharedMaterial  = _material;
            band.gameObject.SetActive(false);
            return band;
        }

        // 집결 중에는 띠를 숨긴다, 거리 피해가 없는 구간이기 때문이다
        private void UpdateBands()
        {
            for (int color = 0; color < COLOR_COUNT; ++color)
            {
                LineRenderer band = _bands[color];
                if (band == null)
                {
                    continue;
                }

                bool visible = _hasStage && _positions(_bandA[color], out Vector3 a) && _positions(_bandB[color], out Vector3 b);
                if (!visible)
                {
                    band.gameObject.SetActive(false);
                    continue;
                }

                _positions(_bandA[color], out a);
                _positions(_bandB[color], out b);
                float distance = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                Color tint = (distance >= _start.LinkWarnDistance) ? BandWarning : BandNormal;
                band.SetPosition(0, a + (Vector3.up * BAND_HEIGHT));
                band.SetPosition(1, b + (Vector3.up * BAND_HEIGHT));
                band.startColor = tint;
                band.endColor   = tint;
                band.gameObject.SetActive(true);
            }
        }

        private void DestroyWardDiscs()
        {
            for (int i = 0; i < _wardDiscs.Length; ++i)
            {
                DestroyObject(ref _wardDiscs[i]);
            }
        }

        private static void DestroyObject(ref GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
                target = null;
            }
        }

        private void ClearDisplay()
        {
            DestroyObject(ref _gatherDisc);
            DestroyWardDiscs();
            HideStrike();

            for (int i = 0; i < _bands.Length; ++i)
            {
                if (_bands[i] != null)
                {
                    UnityEngine.Object.Destroy(_bands[i].gameObject);
                    _bands[i] = null;
                }

                _bandA[i] = -1;
                _bandB[i] = -1;
            }

            _hasStart = false;
            _hasStage = false;
        }
    }
}
