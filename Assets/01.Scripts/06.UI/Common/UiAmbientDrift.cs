// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;

namespace Shinshi.UI
{
    /// <summary>
    /// 그래픽 여러 장을 천천히 떠다니며 밝아졌다 흐려지게 해 일렁이는 빛이나 안개처럼 보이게 한다.
    /// 켜져 있는 동안만 돈다
    /// </summary>
    public sealed class UiAmbientDrift : MonoBehaviour
    {
        // 화면 UI 캔버스가 오버레이라 ParticleSystem이 UI 위에 그려지지 않는다. 그래서 UI 그래픽을 DOTween으로 움직인다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private sealed class Mote
        {
            /****************************************
            *                Fields
            ****************************************/

            // 밝아지는 데 절반, 흐려지는 데 절반을 쓴다
            private const float FADE_SPLIT = 0.5f;

            [SerializeField]                private Graphic _graphic  = null;
            [Tooltip("시작 위치에서 움직일 거리(px)")]
            [SerializeField]                private Vector2 _travel   = Vector2.zero;
            [SerializeField, Min(0.1f)]     private float   _duration = 4f;
            [Tooltip("첫 재생만 늦춰 여러 장의 박자를 어긋나게 한다")]
            [SerializeField, Min(0f)]       private float   _delay    = 0f;
            [SerializeField, Range(0f, 1f)] private float   _minAlpha = 0f;
            [SerializeField, Range(0f, 1f)] private float   _maxAlpha = 0.5f;
            [Tooltip("켜면 왕복하고, 끄면 끝에서 처음으로 돌아가 다시 움직인다(떠오르는 입자)")]
            [SerializeField]                private bool    _yoyo     = true;

            private RectTransform _rect     = null;
            private Vector2       _start    = Vector2.zero;
            private Sequence      _sequence = null;

            public bool IsValid => _graphic != null;

            /****************************************
            *                Methods
            ****************************************/

            public void Build()
            {
                _rect  = _graphic.rectTransform;
                _start = _rect.anchoredPosition;
                ResetPose();

                float half = _duration * FADE_SPLIT;
                _sequence = DOTween.Sequence();
                // DOAnchorPos, Graphic.DOFade는 Shinshi.Runtime이 참조하지 못하는 DOTween 모듈에 있어 코어 DOTween.To를 쓴다
                _sequence.Append(DOTween.To(GetPosition, SetPosition, _start + _travel, _duration).SetEase(Ease.InOutSine));
                _sequence.Insert(0f, DOTween.To(GetAlpha, SetAlpha, _maxAlpha, half).SetEase(Ease.InOutSine));
                _sequence.Insert(half, DOTween.To(GetAlpha, SetAlpha, _minAlpha, half).SetEase(Ease.InOutSine));
                _sequence.SetLoops(-1, _yoyo ? LoopType.Yoyo : LoopType.Restart)
                    .SetDelay(_delay)
                    .SetUpdate(true)
                    .SetAutoKill(false)
                    .Pause()
                    .SetLink(_graphic.gameObject);
            }

            public void Play()
            {
                ResetPose();
                _sequence.Restart();
            }

            public void Pause() { _sequence.Pause(); }

            private Vector2 GetPosition() { return _rect.anchoredPosition; }
            private void SetPosition(Vector2 position) { _rect.anchoredPosition = position; }
            private float GetAlpha() { return _graphic.color.a; }

            private void SetAlpha(float alpha)
            {
                Color color = _graphic.color;
                color.a = alpha;
                _graphic.color = color;
            }

            private void ResetPose()
            {
                _rect.anchoredPosition = _start;
                SetAlpha(_minAlpha);
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Mote[] _motes = Array.Empty<Mote>();

        /****************************************
        *              Unity Event
        ****************************************/

        // 켜고 끌 때마다 다시 쓰므로 트윈은 한 번만 만든다
        private void Awake()
        {
            for (int i = 0; i < _motes.Length; ++i)
            {
                if (_motes[i].IsValid)
                {
                    _motes[i].Build();
                }
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < _motes.Length; ++i)
            {
                if (_motes[i].IsValid)
                {
                    _motes[i].Play();
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _motes.Length; ++i)
            {
                if (_motes[i].IsValid)
                {
                    _motes[i].Pause();
                }
            }
        }
    }
}
