// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using TMPro;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>
    /// 전투 결과와 정산 화면. 확정 보상, 확률 보상, 정기 증감을 보여 주고 저장이 끝나기 전에는 귀환할 수 없게 막는다.
    /// 저장 상태는 서버 정산 영수증을 기준으로 호출하는 쪽이 넘긴다
    /// </summary>
    public sealed class ResultScreenView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SUCCESS_TITLE    = "토벌 성공";
        private const string TIMEOUT_TITLE    = "토벌 실패 - 시간 초과";
        private const string ALL_DEAD_TITLE   = "토벌 실패 - 전멸";
        private const string ABANDON_TITLE    = "토벌 중도 포기";
        private const string OTHER_TITLE      = "전투 종료";
        private const string BASE_FORMAT      = "+{0:N0}";
        private const string EXTRA_FORMAT     = "+{0:N0}";
        private const string LOST_FORMAT      = "손실 -{0:N0}";
        private const string RETURN_TEXT      = "귀환";
        private const string WAIT_HINT        = "저장이 끝나면 귀환할 수 있습니다";
        private const string EMPTY_HINT       = "없음";
        private const int    INFINITE_LOOPS   = -1;

        [Header("제목")]
        [SerializeField] private TMP_Text _titleLabel  = null;
        [SerializeField] private TMP_Text _regionLabel = null;

        [Header("정기")]
        [SerializeField] private TMP_Text _baseLabel  = null;
        [SerializeField] private TMP_Text _extraLabel = null;
        [SerializeField] private TMP_Text _lostLabel  = null;

        [Header("보상")]
        [SerializeField] private ItemSlotGridView _confirmedGrid = null;
        [SerializeField] private ItemSlotGridView _chanceGrid    = null;
        [SerializeField] private TMP_Text         _confirmedEmpty = null;
        [SerializeField] private TMP_Text         _chanceEmpty    = null;

        [Header("하단")]
        [SerializeField] private SaveStatusView _saveStatus   = null;
        [SerializeField] private Button         _returnButton = null;
        [SerializeField] private TMP_Text       _returnLabel  = null;
        [SerializeField] private TMP_Text       _hintLabel    = null;

        [Header("등장 연출")]
        [SerializeField] private CanvasGroup   _group      = null;
        [SerializeField] private RectTransform _window     = null;
        [SerializeField] private float         _fadeTime   = 0.25f;
        [SerializeField] private float         _popTime    = 0.45f;
        [SerializeField] private float         _startScale = 0.92f;

        [Header("결과 색")]
        [SerializeField] private Graphic[] _accents          = null;
        [SerializeField] private Color     _successAccent    = Color.yellow;
        [SerializeField] private Color     _failAccent       = Color.gray;
        [SerializeField] private Color     _successParticles = Color.yellow;
        [SerializeField] private Color     _failParticles    = Color.gray;

        [Header("빛 연출")]
        [SerializeField] private Graphic           _glow          = null;
        [SerializeField] private float             _glowMinAlpha  = 0.35f;
        [SerializeField] private float             _glowPulseTime = 1.6f;
        [SerializeField] private UiParticleEmitter _burst         = null;
        [SerializeField] private UiParticleEmitter _ambient       = null;

        private Sequence _intro        = null;
        private Tween    _glowPulse    = null;
        private float    _pop          = 0f;
        private float    _glowMaxAlpha = 1f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>귀환 버튼을 눌렀을 때 알린다. 저장이 끝난 뒤에만 눌린다</summary>
        public event Action ReturnClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 결과를 열 때마다 같은 등장 연출을 반복하므로 한 번 만들어 재사용한다.
            // 정산 중 게임 시간이 멈춰 있어도 보여야 하므로 시간 배율을 무시한다.
            // CanvasGroup.DOFade는 Shinshi.Runtime이 참조하지 못하는 DOTween 모듈에 있어 코어 DOTween.To를 쓴다
            if (_group != null)
            {
                _intro = DOTween.Sequence()
                    .Append(DOTween.To(GetAlpha, SetAlpha, 1f, _fadeTime))
                    .Join(DOTween.To(GetPop, SetPop, 1f, _popTime).SetEase(Ease.OutBack))
                    .SetUpdate(true)
                    .SetAutoKill(false)
                    .Pause()
                    .SetLink(gameObject);
            }

            if (_glow != null)
            {
                _glowMaxAlpha = _glow.color.a;
                _glowPulse = DOTween.To(GetGlowAlpha, SetGlowAlpha, _glowMinAlpha, _glowPulseTime)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(INFINITE_LOOPS, LoopType.Yoyo)
                    .SetUpdate(true)
                    .SetAutoKill(false)
                    .Pause()
                    .SetLink(gameObject);
            }
        }

        private void OnEnable()
        {
            if (_returnButton != null)
            {
                _returnButton.onClick.AddListener(OnReturn);
            }

            if (_glowPulse != null)
            {
                _glowPulse.Play();
            }
        }

        private void OnDisable()
        {
            if (_returnButton != null)
            {
                _returnButton.onClick.RemoveListener(OnReturn);
            }

            if (_glowPulse != null)
            {
                _glowPulse.Pause();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetResult(ResultScreenData data)
        {
            if (data == null)
            {
                return;
            }

            SetText(_titleLabel, ToTitle(data.Reason));
            SetText(_regionLabel, data.RegionName);
            SetText(_baseLabel, string.Format(BASE_FORMAT, data.BaseEssence));
            SetText(_extraLabel, string.Format(EXTRA_FORMAT, data.ExtraEssence));

            if (_lostLabel != null)
            {
                _lostLabel.gameObject.SetActive(data.LostEssence > 0);
                _lostLabel.text = string.Format(LOST_FORMAT, data.LostEssence);
            }

            ApplyItems(_confirmedGrid, _confirmedEmpty, data.ConfirmedItems);
            ApplyItems(_chanceGrid, _chanceEmpty, data.ChanceItems);

            bool success = (data.Reason == ESessionEndReason.Success);
            ApplyAccent(success ? _successAccent : _failAccent);
            PlayParticles(success);
            PlayIntro();
        }

        /// <summary>저장이 끝나기 전에는 귀환 버튼을 막고 안내 글자를 보여 준다</summary>
        public void SetSaveStatus(ESaveStatus status)
        {
            if (_saveStatus != null)
            {
                _saveStatus.SetStatus(status);
            }

            bool saved = (status == ESaveStatus.Saved);

            if (_returnButton != null)
            {
                _returnButton.interactable = saved;
            }

            SetText(_returnLabel, RETURN_TEXT);

            if (_hintLabel != null)
            {
                _hintLabel.gameObject.SetActive(!saved);
                _hintLabel.text = WAIT_HINT;
            }
        }

        private void PlayIntro()
        {
            if (_intro == null)
            {
                return;
            }

            SetAlpha(0f);
            SetPop(0f);
            _intro.Restart();
        }

        // 성공은 터지는 빛과 떠오르는 금빛으로 축하하고, 실패는 차분한 색의 알갱이만 흘려 끝났다는 것만 알린다
        private void PlayParticles(bool success)
        {
            if (_burst != null)
            {
                _burst.Clear();

                if (success)
                {
                    _burst.Play();
                }
            }

            if (_ambient != null)
            {
                _ambient.Tint = success ? _successParticles : _failParticles;
                _ambient.Play();
            }
        }

        // 알파는 각 그래픽에 정해 둔 값을 그대로 두고 색만 바꾼다
        private void ApplyAccent(Color accent)
        {
            if (_accents == null)
            {
                return;
            }

            for (int i = 0; i < _accents.Length; ++i)
            {
                if (_accents[i] == null)
                {
                    continue;
                }

                Color color = accent;
                color.a = _accents[i].color.a;
                _accents[i].color = color;
            }
        }

        private float GetAlpha() { return (_group != null) ? _group.alpha : 0f; }

        private void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = alpha;
            }
        }

        private float GetPop() { return _pop; }

        private void SetPop(float pop)
        {
            _pop = pop;

            if (_window != null)
            {
                _window.localScale = Vector3.one * Mathf.LerpUnclamped(_startScale, 1f, pop);
            }
        }

        private float GetGlowAlpha() { return (_glow != null) ? _glow.color.a : 0f; }

        private void SetGlowAlpha(float alpha)
        {
            if (_glow == null)
            {
                return;
            }

            Color color = _glow.color;
            color.a = Mathf.Min(alpha, _glowMaxAlpha);
            _glow.color = color;
        }

        private static void ApplyItems(ItemSlotGridView grid, TMP_Text emptyLabel, IReadOnlyList<ItemSlotData> items)
        {
            bool has = (items != null) && (items.Count > 0);

            if (grid != null)
            {
                grid.gameObject.SetActive(has);
                grid.SetItems(items);
            }

            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(!has);
                emptyLabel.text = EMPTY_HINT;
            }
        }

        private static string ToTitle(ESessionEndReason reason)
        {
            switch (reason)
            {
                case ESessionEndReason.Success:
                    return SUCCESS_TITLE;
                case ESessionEndReason.Timeout:
                    return TIMEOUT_TITLE;
                case ESessionEndReason.AllDead:
                    return ALL_DEAD_TITLE;
                case ESessionEndReason.Abandon:
                    return ABANDON_TITLE;
                default:
                    return OTHER_TITLE;
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private void OnReturn()
        {
            Action handler = ReturnClicked;

            if (handler != null)
            {
                handler();
            }
        }
    }
}
