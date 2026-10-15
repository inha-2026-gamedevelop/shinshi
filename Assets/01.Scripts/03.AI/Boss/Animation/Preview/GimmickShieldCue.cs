// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 규칙: 75%, 50%, 25% 기믹이 진행되는 동안 월백은 피해를 받지 않고, 기믹이 성공하면 무적이 풀린다
    // 이 도구는 기믹 패턴이 시작되면 월백을 감싸는 보호막을 만들고 성공 판정 시각에 사라지게 한다
    // 효과 프리팹은 대부분 수명이 정해져 있어 기믹 동안 이어지도록 모든 파티클을 반복으로 바꾼다
    /// <summary> 기믹 동안 월백을 감싸는 무적 보호막 효과를 후보 중에서 골라 보여 주는 확인용 도구, 오른쪽에 후보 버튼을 그린다 </summary>
    public sealed class GimmickShieldCue : MonoBehaviour
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
        private const int   NONE_INDEX    = -1;

        private const string TITLE_TEXT    = "기믹 중 월백 보호막";
        private const string NONE_TEXT     = "보호막 없음";
        private const string SELECTED_MARK = "> ";
        private const string SMALLER_TEXT  = "크기 줄이기";
        private const string LARGER_TEXT   = "크기 키우기";
        private const string SCALE_FORMAT  = "{0}  (크기 {1:F2})";

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 보호막 후보 하나의 프리팹과 월백 기준 위치 </summary>
        [Serializable]
        public sealed class Candidate
        {
            [SerializeField] private string     _label;
            [SerializeField] private GameObject _prefab;
            [SerializeField] private Vector3    _offset;
            [SerializeField] private float      _scale = 1f;

            public string     Label  => _label;
            public GameObject Prefab => _prefab;
            public Vector3    Offset => _offset;
            public float      Scale  => _scale;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private string[] _patternLabels;
        [SerializeField] private Candidate[] _candidates;
        [SerializeField] private float _fadeSeconds = 2.5f;
        [SerializeField] private bool _showPanel;

        private int        _selected = 0;
        private float      _scaleMultiplier = 1f;
        private GameObject _instance;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            _pattern.Struck -= OnStruck;
            DestroyShield();
        }

        private void OnGUI()
        {
            if (!_showPanel)
            {
                return;
            }

            float x = Screen.width - PANEL_WIDTH;
            float y = START_Y;
            GUI.Label(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), TITLE_TEXT);
            y += BUTTON_STEP;

            if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), (_selected == NONE_INDEX ? SELECTED_MARK : string.Empty) + NONE_TEXT))
            {
                Select(NONE_INDEX);
            }

            y += BUTTON_STEP;

            for (int i = 0; i < _candidates.Length; ++i)
            {
                if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), (i == _selected ? SELECTED_MARK : string.Empty) + _candidates[i].Label))
                {
                    Select(i);
                }

                y += BUTTON_STEP;
            }

            if (_selected != NONE_INDEX)
            {
                DrawScaleControls(x, y + (BUTTON_STEP * HALF), _candidates[_selected]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawScaleControls(float x, float y, Candidate candidate)
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
        }

        private void Select(int index)
        {
            _selected = index;
            _scaleMultiplier = 1f;
            Replay();
        }

        private void Replay()
        {
            _pattern.PlayByLabel(_patternLabels[0]);
        }

        private void OnStarted(string label, float strikeSeconds)
        {
            DestroyShield();

            if ((Array.IndexOf(_patternLabels, label) < 0) || (_selected == NONE_INDEX))
            {
                return;
            }

            Candidate candidate = _candidates[_selected];
            _instance = Instantiate(candidate.Prefab, _origin.position + candidate.Offset, Quaternion.identity);
            _instance.transform.localScale = candidate.Prefab.transform.localScale * (candidate.Scale * _scaleMultiplier);

            foreach (ParticleSystem system in _instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.loop = true;
            }
        }

        // 성공 판정 시각에 새 파티클을 멈추고 남은 파티클이 사라진 뒤 지운다
        private void OnStruck(string label, int shotIndex)
        {
            if ((_instance == null) || (Array.IndexOf(_patternLabels, label) < 0))
            {
                return;
            }

            foreach (ParticleSystem system in _instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(_instance, _fadeSeconds);
            _instance = null;
        }

        private void DestroyShield()
        {
            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }
        }
    }
}
