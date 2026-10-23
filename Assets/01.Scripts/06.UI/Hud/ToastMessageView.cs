// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using TMPro;

namespace Shinshi.UI
{
    /// <summary>짧은 안내 문구를 순서대로 페이드 인, 유지, 페이드 아웃으로 한 개씩 표시한다</summary>
    public sealed class ToastMessageView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_QUEUE_COUNT = 8;

        [Header("표시")]
        [SerializeField] private CanvasGroup _group = null;
        [SerializeField] private TMP_Text    _label = null;

        [Header("시간(초)")]
        [SerializeField] private float _fadeInTime  = 0.15f;
        [SerializeField] private float _holdTime    = 2f;
        [SerializeField] private float _fadeOutTime = 0.3f;

        private readonly Queue<string> _queue = new Queue<string>();

        private Sequence _sequence  = null;
        private bool     _isPlaying = false;

        /****************************************
        *              Properties
        ****************************************/

        public int PendingCount => _queue.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_group == null)
            {
                return;
            }

            // 문구마다 같은 페이드 인, 유지, 페이드 아웃을 반복하므로 한 번 만들어 재사용한다.
            // 일시 정지 화면에서도 안내가 사라져야 하므로 시간 배율을 무시한다.
            // CanvasGroup.DOFade는 Shinshi.Runtime이 참조하지 못하는 DOTween 모듈에 있어 코어 DOTween.To를 쓴다
            _sequence = DOTween.Sequence()
                .Append(DOTween.To(GetAlpha, SetAlpha, 1f, _fadeInTime))
                .AppendInterval(_holdTime)
                .Append(DOTween.To(GetAlpha, SetAlpha, 0f, _fadeOutTime))
                .SetUpdate(true)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject)
                .OnComplete(PlayNext);
        }

        private void OnEnable() { HideImmediate(); }

        private void OnDisable() { Clear(); }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>대기열이 가득 차면 가장 오래된 문구를 버리고 새 문구를 넣는다</summary>
        public void Show(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (_queue.Count >= MAX_QUEUE_COUNT)
            {
                _queue.Dequeue();
            }

            _queue.Enqueue(message);

            if (!_isPlaying)
            {
                PlayNext();
            }
        }

        public void Clear()
        {
            _queue.Clear();
            _isPlaying = false;

            if (_sequence != null)
            {
                _sequence.Pause();
            }

            HideImmediate();
        }

        private void PlayNext()
        {
            if ((_sequence == null) || (_queue.Count == 0))
            {
                _isPlaying = false;
                return;
            }

            if (_label != null)
            {
                _label.text = _queue.Dequeue();
            }
            else
            {
                _queue.Dequeue();
            }

            _isPlaying = true;
            HideImmediate();
            _sequence.Restart();
            UiSound.Play(EUiSoundGroup.Toast, EUiSoundAction.Open);
        }

        private void HideImmediate() { SetAlpha(0f); }

        private float GetAlpha() { return (_group != null) ? _group.alpha : 0f; }

        private void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = alpha;
            }
        }
    }
}
