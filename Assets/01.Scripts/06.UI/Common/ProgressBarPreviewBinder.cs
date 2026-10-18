#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>
    /// Dev_ProgressBar 씬에서 로딩과 통신, 시간이 걸리는 행동, 스킬 게이지 막대를 버튼으로 시험한다.
    /// 로딩은 단계 진행, 무한 대기, 실패, 처음부터. 행동은 시작, 취소, 반복. 스킬은 충전, 채널링, 끊기
    /// </summary>
    public sealed class ProgressBarPreviewBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ACTION_SECONDS   = 3f;
        private const float CHARGE_SECONDS   = 1.5f;
        private const float CHANNEL_SECONDS  = 2f;
        private const string LOADING_TITLE   = "마을 입장";
        private const string ACTION_TITLE    = "약초 채집";
        private const string CHARGE_TITLE    = "베기 충전";
        private const string CHANNEL_TITLE   = "신령 소환 유지";
        private const string STEP_FORMAT     = "{0}/{1}";
        private const string CANCELLED_TEXT  = "취소됨";
        private const string FAILED_TEXT     = "서버 응답이 없습니다";
        private const string BROKEN_TEXT     = "끊김";
        private const string LOOP_ON_TEXT    = "반복 켬";
        private const string LOOP_OFF_TEXT   = "반복 끔";

        private static readonly string[] G_LoadingStages =
        {
            "서버에 연결하는 중", "로그인 확인", "캐릭터 목록 받기", "마을 입장 준비", "마을 입장"
        };

        [Header("막대")]
        [SerializeField] private UiProgressBar _loadingBar = null;
        [SerializeField] private UiProgressBar _actionBar  = null;
        [SerializeField] private UiProgressBar _skillBar   = null;

        [Header("로딩과 통신")]
        [SerializeField] private Button _loadingStepButton          = null;
        [SerializeField] private Button _loadingIndeterminateButton = null;
        [SerializeField] private Button _loadingFailButton          = null;
        [SerializeField] private Button _loadingResetButton         = null;

        [Header("시간이 걸리는 행동")]
        [SerializeField] private Button _actionStartButton  = null;
        [SerializeField] private Button _actionCancelButton = null;
        [SerializeField] private Button _actionLoopButton   = null;
        [SerializeField] private TMP_Text _actionLoopLabel = null;

        [Header("스킬 게이지")]
        [SerializeField] private Button _skillChargeButton  = null;
        [SerializeField] private Button _skillChannelButton = null;
        [SerializeField] private Button _skillBreakButton   = null;

        private int  _loadingStage = 0;
        private bool _isActionLoop = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            AddListener(_loadingStepButton, OnLoadingStep);
            AddListener(_loadingIndeterminateButton, OnLoadingIndeterminate);
            AddListener(_loadingFailButton, OnLoadingFail);
            AddListener(_loadingResetButton, OnLoadingReset);
            AddListener(_actionStartButton, OnActionStart);
            AddListener(_actionCancelButton, OnActionCancel);
            AddListener(_actionLoopButton, OnActionLoop);
            AddListener(_skillChargeButton, OnSkillCharge);
            AddListener(_skillChannelButton, OnSkillChannel);
            AddListener(_skillBreakButton, OnSkillBreak);
            if (_actionBar != null)
            {
                _actionBar.Completed += OnActionCompleted;
            }
        }

        private void OnDisable()
        {
            RemoveListener(_loadingStepButton, OnLoadingStep);
            RemoveListener(_loadingIndeterminateButton, OnLoadingIndeterminate);
            RemoveListener(_loadingFailButton, OnLoadingFail);
            RemoveListener(_loadingResetButton, OnLoadingReset);
            RemoveListener(_actionStartButton, OnActionStart);
            RemoveListener(_actionCancelButton, OnActionCancel);
            RemoveListener(_actionLoopButton, OnActionLoop);
            RemoveListener(_skillChargeButton, OnSkillCharge);
            RemoveListener(_skillChannelButton, OnSkillChannel);
            RemoveListener(_skillBreakButton, OnSkillBreak);
            if (_actionBar != null)
            {
                _actionBar.Completed -= OnActionCompleted;
            }
        }

        private void Start()
        {
            OnLoadingReset();
            RefreshLoopLabel();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnLoadingReset()
        {
            _loadingStage = 0;
            if (_loadingBar != null)
            {
                _loadingBar.Begin(LOADING_TITLE);
                _loadingBar.SetStep(0f, StageDetail());
            }
        }

        // 마지막 단계에서 완료로 바꾼다. 이미 끝났거나 실패한 막대는 처음부터 다시 시작한다
        private void OnLoadingStep()
        {
            if (_loadingBar == null)
            {
                return;
            }

            if (!_loadingBar.IsRunning)
            {
                OnLoadingReset();
                return;
            }

            ++_loadingStage;
            if (_loadingStage >= G_LoadingStages.Length)
            {
                _loadingBar.SetTitle(LOADING_TITLE);
                _loadingBar.Complete(null);
                return;
            }

            _loadingBar.SetTitle(G_LoadingStages[_loadingStage]);
            _loadingBar.SetStep((float)_loadingStage / G_LoadingStages.Length, StageDetail());
        }

        private void OnLoadingIndeterminate()
        {
            if ((_loadingBar == null) || (!_loadingBar.IsRunning))
            {
                return;
            }

            _loadingBar.SetIndeterminate(true);
        }

        private void OnLoadingFail()
        {
            if (_loadingBar != null)
            {
                _loadingBar.Fail(FAILED_TEXT);
            }
        }

        private void OnActionStart()
        {
            if (_actionBar != null)
            {
                _actionBar.StartTimed(ACTION_TITLE, ACTION_SECONDS, false);
            }
        }

        private void OnActionCancel()
        {
            _isActionLoop = false;
            RefreshLoopLabel();
            if (_actionBar != null)
            {
                _actionBar.Fail(CANCELLED_TEXT);
            }
        }

        private void OnActionLoop()
        {
            _isActionLoop = !_isActionLoop;
            RefreshLoopLabel();
            if ((_isActionLoop) && (_actionBar != null) && (!_actionBar.IsRunning))
            {
                OnActionStart();
            }
        }

        private void OnActionCompleted()
        {
            if (_isActionLoop)
            {
                OnActionStart();
            }
        }

        private void OnSkillCharge()
        {
            if (_skillBar != null)
            {
                _skillBar.StartTimed(CHARGE_TITLE, CHARGE_SECONDS, false);
            }
        }

        private void OnSkillChannel()
        {
            if (_skillBar != null)
            {
                _skillBar.StartTimed(CHANNEL_TITLE, CHANNEL_SECONDS, true);
            }
        }

        private void OnSkillBreak()
        {
            if (_skillBar != null)
            {
                _skillBar.Fail(BROKEN_TEXT);
            }
        }

        private string StageDetail()
        {
            return string.Format(CultureInfo.InvariantCulture, STEP_FORMAT, _loadingStage, G_LoadingStages.Length);
        }

        private void RefreshLoopLabel()
        {
            if (_actionLoopLabel != null)
            {
                _actionLoopLabel.text = _isActionLoop ? LOOP_ON_TEXT : LOOP_OFF_TEXT;
            }
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
#endif
