// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 이동을 찍은 지면에 짧은 표시를 재생한다, 서버가 거절하면 같은 표시를 거절 색으로 바꾼다 </summary>
    public sealed class UiMoveClickMarker : ClientView
    {
        // 표시 하나를 층 몇 장(고리, 점, 문양 등)으로 쌓고, 층마다 재생 진행도 0~1에 대한 곡선으로 크기, 투명도, 높이를 정한다.
        // 후보 모양을 코드 수정 없이 프리팹 값만 바꿔 만들려고 층 단위 곡선으로 두었다. 시간 진행은 DOTween 하나가 맡는다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private sealed class Layer
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private Renderer       _renderer   = null;
            [SerializeField] private Color          _color      = Color.white;
            [Tooltip("진행도 0~1에 대한 크기 배율, 프리팹에 둔 크기가 1이다")]
            [SerializeField] private AnimationCurve _scale      = AnimationCurve.Constant(0f, 1f, 1f);
            [Tooltip("진행도 0~1에 대한 투명도 배율")]
            [SerializeField] private AnimationCurve _alpha      = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            [Tooltip("진행도 0~1에 대한 높이 변화(m), 프리팹에 둔 위치에 더한다")]
            [SerializeField] private AnimationCurve _height     = AnimationCurve.Constant(0f, 1f, 0f);
            [Tooltip("재생하는 동안 세로축으로 도는 각도(도)")]
            [SerializeField] private float          _spin       = 0f;
            [Tooltip("지면에 눕히지 않고 세로로 세워 카메라 쪽을 보게 한다, 빛 기둥 같은 층에 쓴다")]
            [SerializeField] private bool           _faceCamera = false;

            private Transform  _transform    = null;
            private Vector3    _basePosition = Vector3.zero;
            private Vector3    _baseScale    = Vector3.one;
            private Quaternion _baseRotation = Quaternion.identity;

            public bool IsValid => _renderer != null;

            /****************************************
            *                Methods
            ****************************************/

            public void Capture()
            {
                _transform    = _renderer.transform;
                _basePosition = _transform.localPosition;
                _baseScale    = _transform.localScale;
                _baseRotation = _transform.localRotation;
            }

            public void SetVisible(bool isVisible) { _renderer.enabled = isVisible; }

            public void Apply(float progress, Quaternion billboard, Color tint, bool useTint, MaterialPropertyBlock properties)
            {
                _transform.localScale    = _baseScale * _scale.Evaluate(progress);
                _transform.localPosition = _basePosition + (Vector3.up * _height.Evaluate(progress));
                _transform.localRotation = _faceCamera
                    ? billboard
                    : Quaternion.AngleAxis(_spin * progress, Vector3.up) * _baseRotation;

                // 거절 색은 색만 바꾸고 층마다 정한 밝기 차이(알파)는 그대로 둔다
                Color color = useTint ? new Color(tint.r, tint.g, tint.b, _color.a) : _color;
                color.a *= Mathf.Clamp01(_alpha.Evaluate(progress));

                _renderer.GetPropertyBlock(properties);
                properties.SetColor(_colorId, color);
                _renderer.SetPropertyBlock(properties);
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _colorId = Shader.PropertyToID("_Color");

        [Header("재생")]
        [SerializeField, Min(0.05f)] private float _duration    = 0.6f;
        [SerializeField]             private float _groundLift  = 0.04f; // m, 지면과 겹쳐 깜빡이지 않을 만큼 띄운다
        [SerializeField]             private Color _rejectColor = new Color(1.4f, 0.32f, 0.26f, 1f);

        [Header("층")]
        [SerializeField] private Layer[] _layers = Array.Empty<Layer>();

        private MaterialPropertyBlock _properties = null;
        private Tween                 _tween      = null;
        private Camera                _camera     = null;
        private float                 _progress   = 0f;
        private bool                  _isRejected = false;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _properties = new MaterialPropertyBlock();
            for (int i = 0; i < _layers.Length; ++i)
            {
                if (_layers[i].IsValid)
                {
                    _layers[i].Capture();
                    _layers[i].SetVisible(false);
                }
            }

            // 클릭마다 다시 재생하므로 한 번 만들어 재사용한다
            _tween = DOTween.To(GetProgress, SetProgress, 1f, _duration)
                .SetEase(Ease.Linear)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject)
                .OnComplete(HideLayers);
        }

        private void OnDisable()
        {
            if (_tween != null)
            {
                _tween.Pause();
            }

            HideLayers();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지면 좌표에서 처음부터 다시 재생한다, 이전 표시는 끊고 새 지점으로 옮긴다 </summary>
        public void Play(Vector3 point)
        {
            if ((!enabled) || (_tween == null))
            {
                return;
            }

            transform.SetPositionAndRotation(point + (Vector3.up * _groundLift), Quaternion.identity);
            _isRejected = false;

            // 트윈은 다음 갱신에서야 첫 값을 넣으므로 보이기 전에 시작 모양으로 맞춰 이전 재생의 끝 모양이 한 프레임 비치지 않게 한다
            _progress = 0f;
            ApplyLayers();
            for (int i = 0; i < _layers.Length; ++i)
            {
                if (_layers[i].IsValid)
                {
                    _layers[i].SetVisible(true);
                }
            }

            _tween.Restart();
        }

        /// <summary> 재생 중인 표시를 거절 색으로 바꾼다, 끝난 표시에는 아무것도 하지 않는다 </summary>
        public void MarkRejected()
        {
            if ((_tween == null) || (!_tween.IsPlaying()))
            {
                return;
            }

            _isRejected = true;
            ApplyLayers();
        }

        private float GetProgress() { return _progress; }

        private void SetProgress(float progress)
        {
            _progress = progress;
            ApplyLayers();
        }

        private void ApplyLayers()
        {
            Quaternion billboard = GetBillboardRotation();
            for (int i = 0; i < _layers.Length; ++i)
            {
                if (_layers[i].IsValid)
                {
                    _layers[i].Apply(_progress, billboard, _rejectColor, _isRejected, _properties);
                }
            }
        }

        // 세운 층이 카메라를 보도록 세로축으로만 돌린다. 쿼터뷰 카메라는 씬에 있어 처음 한 번 찾고 사라졌을 때만 다시 찾는다
        private Quaternion GetBillboardRotation()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return Quaternion.identity;
            }

            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(forward, Vector3.up);
        }

        private void HideLayers()
        {
            for (int i = 0; i < _layers.Length; ++i)
            {
                if (_layers[i].IsValid)
                {
                    _layers[i].SetVisible(false);
                }
            }
        }
    }
}
