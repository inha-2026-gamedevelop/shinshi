// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 일반전 진행 패널. 남은 시간, 전체 처치 진행도, 웨이브별 진행 줄을 보여 준다 </summary>
    public sealed class DungeonProgressView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TIME_FORMAT  = "{0:00}:{1:00}";
        private const string COUNT_FORMAT = "<color=#EBC97A>{0}</color> / {1}";
        private const string KILL_FORMAT  = "{0}/{1}";
        private const int    SECONDS_PER_MIN = 60;

        private const float NODE_NORMAL_SIZE  = 30f;
        private const float NODE_CURRENT_SIZE = 36f;
        private const float HALF              = 0.5f;
        private const int   NONE              = -1;

        // 이 시간 이하면 경고 표시를 켠다. 색만으로 위험을 알리지 않기 위해 글자 표시도 함께 쓴다
        private const float WARNING_SECONDS = 60f;

        [Header("시간")]
        [SerializeField] private TMP_Text      _timeValue   = null;
        [SerializeField] private GameObject    _timeWarning = null;
        [Tooltip("전체 진행 게이지 위에서 경과 시간 비율 위치를 가리키는 막대")]
        [SerializeField] private RectTransform _timeMarker  = null;
        [SerializeField] private RectTransform _timeBar     = null;

        [Header("전체 진행")]
        [SerializeField] private UiGaugeFollowLabel _overallGauge = null;

        [Header("웨이브 줄 (칸 수만큼 같은 순서로 넣는다)")]
        [SerializeField] private TMP_Text      _waveCount = null;
        [SerializeField] private Image[]       _nodes     = null;
        [SerializeField] private Image[]       _links     = null; // 칸 i와 i+1 사이, 칸 수보다 하나 적다
        [SerializeField] private GameObject[]  _checks    = null;
        [SerializeField] private GameObject[]  _inners    = null;
        [SerializeField] private TMP_Text[]    _numbers   = null;
        [SerializeField] private TMP_Text[]    _planned   = null;

        [Header("색")]
        [SerializeField] private Color _goldColor          = new Color(0.922f, 0.788f, 0.478f, 1f);
        [SerializeField] private Color _pendingNodeColor   = new Color(0.137f, 0.165f, 0.212f, 1f);
        [SerializeField] private Color _linkIdleColor      = new Color(0.6f, 0.64f, 0.7f, 0.35f);
        [SerializeField] private Color _clearedTextColor   = new Color(0.6f, 0.639f, 0.698f, 1f);
        [SerializeField] private Color _currentTextColor   = new Color(0.922f, 0.941f, 0.969f, 1f);
        [SerializeField] private Color _pendingTextColor   = new Color(0.353f, 0.384f, 0.439f, 1f);
        [SerializeField] private Color _timeWarningColor   = new Color(0.95f, 0.45f, 0.4f, 1f);

        private Vector2[] _nodeBasePosition = null; // 보통 크기일 때의 위치
        private int[]     _nodeStates       = null; // 칸마다 마지막으로 그린 값, 같으면 다시 그리지 않는다
        private int[]     _nodeKilled       = null;
        private int[]     _nodePlanned      = null;
        private Color     _timeNormalColor  = Color.white;
        private float     _overallRatio     = -1f;
        private int       _remainSeconds    = -1;
        private int       _countCurrent     = -1;
        private int       _countTotal       = -1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_nodes == null) || (_nodes.Length == 0))
            {
                Debug.LogError("[DungeonProgressView] 웨이브 칸이 비어 있다", this);
                enabled = false;
                return;
            }

            if (_timeValue != null)
            {
                _timeNormalColor = _timeValue.color;
            }

            // 현재 칸은 키워 두므로 보통 크기일 때의 위치로 되돌려 저장한다
            _nodeBasePosition = new Vector2[_nodes.Length];
            _nodeStates       = new int[_nodes.Length];
            _nodeKilled       = new int[_nodes.Length];
            _nodePlanned      = new int[_nodes.Length];
            for (int i = 0; i < _nodes.Length; ++i)
            {
                _nodeStates[i] = NONE;
                RectTransform rect = _nodes[i].rectTransform;
                _nodeBasePosition[i] = KeepCenter(rect, rect.anchoredPosition, rect.sizeDelta.x, NODE_NORMAL_SIZE);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 남은 초를 올림해 표시하며 같은 초면 글자를 다시 만들지 않는다. 막대는 경과 비율 위치로 옮긴다 </summary>
        public void SetTime(float remainSeconds, float totalSeconds)
        {
            int whole = Mathf.CeilToInt(Mathf.Max(0f, remainSeconds));

            if (whole != _remainSeconds)
            {
                _remainSeconds = whole;

                bool warning = (remainSeconds <= WARNING_SECONDS);

                if (_timeValue != null)
                {
                    _timeValue.text  = string.Format(TIME_FORMAT, whole / SECONDS_PER_MIN, whole % SECONDS_PER_MIN);
                    _timeValue.color = warning ? _timeWarningColor : _timeNormalColor;
                }

                if (_timeWarning != null)
                {
                    _timeWarning.SetActive(warning);
                }
            }

            if ((_timeMarker != null) && (_timeBar != null) && (totalSeconds > 0f))
            {
                float elapsedRatio = Mathf.Clamp01(1f - (remainSeconds / totalSeconds));
                Vector2 position   = _timeMarker.anchoredPosition;
                position.x         = (_timeBar.rect.width * elapsedRatio) - (_timeMarker.rect.width * HALF);
                _timeMarker.anchoredPosition = position;
            }
        }

        public void SetOverall(float ratio)
        {
            if ((_overallGauge == null) || (Mathf.Approximately(ratio, _overallRatio)))
            {
                return;
            }

            _overallRatio = ratio;
            _overallGauge.SetRatio(ratio);
        }

        /// <summary> 현재 웨이브 번호(1부터)와 전체 웨이브 수를 "4 / 6" 모양으로 보여 준다 </summary>
        public void SetWaveCount(int current, int total)
        {
            if ((current == _countCurrent) && (total == _countTotal))
            {
                return;
            }

            _countCurrent = current;
            _countTotal   = total;

            if (_waveCount != null)
            {
                _waveCount.text = string.Format(COUNT_FORMAT, current, total);
            }
        }

        /// <summary> 웨이브 한 칸의 상태를 바꾼다. 현재 칸은 처치 수/계획 수, 나머지는 계획 수만 보여 준다 </summary>
        public void SetNode(int index, EWaveNodeState state, int killed, int planned)
        {
            if ((index < 0) || (index >= _nodes.Length))
            {
                return;
            }

            // 매 프레임 불려도 값이 그대로면 글자를 다시 만들지 않는다
            if ((_nodeStates[index] == (int)state) && (_nodeKilled[index] == killed) && (_nodePlanned[index] == planned))
            {
                return;
            }

            _nodeStates[index]  = (int)state;
            _nodeKilled[index]  = killed;
            _nodePlanned[index] = planned;

            bool isCleared = (state == EWaveNodeState.Cleared);
            bool isCurrent = (state == EWaveNodeState.Current);

            RectTransform rect = _nodes[index].rectTransform;
            float         size = isCurrent ? NODE_CURRENT_SIZE : NODE_NORMAL_SIZE;
            rect.sizeDelta        = new Vector2(size, size);
            rect.anchoredPosition = KeepCenter(rect, _nodeBasePosition[index], NODE_NORMAL_SIZE, size);

            _nodes[index].color = (isCleared || isCurrent) ? _goldColor : _pendingNodeColor;

            SetActive(_checks, index, isCleared);
            SetActive(_inners, index, isCurrent);

            if ((_numbers != null) && (index < _numbers.Length) && (_numbers[index] != null))
            {
                _numbers[index].gameObject.SetActive(!isCleared);
                _numbers[index].color = isCurrent ? _goldColor : _pendingTextColor;
            }

            if ((_planned != null) && (index < _planned.Length) && (_planned[index] != null))
            {
                _planned[index].text  = isCurrent ? string.Format(KILL_FORMAT, killed, planned) : planned.ToString();
                _planned[index].color = isCleared ? _clearedTextColor : (isCurrent ? _currentTextColor : _pendingTextColor);
            }

            if ((_links != null) && (index < _links.Length) && (_links[index] != null))
            {
                _links[index].color = isCleared ? _goldColor : _linkIdleColor;
            }
        }

        // 크기가 바뀌어도 칸의 중심이 그대로이도록 피벗을 따져 위치를 옮긴다
        private static Vector2 KeepCenter(RectTransform rect, Vector2 position, float fromSize, float toSize)
        {
            float delta = fromSize - toSize;
            return position + new Vector2((HALF - rect.pivot.x) * delta, (HALF - rect.pivot.y) * delta);
        }

        private static void SetActive(GameObject[] targets, int index, bool active)
        {
            if ((targets != null) && (index < targets.Length) && (targets[index] != null))
            {
                targets[index].SetActive(active);
            }
        }
    }
}
