// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.AI
{
    // 항목 형식은 "라벨|상태이름"이면 라벨을 버튼에 쓰고 상태를 재생하고, "상태이름"만 있으면 그대로 쓴다
    // "---제목"으로 시작하면 구분 제목이고, "A>B"이면 A가 끝난 뒤 B를 이어서 재생한다
    /// <summary> Game 뷰의 버튼으로 Animator 상태를 바꿔 가며 모션 클립을 비교하는 확인용 도구 </summary>
    public sealed class MotionPreviewButtons : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string HEADER_PREFIX    = "---";
        private const char   LABEL_SEPARATOR  = '|';
        private const char   CHAIN_SEPARATOR  = '>';
        private const string LABEL_JOIN       = "  -  ";
        private const string INPLACE_SUFFIX   = "_inplace";

        private const float  MARGIN           = 10f;
        private const float  BUTTON_WIDTH     = 325f;
        private const float  BUTTON_HEIGHT    = 24f;
        private const float  BUTTON_GAP       = 2f;
        private const float  COLUMN_WIDTH     = 330f;
        private const float  SCREEN_PADDING   = 20f;
        private const int    HEADER_FONT_SIZE = 13;

        private const float  CROSSFADE_SECONDS    = 0.1f;
        private const float  CHAIN_ENTER_SECONDS  = 0.15f;
        private const float  CHAIN_END_NORMALIZED = 0.97f;

        private static readonly Color HEADER_COLOR = new Color(1f, 0.9f, 0.4f);

        /****************************************
        *                Fields
        ****************************************/

        [FormerlySerializedAs("animator")]
        [SerializeField] private Animator _animator;

        [FormerlySerializedAs("states")]
        [SerializeField] private string[] _states;

        private static GUIStyle s_headerStyle;

        private Coroutine _running;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
        }

        private void OnGUI()
        {
            if ((_animator == null) || (_states == null))
            {
                return;
            }

            // 버튼이 많으면 열을 나눠 화면 안에 들어오게 한다
            int perColumn = Mathf.Max(1, Mathf.FloorToInt((Screen.height - SCREEN_PADDING) / (BUTTON_HEIGHT + BUTTON_GAP)));

            for (int i = 0; i < _states.Length; ++i)
            {
                int column = i / perColumn;
                int row    = i % perColumn;
                Rect rect  = new Rect(MARGIN + (column * COLUMN_WIDTH), MARGIN + (row * (BUTTON_HEIGHT + BUTTON_GAP)),
                    BUTTON_WIDTH, BUTTON_HEIGHT);

                DrawEntry(rect, _states[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawEntry(Rect rect, string entry)
        {
            if (entry.StartsWith(HEADER_PREFIX))
            {
                GUI.Label(rect, entry.Substring(HEADER_PREFIX.Length), GetHeaderStyle());
                return;
            }

            string label = entry;
            string state = entry;
            int    bar   = entry.IndexOf(LABEL_SEPARATOR);

            if (bar >= 0)
            {
                state = entry.Substring(bar + 1).Trim();
                label = entry.Substring(0, bar).Trim() + LABEL_JOIN + state.Replace(INPLACE_SUFFIX, string.Empty);
            }

            if (GUI.Button(rect, label))
            {
                Play(state);
            }
        }

        private static GUIStyle GetHeaderStyle()
        {
            if (s_headerStyle == null)
            {
                s_headerStyle = new GUIStyle(GUI.skin.label);
                s_headerStyle.fontSize = HEADER_FONT_SIZE;
                s_headerStyle.normal.textColor = HEADER_COLOR;
            }

            return s_headerStyle;
        }

        private void Play(string entry)
        {
            if (_running != null)
            {
                StopCoroutine(_running);
            }

            _running = StartCoroutine(PlayChain(entry.Split(CHAIN_SEPARATOR)));
        }

        private IEnumerator PlayChain(string[] chain)
        {
            for (int i = 0; i < chain.Length; ++i)
            {
                _animator.CrossFade(chain[i], CROSSFADE_SECONDS, 0, 0f);

                if (i == (chain.Length - 1))
                {
                    yield break;
                }

                // 전환이 끝나 새 상태에 들어간 뒤 그 클립이 거의 끝날 때까지 기다린다
                yield return new WaitForSeconds(CHAIN_ENTER_SECONDS);

                while (_animator.GetCurrentAnimatorStateInfo(0).normalizedTime < CHAIN_END_NORMALIZED)
                {
                    yield return null;
                }
            }
        }
    }
}
