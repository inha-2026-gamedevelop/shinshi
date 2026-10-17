// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 후보 버튼을 누르면 그 패턴을 확정한 타이밍으로 재생하고, 서버 판정 시각마다 후보 효과를 월백 기준 위치에 만든다
    // 크기와 방향은 후보마다 다르므로 눈으로 보고 정한다, 크기는 버튼으로 바꿔 보고 정한 값을 후보 설정에 옮긴다
    /// <summary> 패턴에 어울리는 효과를 서드파티 후보 중에서 고르기 위한 비교용 도구, Game 뷰 오른쪽에 버튼을 그린다 </summary>
    public sealed class PatternVfxCandidates : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float PANEL_WIDTH   = 250f;
        private const float BUTTON_WIDTH  = 240f;
        private const float BUTTON_HEIGHT = 24f;
        private const float BUTTON_STEP   = 26f;
        private const float START_Y       = 330f;
        private const float SCALE_STEP    = 1.25f;
        private const float HALF          = 0.5f;

        private const string SELECTED_MARK = "> ";
        private const string REPLAY_TEXT   = "다시 재생";
        private const string SMALLER_TEXT  = "크기 줄이기";
        private const string LARGER_TEXT   = "크기 키우기";
        private const string SCALE_FORMAT  = "{0}  (크기 {1:F2})";

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 후보 하나의 프리팹과 놓는 방식 </summary>
        [Serializable]
        public sealed class Candidate
        {
            [SerializeField] private string     _label;
            [SerializeField] private GameObject _prefab;
            [SerializeField] private Vector3    _offset;
            [SerializeField] private Vector3    _eulerOffset;
            [SerializeField] private float      _scale    = 1f;
            [SerializeField] private float      _lifetime = 4f;

            public string     Label       => _label;
            public GameObject Prefab      => _prefab;
            public Vector3    Offset      => _offset;
            public Vector3    EulerOffset => _eulerOffset;
            public float      Scale       => _scale;
            public float      Lifetime    => _lifetime;
        }

        /// <summary> 같은 패턴에 쓸 후보 묶음 </summary>
        [Serializable]
        public sealed class Group
        {
            [SerializeField] private string      _title;
            [SerializeField] private string      _patternLabel;
            [SerializeField] private bool        _spawnOnEveryStrike = true;
            [SerializeField] private Candidate[] _candidates;

            public string      Title              => _title;
            public string      PatternLabel       => _patternLabel;
            public bool        SpawnOnEveryStrike => _spawnOnEveryStrike;
            public Candidate[] Candidates         => _candidates;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private Group[] _groups;

        private int   _activeGroup;
        private int   _selected = -1;
        private float _scaleMultiplier = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Struck -= OnStruck;
        }

        private void OnGUI()
        {
            float x = Screen.width - PANEL_WIDTH;
            float y = START_Y;

            for (int i = 0; i < _groups.Length; ++i)
            {
                if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), (i == _activeGroup ? SELECTED_MARK : string.Empty) + _groups[i].Title))
                {
                    _activeGroup = i;
                    _selected = -1;
                }

                y += BUTTON_STEP;
            }

            y += BUTTON_STEP * HALF;
            Candidate[] candidates = _groups[_activeGroup].Candidates;

            for (int i = 0; i < candidates.Length; ++i)
            {
                string text = (i == _selected ? SELECTED_MARK : string.Empty) + candidates[i].Label;

                if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), text))
                {
                    _selected = i;
                    _scaleMultiplier = 1f;
                    Replay();
                }

                y += BUTTON_STEP;
            }

            if (_selected >= 0)
            {
                DrawControls(x, y + (BUTTON_STEP * HALF), candidates[_selected]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawControls(float x, float y, Candidate candidate)
        {
            GUI.Label(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), string.Format(SCALE_FORMAT, candidate.Label, candidate.Scale * _scaleMultiplier));
            y += BUTTON_STEP;

            if (GUI.Button(new Rect(x, y, BUTTON_WIDTH * HALF, BUTTON_HEIGHT), SMALLER_TEXT))
            {
                _scaleMultiplier /= SCALE_STEP;
                Replay();
            }

            if (GUI.Button(new Rect(x + (BUTTON_WIDTH * HALF), y, BUTTON_WIDTH * HALF, BUTTON_HEIGHT), LARGER_TEXT))
            {
                _scaleMultiplier *= SCALE_STEP;
                Replay();
            }

            y += BUTTON_STEP;

            if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), REPLAY_TEXT))
            {
                Replay();
            }
        }

        private void Replay()
        {
            _pattern.PlayByLabel(_groups[_activeGroup].PatternLabel);
        }

        private void OnStruck(string label, int shotIndex)
        {
            Group group = _groups[_activeGroup];

            if ((_selected < 0) || (label != group.PatternLabel) || ((!group.SpawnOnEveryStrike) && (shotIndex > 0)))
            {
                return;
            }

            Candidate candidate = group.Candidates[_selected];

            if (candidate.Prefab == null)
            {
                return;
            }

            Vector3 position = _origin.position + (_origin.rotation * candidate.Offset);
            Quaternion rotation = _origin.rotation * Quaternion.Euler(candidate.EulerOffset);
            GameObject instance = Instantiate(candidate.Prefab, position, rotation);
            instance.transform.localScale = candidate.Prefab.transform.localScale * (candidate.Scale * _scaleMultiplier);
            Destroy(instance, candidate.Lifetime);
        }
    }
}
