// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 도형 표시다. 중앙 바닥은 파란 원판, 외곽 바닥은 회색 원판이며 바닥 제거 알림에서 사라진다(실제 바닥이 아니라 표시만).
    // 노란 여우불은 노란 원기둥, 빨간 여우불은 낙하 중심의 납작한 원판(추적 중 주황, 고정 뒤 빨강, 대상을 놓치면 회색)이다.
    // 최종 아트가 아니다. 서버 알림(IWolbaekFinalAreaListener)을 그대로 받아 그리며 판정이나 상태는 만들지 않는다.
    // 빨간 여우불이 대상을 따라가는 모습은 알림이 아니라 매 프레임 참가자 위치를 읽어 표시만 한다
    /// <summary> 최후의 영역 기믹의 임시 도형 표시, 시험 장면에서만 쓴다 </summary>
    public sealed class WolbaekFinalAreaDevView : IWolbaekFinalAreaListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float DISC_THICKNESS  = 0.05f;
        private const float FLOOR_THICKNESS = 0.02f;
        private const float DISC_HEIGHT     = 0.03f;
        private const float YELLOW_HEIGHT   = 1.2f;
        private const float DIAMETER_FACTOR = 2f;
        private const int   LOG_LENGTH      = 14;

        private static readonly Color CentralColor   = new Color(0.2f, 0.4f, 0.9f);
        private static readonly Color OuterColor     = new Color(0.45f, 0.45f, 0.45f);
        private static readonly Color YellowColor    = new Color(1f, 0.9f, 0.2f);
        private static readonly Color TrackingColor  = new Color(1f, 0.55f, 0f);
        private static readonly Color LockedColor    = Color.red;
        private static readonly Color DetachedColor  = Color.gray;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 참가자의 지금 위치를 읽는다, 쓰러졌거나 없으면 거짓이다 </summary>
        public delegate bool PositionReader(int combatId, out Vector3 position);

        private readonly Transform        _parent;
        private readonly Vector3          _center;
        private readonly float            _arenaRadius;
        private readonly PositionReader   _positions;
        private readonly List<string>     _log          = new List<string>();
        private readonly List<GameObject> _yellowObjects = new List<GameObject>();
        private readonly List<GameObject> _redObjects   = new List<GameObject>();
        private readonly List<int>        _redTargets   = new List<int>();
        private readonly List<bool>       _redDetached  = new List<bool>();
        private GameObject                _outerFloor;
        private GameObject                _centralFloor;
        private WolbaekFinalStart         _start;
        private bool                      _locked;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 최근 알림 기록, 오래된 것이 앞이다 </summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary> 지금 표시 중인 노란 여우불 수 </summary>
        public int YellowCount => _yellowObjects.Count;

        /// <summary> 외곽 바닥 표시가 남아 있다 </summary>
        public bool HasOuterFloor => _outerFloor != null;

        /// <summary> 지금 표시 중인 노란 여우불의 위치, 번호가 범위 밖이면 거짓이다 </summary>
        public bool TryGetYellowCenter(int index, out Vector3 center)
        {
            if ((index < 0) || (index >= _yellowObjects.Count) || (_yellowObjects[index] == null))
            {
                center = Vector3.zero;
                return false;
            }

            center = _yellowObjects[index].transform.position;
            center.y = 0f;
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalAreaDevView(Transform parent, Vector3 center, float arenaRadius, PositionReader positions)
        {
            _parent      = parent;
            _center      = center;
            _arenaRadius = arenaRadius;
            _positions   = positions;
        }

        /// <summary> 추적 중인 빨간 여우불 위치를 갱신한다, 매 프레임 불러야 한다 </summary>
        public void Update()
        {
            if (_locked)
            {
                return;
            }

            for (int i = 0; i < _redObjects.Count; ++i)
            {
                if ((_redObjects[i] == null) || _redDetached[i])
                {
                    continue;
                }

                if (_positions(_redTargets[i], out Vector3 position))
                {
                    _redObjects[i].transform.position = new Vector3(position.x, DISC_HEIGHT, position.z);
                }
            }
        }

        /// <summary> 도형과 기록을 모두 치운다 </summary>
        public void ClearAll()
        {
            ClearDisplay();
            _log.Clear();
        }

        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            ClearDisplay();
            _start        = start;
            _outerFloor   = CreateDisc("FinalOuterFloor", _center, _arenaRadius, OuterColor, FLOOR_THICKNESS);
            _centralFloor = CreateDisc("FinalCentralFloor", _center, start.CentralRadius, CentralColor, FLOOR_THICKNESS);
            Add("진입, 중앙 " + start.CentralRadius + "m만 남는다, 노란 여우불 " + start.YellowCount + "개 예정");
        }

        public void OnOuterFloorRemoved(int castInstanceId, double serverTime, IReadOnlyList<int> fallen)
        {
            DestroyObject(_outerFloor);
            _outerFloor = null;
            Add("외곽 바닥 제거, 추락 사망 " + fallen.Count + "명");
        }

        public void OnYellowsSpawned(int castInstanceId, double serverTime, IReadOnlyList<WolbaekFinalYellow> yellows)
        {
            for (int i = 0; i < yellows.Count; ++i)
            {
                _yellowObjects.Add(CreateDisc("FinalYellow" + i, yellows[i].Position, 1f, YellowColor, YELLOW_HEIGHT));
            }

            Add("노란 여우불 " + yellows.Count + "개 생성");
        }

        public void OnYellowDestroyed(int castInstanceId, int yellowIndex, double serverTime, int remaining)
        {
            if ((yellowIndex >= 0) && (yellowIndex < _yellowObjects.Count))
            {
                DestroyObject(_yellowObjects[yellowIndex]);
                _yellowObjects[yellowIndex] = null;
            }

            Add("노란 여우불 " + yellowIndex + " 파괴, 남은 " + remaining);
        }

        public void OnBundleStarted(int castInstanceId, in WolbaekFinalBundle bundle)
        {
            ClearReds();
            _locked = false;

            for (int i = 0; i < bundle.Reds.Count; ++i)
            {
                int target = bundle.Reds[i].TargetCombatId;
                Vector3 at = _positions(target, out Vector3 position) ? position : Vector3.zero;
                _redObjects.Add(CreateDisc("FinalRed" + i, at, _start.RedBlastRadius, TrackingColor, DISC_THICKNESS));
                _redTargets.Add(target);
                _redDetached.Add(false);
            }

            Add("묶음 " + bundle.Index + " 시작, 빨간 여우불 " + bundle.Reds.Count + "개");
        }

        public void OnRedDetached(int castInstanceId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            if ((redIndex < 0) || (redIndex >= _redObjects.Count) || (_redObjects[redIndex] == null))
            {
                return;
            }

            _redDetached[redIndex] = true;
            _redObjects[redIndex].transform.position = new Vector3(position.x, DISC_HEIGHT, position.z);
            _redObjects[redIndex].GetComponent<Renderer>().material.color = DetachedColor;
            Add("빨간 여우불 " + redIndex + " 대상 이탈, 마지막 위치에 머문다");
        }

        public void OnRedsLocked(int castInstanceId, int bundleIndex, double serverTime, IReadOnlyList<Vector3> centers)
        {
            _locked = true;
            for (int i = 0; (i < _redObjects.Count) && (i < centers.Count); ++i)
            {
                if (_redObjects[i] == null)
                {
                    continue;
                }

                _redObjects[i].transform.position = new Vector3(centers[i].x, DISC_HEIGHT, centers[i].z);
                if (!_redDetached[i])
                {
                    _redObjects[i].GetComponent<Renderer>().material.color = LockedColor;
                }
            }

            Add("낙하 위치 고정");
        }

        public void OnBundleResolved(int castInstanceId, int bundleIndex, double serverTime, int singleHits, int doubleKills)
        {
            ClearReds();
            Add("묶음 " + bundleIndex + " 폭발, 단일 적중 " + singleHits + " 중복 즉사 " + doubleKills);
        }

        public void OnFinalSucceeded(int castInstanceId, double serverTime)
        {
            ClearDisplay();
            Add("최종 성공, 남은 빨간 폭발 취소");
        }

        public void OnFinalFailed(int castInstanceId, double serverTime, EWolbaekFinalFail reason)
        {
            ClearDisplay();
            Add("실패 - " + reason);
        }

        public void OnFinalHalted(int castInstanceId, double serverTime, EWolbaekFinalHaltReason reason)
        {
            ClearDisplay();
            Add("정지 - " + reason);
        }

        public void OnFinalCleared(int castInstanceId, EBossInterruptReason reason)
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
            disc.GetComponent<Renderer>().material.color = color;
            return disc;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
            }
        }

        private void ClearReds()
        {
            for (int i = 0; i < _redObjects.Count; ++i)
            {
                DestroyObject(_redObjects[i]);
            }

            _redObjects.Clear();
            _redTargets.Clear();
            _redDetached.Clear();
            _locked = false;
        }

        private void ClearDisplay()
        {
            ClearReds();

            for (int i = 0; i < _yellowObjects.Count; ++i)
            {
                DestroyObject(_yellowObjects[i]);
            }

            _yellowObjects.Clear();
            DestroyObject(_outerFloor);
            DestroyObject(_centralFloor);
            _outerFloor   = null;
            _centralFloor = null;
        }
    }
}
