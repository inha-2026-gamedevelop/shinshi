// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
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
        private Coroutine _routine = null;

        /****************************************
        *              Properties
        ****************************************/

        public int PendingCount => _queue.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable() { HideImmediate(); }

        private void OnDisable()
        {
            _routine = null;
            _queue.Clear();
        }

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

            if (_routine == null)
            {
                _routine = StartCoroutine(PlayQueue());
            }
        }

        public void Clear()
        {
            _queue.Clear();

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            HideImmediate();
        }

        private IEnumerator PlayQueue()
        {
            while (_queue.Count > 0)
            {
                if (_label != null)
                {
                    _label.text = _queue.Dequeue();
                }

                yield return Fade(0f, 1f, _fadeInTime);
                yield return new WaitForSeconds(_holdTime);
                yield return Fade(1f, 0f, _fadeOutTime);
            }

            _routine = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }

            SetAlpha(to);
        }

        private void HideImmediate() { SetAlpha(0f); }

        private void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = alpha;
            }
        }
    }
}
