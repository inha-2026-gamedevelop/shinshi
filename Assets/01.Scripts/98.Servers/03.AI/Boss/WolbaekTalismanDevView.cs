// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 도형 표시다. 구조물은 납작한 원판, 고리는 선 두 줄, 직선은 납작한 막대로 그린다.
    // 최종 아트가 아니고 문양 그림도 확정하지 않았다(문양 번호만 글자로 보인다). 서버 알림(IWolbaekTalismanListener)을 그대로 받아 그리며
    // 판정이나 상태는 만들지 않는다. 운영 클라이언트의 표시는 복제 계층이 연결된 뒤 다른 뷰가 맡는다
    /// <summary> 부적진 기믹의 임시 도형 표시, 시험 장면에서만 쓴다 </summary>
    public sealed class WolbaekTalismanDevView : IWolbaekTalismanListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    RING_SEGMENTS    = 64;
        private const float  RING_WIDTH       = 0.2f;
        private const float  RING_HEIGHT      = 0.08f;
        private const float  DISC_THICKNESS   = 0.05f;
        private const float  DISC_HEIGHT      = 0.03f;
        private const float  LINE_THICKNESS   = 0.05f;
        private const float  LINE_HEIGHT      = 0.06f;
        private const float  HALF             = 0.5f;
        private const float  STRIKE_FLASH     = 0.3f;
        private const int    LOG_LENGTH       = 10;
        private const int    RING_COUNT       = 2;   // 고리 공격의 안쪽 선과 바깥쪽 선
        private const float  DIAMETER_FACTOR  = 2f;  // 반지름에서 지름으로
        private const float  FULL_TURN        = Mathf.PI * 2f;

        private static readonly Color TelegraphColor = new Color(1f, 0.55f, 0f);
        private static readonly Color StrikeColor    = Color.red;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Transform      _parent;
        private readonly Func<double>   _clock;
        private readonly List<GameObject> _discs  = new List<GameObject>();
        private readonly List<string>   _log      = new List<string>();
        private readonly LineRenderer[] _rings    = new LineRenderer[RING_COUNT];
        private GameObject              _lineBar;
        private Material               _material;
        private double                  _hideAt;
        private bool                   _isFlashing;
        private WolbaekTalismanStart    _start;
        private bool                   _hasStart;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 최근 알림 기록, 오래된 것이 앞이다 </summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary> 지금 보이는 구조물 배치, 표시 중이 아니면 비어 있다 </summary>
        public IReadOnlyList<WolbaekTalismanStructure> Structures
            => _hasStart ? _start.Structures : (IReadOnlyList<WolbaekTalismanStructure>)Array.Empty<WolbaekTalismanStructure>();

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanDevView(Transform parent, Func<double> clock)
        {
            _parent = parent;
            _clock  = clock;
        }

        /// <summary> 플래시 종료 시각을 확인한다, 매 프레임 불러야 한다 </summary>
        public void Update()
        {
            if ((_isFlashing) && (_clock() >= _hideAt))
            {
                HideAttack();
            }
        }

        /// <summary> 도형과 기록을 모두 치운다 </summary>
        public void ClearAll()
        {
            ClearDisplay();
            _log.Clear();
        }

        public void OnFormationStarted(in WolbaekTalismanStart start)
        {
            ClearDisplay();
            _start    = start;
            _hasStart = true;

            for (int i = 0; i < start.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = start.Structures[i];
                GameObject disc = TestRigDisplay.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "TalismanStructure" + i;
                UnityEngine.Object.Destroy(disc.GetComponent<Collider>());
                disc.transform.SetParent(_parent, false);
                disc.transform.position   = structure.Center + (Vector3.up * DISC_HEIGHT);
                disc.transform.localScale = new Vector3(structure.Radius * DIAMETER_FACTOR, DISC_THICKNESS, structure.Radius * DIAMETER_FACTOR);
                disc.GetComponent<Renderer>().material.color = ToColor(structure.Color);
                _discs.Add(disc);
            }

            Add("진입, 구조물 " + start.Structures.Count + "개");
        }

        public void OnHoldStarted(int castInstanceId, double serverTime)
        {
            Add("전원 자리 도착, 연속 유지 시작");
        }

        public void OnHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            Add("유지 초기화 - " + reason + " (참가자 " + combatId + ")");
        }

        public void OnAttackTelegraphed(int castInstanceId, in WolbaekTalismanAttack attack)
        {
            HideAttack();
            if (attack.Kind == EWolbaekTalismanAttackKind.Ring)
            {
                ShowRing(0, attack.Origin, attack.InnerRadius);
                ShowRing(1, attack.Origin, attack.OuterRadius);
                Add("고리 예고 " + attack.InnerRadius + "~" + attack.OuterRadius + "m");
                return;
            }

            ShowLine(attack);
            Add("직선 예고, 대상 " + attack.TargetCombatId);
        }

        public void OnAttackStruck(int castInstanceId, int attackId, double strikeServerTime, int hitCount)
        {
            Flash();
            Add("공격 " + attackId + " 판정, 적중 " + hitCount);
        }

        public void OnAttackCancelled(int castInstanceId, int attackId)
        {
            HideAttack();
            Add("공격 " + attackId + " 취소");
        }

        public void OnFormationSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
        {
            ClearDisplay();
            Add("성공, 반격 시간 시작");
        }

        public void OnCounterEnded(int castInstanceId, double serverTime)
        {
            Add("반격 시간 끝");
        }

        public void OnFormationFailed(int castInstanceId, double serverTime, EWolbaekFormationFail reason)
        {
            ClearDisplay();
            Add("실패 - " + reason);
        }

        public void OnFormationHalted(int castInstanceId, double serverTime, EWolbaekHaltReason reason)
        {
            ClearDisplay();
            Add("정지 - " + reason);
        }

        public void OnFormationCleared(int castInstanceId, EBossInterruptReason reason)
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

        private void ShowRing(int slot, Vector3 center, float radius)
        {
            LineRenderer ring = EnsureRing(slot);
            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float angle = (i / (float)RING_SEGMENTS) * FULL_TURN;
                ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, RING_HEIGHT, Mathf.Sin(angle) * radius));
            }

            ring.startColor = TelegraphColor;
            ring.endColor   = TelegraphColor;
            ring.gameObject.SetActive(true);
        }

        private void ShowLine(in WolbaekTalismanAttack attack)
        {
            if (_lineBar == null)
            {
                _lineBar = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
                _lineBar.name = "TalismanLine";
                UnityEngine.Object.Destroy(_lineBar.GetComponent<Collider>());
                _lineBar.transform.SetParent(_parent, false);
            }

            _lineBar.transform.position   = attack.Origin + (attack.Direction * (attack.Length * HALF)) + (Vector3.up * LINE_HEIGHT);
            _lineBar.transform.rotation   = Quaternion.LookRotation(attack.Direction, Vector3.up);
            _lineBar.transform.localScale = new Vector3(attack.Width, LINE_THICKNESS, attack.Length);
            _lineBar.GetComponent<Renderer>().material.color = TelegraphColor;
            _lineBar.SetActive(true);
        }

        private LineRenderer EnsureRing(int slot)
        {
            if (_rings[slot] != null)
            {
                return _rings[slot];
            }

            if (_material == null)
            {
                GameObject sample = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
                _material = sample.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.Destroy(sample);
            }

            GameObject holder = new GameObject("TalismanRing" + slot);
            holder.transform.SetParent(_parent, false);
            LineRenderer ring = holder.AddComponent<LineRenderer>();
            ring.useWorldSpace  = true;
            ring.positionCount  = RING_SEGMENTS + 1;
            ring.widthMultiplier = RING_WIDTH;
            ring.sharedMaterial = _material;
            _rings[slot] = ring;
            return ring;
        }

        private void Flash()
        {
            for (int i = 0; i < _rings.Length; ++i)
            {
                if ((_rings[i] != null) && (_rings[i].gameObject.activeSelf))
                {
                    _rings[i].startColor = StrikeColor;
                    _rings[i].endColor   = StrikeColor;
                }
            }

            if ((_lineBar != null) && (_lineBar.activeSelf))
            {
                _lineBar.GetComponent<Renderer>().material.color = StrikeColor;
            }

            _isFlashing = true;
            _hideAt     = _clock() + STRIKE_FLASH;
        }

        private void HideAttack()
        {
            for (int i = 0; i < _rings.Length; ++i)
            {
                if (_rings[i] != null)
                {
                    _rings[i].gameObject.SetActive(false);
                }
            }

            if (_lineBar != null)
            {
                _lineBar.SetActive(false);
            }

            _isFlashing = false;
        }

        private void ClearDisplay()
        {
            for (int i = 0; i < _discs.Count; ++i)
            {
                UnityEngine.Object.Destroy(_discs[i]);
            }

            _discs.Clear();
            HideAttack();
            _hasStart = false;
        }

        private static Color ToColor(EWolbaekTalismanColor color)
        {
            switch (color)
            {
                case EWolbaekTalismanColor.Red:
                    return Color.red;
                case EWolbaekTalismanColor.Blue:
                    return Color.blue;
                case EWolbaekTalismanColor.Yellow:
                    return Color.yellow;
                default:
                    return Color.green;
            }
        }
    }
}
