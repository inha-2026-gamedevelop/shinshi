// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 도형 표시다. 핵은 노란 원기둥, 추적 여우불은 낙하 중심의 납작한 원판(추적 중 주황, 고정 뒤 빨강, 대상을 놓치면 회색),
    // 휩쓸기는 부채꼴 외곽선으로 그린다. 최종 아트가 아니다. 서버 알림(IWolbaekFoxfireSealListener)을 그대로 받아 그리며 판정이나 상태는 만들지 않는다.
    // 여우불이 대상을 따라가는 모습은 알림이 아니라 매 프레임 참가자 위치를 읽어 표시만 한다.
    // 운영 클라이언트의 표시는 복제 계층이 연결된 뒤 다른 뷰가 맡는다
    /// <summary> 여우불 유도와 봉인 핵 기믹의 임시 도형 표시, 시험 장면에서만 쓴다 </summary>
    public sealed class WolbaekFoxfireSealDevView : IWolbaekFoxfireSealListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float DISC_THICKNESS = 0.05f;
        private const float DISC_HEIGHT    = 0.03f;
        private const float CORE_HEIGHT    = 1f;
        private const float CORE_RADIUS    = 0.6f;
        private const float LINE_HEIGHT    = 0.1f;
        private const float LINE_WIDTH     = 0.12f;
        private const float DIAMETER_FACTOR = 2f;
        private const float SWEEP_FLASH    = 0.3f;
        private const int   ARC_STEPS      = 24;
        private const int   LOG_LENGTH     = 14;
        private const int   MAX_SWEEPS     = 4;

        private static readonly Color CoreColor         = new Color(1f, 0.9f, 0.2f);
        private static readonly Color TrackingColor     = new Color(1f, 0.55f, 0f);
        private static readonly Color LockedColor       = Color.red;
        private static readonly Color DetachedColor     = Color.gray;
        private static readonly Color SweepTelegraph    = new Color(1f, 0.55f, 0f);
        private static readonly Color SweepStrikeColor  = Color.red;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 참가자의 지금 위치를 읽는다, 쓰러졌거나 없으면 거짓이다 </summary>
        public delegate bool PositionReader(int combatId, out Vector3 position);

        private readonly Transform      _parent;
        private readonly Func<double>   _clock;
        private readonly PositionReader _positions;
        private readonly List<string>   _log      = new List<string>();
        private readonly List<GameObject> _coreObjects = new List<GameObject>();
        private readonly List<GameObject> _fireObjects = new List<GameObject>();
        private readonly List<int>      _fireTargets   = new List<int>();
        private readonly List<bool>     _fireDetached  = new List<bool>();
        private readonly LineRenderer[] _sweepLines    = new LineRenderer[MAX_SWEEPS];
        private readonly double[]       _sweepHideAt   = new double[MAX_SWEEPS];
        private Material                _material;
        private WolbaekSealStart        _start;
        private bool                    _hasStart;
        private bool                    _tracking;
        private bool                    _locked;
        private int                     _turnIndex;
        private int                     _successCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 최근 알림 기록, 오래된 것이 앞이다 </summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary> 지금 보이는 시작 값, 표시 중이 아니면 거짓이다 </summary>
        public bool TryGetStart(out WolbaekSealStart start)
        {
            start = _start;
            return _hasStart;
        }

        /// <summary> 알림으로 받은 누적 성공 횟수 </summary>
        public int SuccessCount => _successCount;

        /// <summary> 알림으로 받은 지금 차례 번호 </summary>
        public int TurnIndex => _turnIndex;

        /// <summary> 지금 표시 중인 핵 개수, 아직 치우지 않은 것만 센다 </summary>
        public int CoreCount => _coreObjects.Count;

        /// <summary> 지금 표시 중인 핵의 위치, 번호가 범위 밖이면 거짓이다 </summary>
        public bool TryGetCoreCenter(int index, out Vector3 center)
        {
            if ((index < 0) || (index >= _coreObjects.Count) || (_coreObjects[index] == null))
            {
                center = Vector3.zero;
                return false;
            }

            center = _coreObjects[index].transform.position;
            center.y = 0f;
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFoxfireSealDevView(Transform parent, Func<double> clock, PositionReader positions)
        {
            _parent    = parent;
            _clock     = clock;
            _positions = positions;
        }

        /// <summary> 추적 여우불 위치와 휩쓸기 플래시 종료를 갱신한다, 매 프레임 불러야 한다 </summary>
        public void Update()
        {
            if (_tracking && (!_locked))
            {
                for (int i = 0; i < _fireObjects.Count; ++i)
                {
                    if ((_fireObjects[i] == null) || _fireDetached[i])
                    {
                        continue;
                    }

                    if (_positions(_fireTargets[i], out Vector3 position))
                    {
                        _fireObjects[i].transform.position = new Vector3(position.x, DISC_HEIGHT, position.z);
                    }
                }
            }

            double now = _clock();
            for (int i = 0; i < _sweepLines.Length; ++i)
            {
                if ((_sweepLines[i] != null) && (_sweepHideAt[i] > 0d) && (now >= _sweepHideAt[i]))
                {
                    DestroySweep(i);
                }
            }
        }

        /// <summary> 도형과 기록을 모두 치운다 </summary>
        public void ClearAll()
        {
            ClearDisplay();
            _log.Clear();
        }

        public void OnSealStarted(in WolbaekSealStart start)
        {
            ClearDisplay();
            _start        = start;
            _hasStart     = true;
            _successCount = 0;
            Add("진입, 성공 " + start.RequiredSuccesses + "회가 필요, 제한 " + (start.LimitEndServerTime - start.StartServerTime) + "초");
        }

        public void OnTurnStarted(int castInstanceId, in WolbaekSealTurn turn)
        {
            ClearTurnShapes();
            _turnIndex = turn.Index;
            _tracking  = false;
            _locked    = false;

            for (int i = 0; i < turn.Cores.Count; ++i)
            {
                GameObject core = CreateDisc("SealCore" + i, turn.Cores[i].Center, CORE_RADIUS, CoreColor, CORE_HEIGHT);
                _coreObjects.Add(core);
            }

            for (int i = 0; i < turn.Foxfires.Count; ++i)
            {
                int target = turn.Foxfires[i].TargetCombatId;
                Vector3 at = _positions(target, out Vector3 position) ? position : Vector3.zero;
                _fireObjects.Add(CreateDisc("SealFoxfire" + i, at, _start.BlastRadius, TrackingColor, DISC_THICKNESS));
                _fireTargets.Add(target);
                _fireDetached.Add(false);
                _fireObjects[i].SetActive(false);
            }

            Add("차례 " + turn.Index + " 시작, 핵 " + turn.Cores.Count + "개 여우불 " + turn.Foxfires.Count + "개");
        }

        public void OnTrackingStarted(int castInstanceId, int turnIndex, double serverTime)
        {
            _tracking = true;
            for (int i = 0; i < _fireObjects.Count; ++i)
            {
                if (_fireObjects[i] != null)
                {
                    _fireObjects[i].SetActive(true);
                }
            }

            Add("추적 시작");
        }

        public void OnFoxfireDetached(int castInstanceId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            if ((foxfireIndex < 0) || (foxfireIndex >= _fireObjects.Count))
            {
                return;
            }

            _fireDetached[foxfireIndex] = true;
            _fireObjects[foxfireIndex].transform.position = new Vector3(position.x, DISC_HEIGHT, position.z);
            SetColor(_fireObjects[foxfireIndex], DetachedColor);
            Add("여우불 " + foxfireIndex + " 대상 이탈, 마지막 위치에 머문다");
        }

        public void OnSweepTelegraphed(int castInstanceId, in WolbaekSealSweep sweep)
        {
            if ((sweep.SweepIndex < 0) || (sweep.SweepIndex >= _sweepLines.Length))
            {
                return;
            }

            DestroySweep(sweep.SweepIndex);
            _sweepLines[sweep.SweepIndex] = CreateFan(sweep, SweepTelegraph);
            _sweepHideAt[sweep.SweepIndex] = 0d;
            Add("휩쓸기 " + sweep.SweepIndex + " 예고");
        }

        public void OnSweepResolved(int castInstanceId, int turnIndex, int sweepIndex, double serverTime, int hitCount)
        {
            if ((sweepIndex >= 0) && (sweepIndex < _sweepLines.Length) && (_sweepLines[sweepIndex] != null))
            {
                _sweepLines[sweepIndex].startColor = SweepStrikeColor;
                _sweepLines[sweepIndex].endColor   = SweepStrikeColor;
                _sweepHideAt[sweepIndex]           = _clock() + SWEEP_FLASH;
            }

            Add("휩쓸기 " + sweepIndex + " 판정, 적중 " + hitCount);
        }

        public void OnFoxfiresLocked(int castInstanceId, int turnIndex, double serverTime, IReadOnlyList<Vector3> centers)
        {
            _locked = true;
            for (int i = 0; (i < _fireObjects.Count) && (i < centers.Count); ++i)
            {
                _fireObjects[i].transform.position = new Vector3(centers[i].x, DISC_HEIGHT, centers[i].z);
                if (!_fireDetached[i])
                {
                    SetColor(_fireObjects[i], LockedColor);
                }
            }

            Add("낙하 위치 고정");
        }

        public void OnTurnResolved(int castInstanceId, int turnIndex, double serverTime, int destroyedCores, int totalCores,
            int playerHits, bool succeeded, int successCount)
        {
            _successCount = successCount;
            ClearTurnShapes();
            Add("차례 " + turnIndex + " 판정, 핵 " + destroyedCores + "/" + totalCores + " 파괴, 플레이어 적중 " + playerHits
                + (succeeded ? " 성공 누적 " + successCount : " 실패"));
        }

        public void OnSealSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
        {
            ClearDisplay();
            Add("성공, 무적 해제 반격 " + (counterEndServerTime - serverTime) + "초");
        }

        public void OnCounterEnded(int castInstanceId, double serverTime, bool reachedNextBoundary)
        {
            Add(reachedNextBoundary ? "반격 끝, 다음 경계에 멈춤 - 다음 기믹 인계 대기" : "반격 끝, 일반전 재개");
        }

        public void OnSealFailed(int castInstanceId, double serverTime, EWolbaekSealFail reason)
        {
            ClearDisplay();
            Add("실패 - " + reason);
        }

        public void OnSealHalted(int castInstanceId, double serverTime, EWolbaekSealHaltReason reason)
        {
            ClearDisplay();
            Add("정지 - " + reason);
        }

        public void OnSealCleared(int castInstanceId, EBossInterruptReason reason)
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

        private GameObject CreateDisc(string name, Vector3 center, float radius, Color color, float height)
        {
            GameObject disc = TestRigDisplay.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            UnityEngine.Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_parent, false);
            disc.transform.position   = new Vector3(center.x, DISC_HEIGHT, center.z);
            disc.transform.localScale = new Vector3(radius * DIAMETER_FACTOR, height, radius * DIAMETER_FACTOR);
            SetColor(disc, color);
            return disc;
        }

        private static void SetColor(GameObject target, Color color)
        {
            target.GetComponent<Renderer>().material.color = color;
        }

        private LineRenderer CreateFan(in WolbaekSealSweep sweep, Color color)
        {
            if (_material == null)
            {
                GameObject sample = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
                _material = sample.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.Destroy(sample);
            }

            GameObject holder = new GameObject("SealSweep" + sweep.SweepIndex);
            holder.transform.SetParent(_parent, false);
            LineRenderer line = holder.AddComponent<LineRenderer>();
            line.useWorldSpace   = true;
            line.widthMultiplier = LINE_WIDTH;
            line.sharedMaterial  = _material;
            line.startColor      = color;
            line.endColor        = color;
            line.positionCount   = ARC_STEPS + 3;

            Vector3 lift  = Vector3.up * LINE_HEIGHT;
            float   start = -sweep.HalfAngleDegrees;
            float   step  = (sweep.HalfAngleDegrees * DIAMETER_FACTOR) / ARC_STEPS;

            line.SetPosition(0, sweep.Origin + lift);
            for (int i = 0; i <= ARC_STEPS; ++i)
            {
                Vector3 direction = Quaternion.AngleAxis(start + (step * i), Vector3.up) * sweep.Direction;
                line.SetPosition(i + 1, sweep.Origin + (direction * sweep.Range) + lift);
            }

            line.SetPosition(ARC_STEPS + 2, sweep.Origin + lift);
            return line;
        }

        private void DestroySweep(int index)
        {
            if (_sweepLines[index] != null)
            {
                UnityEngine.Object.Destroy(_sweepLines[index].gameObject);
                _sweepLines[index] = null;
            }

            _sweepHideAt[index] = 0d;
        }

        private void ClearTurnShapes()
        {
            for (int i = 0; i < _coreObjects.Count; ++i)
            {
                DestroyObject(_coreObjects[i]);
            }

            for (int i = 0; i < _fireObjects.Count; ++i)
            {
                DestroyObject(_fireObjects[i]);
            }

            _coreObjects.Clear();
            _fireObjects.Clear();
            _fireTargets.Clear();
            _fireDetached.Clear();
            _tracking = false;
            _locked   = false;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
            }
        }

        private void ClearDisplay()
        {
            ClearTurnShapes();

            for (int i = 0; i < _sweepLines.Length; ++i)
            {
                DestroySweep(i);
            }

            _hasStart = false;
        }
    }
}
