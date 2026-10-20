// System
using System;
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 신체 강화의 투자 초안과 총량을 보존하는 재분배를 표시하고 서버 확정을 요청한다 </summary>
    [ExecuteAlways]
    public sealed class TrainingView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   STAT_COUNT    = Constants.Combat.TRAINING_STAT_COUNT;
        private const float PERCENT       = 100f;
        private const int   NO_STAT_INDEX = -1;
        private const int   MIN_QUANTITY  = 1;

        private const string RESPEC_COMMIT_LABEL = "재분배 확정";

        private static readonly string[] G_StatNames = { "공격력", "체력", "방어력", "치명타", "기력" };

        [Header("오각형 선택 (ETrainingStat 순서)")]
        [SerializeField] private Button[]             _statButtons = null;
        [SerializeField] private TMP_Text[]           _stageLabels = null;
        [SerializeField] private TrainingRadarGraphic _radar       = null;

        [Header("선택 항목")]
        [SerializeField] private TMP_Text _selectedTitleText  = null;
        [SerializeField] private TMP_Text _selectedStageText  = null;
        [SerializeField] private TMP_Text _selectedBeforeText = null;
        [SerializeField] private TMP_Text _selectedAfterText  = null;
        [SerializeField] private TMP_Text _quantityText       = null;

        [Header("정기와 배분")]
        [SerializeField] private TMP_Text _essenceText     = null;
        [SerializeField] private TMP_Text _totalInvestText = null;
        [SerializeField] private TMP_Text _poolText        = null;
        [SerializeField] private TMP_Text _costText        = null;
        [SerializeField] private TMP_Text _balanceText     = null;
        [SerializeField] private TMP_Text _statusText      = null;

        [Header("조작")]
        [SerializeField] private Button _minusButton  = null;
        [SerializeField] private Button _plusButton   = null;
        [SerializeField] private Button _trainButton  = null;
        [SerializeField] private Button _revertButton = null;
        [SerializeField] private Button _respecButton = null;
        [SerializeField] private Button _closeButton  = null;

        [Header("개발 미리보기 표시")]
        [SerializeField] private GameObject _previewTag = null;

        private readonly int[]         _confirmed       = new int[STAT_COUNT];
        private readonly int[]         _working         = new int[STAT_COUNT];
        private readonly int[]         _batchTarget     = new int[STAT_COUNT];
        private readonly int[]         _pendingExpected = new int[STAT_COUNT];
        private readonly UnityAction[] _selectActions   = new UnityAction[STAT_COUNT];

        private GrowthConfigSO _growth;
        private Button[]       _boundStatButtons;
        private Button         _boundMinusButton;
        private Button         _boundPlusButton;
        private Button         _boundTrainButton;
        private Button         _boundRevertButton;
        private Button         _boundRespecButton;
        private Button         _boundCloseButton;
        private TMP_Text       _respecButtonLabel;
        private Coroutine      _requestWatcher;
        private Guid           _pendingOperation;
        private EGrowthAction  _pendingAction;
        private long           _revision;
        private long           _pendingRevision;
        private long           _essence;
        private int            _selectedIndex;
        private int            _pool;
        private bool           _respecMode;
        private bool           _hasData;
        private bool           _hasServerSnapshot;
        private bool           _runtimeStarted;
        private bool           _requestInFlight;
        private bool           _pendingAccepted;
        private bool           _pendingSnapshotReady;
        private bool           _awaitingFreshSnapshot;
        private bool           _selectActionsReady;
        private bool           _modernAlternate = true;
        private string         _status = "능력치를 선택하고 강화할 단계를 조절하세요";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _previewMode;
#endif

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 개발 예시 데이터만 바꾸는 화면이면 참이며 일반 출시 빌드에서는 항상 거짓이다 </summary>
        public bool PreviewMode
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return _previewMode;
#else
                return false;
#endif
            }
        }

        /// <summary> 신체 강화(참) · 수련(거짓) 표시 모드. 같은 성장 데이터를 다르게 보여 줄 뿐이다 </summary>
        public bool ModernAlternate => _modernAlternate;

        /****************************************
        *              Public Methods
        ****************************************/

        /// <summary> 시안 레이아웃을 한 번만 적용한다. 여러 번 불러도 같은 결과다 </summary>
        public void EnsureModernLayout()
        {
            TrainingModernLayout.Apply(this);
        }

        /// <summary> 표시 모드만 바꾼다. 서버 데이터 · 초안 · 요청 상태는 그대로다 </summary>
        public void SetModernPresentation(bool alternate)
        {
            _modernAlternate = alternate;
            TrainingModernLayout.Present(this, alternate);
            if ((transform.Find(ModernDarkKit.APPLIED_NAME) != null) && (isActiveAndEnabled))
            {
                RefreshAll();
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            BindButtons();
            if (Application.isPlaying)
            {
                EnsureModernLayout();
            }
        }

        private void OnEnable()
        {
            BindButtons();
            if (!Application.isPlaying)
            {
                RefreshAll();
                return;
            }

            TownGrowthClient.SnapshotChanged  += OnSnapshotChanged;
            TownGrowthClient.ResultReceived   += OnResultReceived;
            TownGrowthClient.WriteLockChanged += OnWriteLockChanged;
            if (_runtimeStarted)
            {
                ReadServerState();
            }
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _runtimeStarted = true;
            if (!GameDB.IsInitialized)
            {
                _growth  = null;
                _hasData = false;
                SetStatus("성장 데이터를 불러오지 못했습니다");
                Debug.LogError("TrainingView: GameDB가 초기화되지 않았다", this);
                return;
            }

            _growth = GameDB.Growth;
            ReadServerState();
        }

        private void OnDisable()
        {
            UnbindButtons();
            TownGrowthClient.SnapshotChanged  -= OnSnapshotChanged;
            TownGrowthClient.ResultReceived   -= OnResultReceived;
            TownGrowthClient.WriteLockChanged -= OnWriteLockChanged;
            if (_requestInFlight)
            {
                // 숨겨진 창은 다음 항목을 자동으로 구매하지 않는다. 재개 전에 서버 상태를 다시 조회한다.
                ClearRequest();
                _awaitingFreshSnapshot = true;
                _hasServerSnapshot    = false;
                _hasData              = false;
            }

            ResetDraft();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> ETrainingStat 순서의 항목을 선택한다 </summary>
        public void SelectStat(int index)
        {
            if ((index < 0) || (index >= STAT_COUNT) || (_requestInFlight))
            {
                return;
            }

            // 수량 최소 1로 넣어 둔 한 단계는 고른 항목을 바꾸면 거둔다. 그 이상 올린 수량은 묶음 강화로 남긴다
            if ((!_respecMode) && (_working[_selectedIndex] == _confirmed[_selectedIndex] + MIN_QUANTITY))
            {
                _working[_selectedIndex] -= MIN_QUANTITY;
            }

            _selectedIndex = index;
            ApplyMinimumQuantity();
            RefreshAll();
        }

        /// <summary> 추가 강화 수량을 늘리거나 재분배 풀에서 선택 항목에 한 단계를 배분한다 </summary>
        public void IncreaseSelected()
        {
            if (!CanEdit())
            {
                return;
            }

            if (_working[_selectedIndex] >= _growth.TrainingStatCap)
            {
                SetStatus("선택한 능력치가 최대 단계입니다");
                return;
            }

            if (_respecMode)
            {
                if (_pool <= 0)
                {
                    SetStatus("배분 대기 단계가 없습니다");
                    return;
                }

                ++_working[_selectedIndex];
                --_pool;
                SetStatus("배분을 마친 뒤 강화로 확정하세요");
                return;
            }

            if (Sum(_working) >= _growth.TrainingTotalCap)
            {
                SetStatus("전체 강화가 최대 단계입니다");
                return;
            }

            ++_working[_selectedIndex];
            SetStatus(DraftCost() > _essence ? "강화에 필요한 정기가 부족합니다" : "강화로 선택한 단계를 확정하세요");
        }

        /// <summary> 추가 강화 수량을 줄인다. 재분배 중에는 선택 항목의 한 단계를 풀로 돌린다 </summary>
        public void DecreaseSelected()
        {
            if (!CanEdit())
            {
                return;
            }

            if (_respecMode)
            {
                ReduceAllocation();
                return;
            }

            if (_working[_selectedIndex] <= _confirmed[_selectedIndex] + MIN_QUANTITY)
            {
                SetStatus("강화 수량은 최소 1단계입니다");
                return;
            }

            --_working[_selectedIndex];
            SetStatus("강화 수량을 한 단계 줄였습니다");
        }

        /// <summary> 선택 항목을 한 단계 줄이며 확정된 단계는 환급 대신 재분배 풀로 돌린다 </summary>
        public void RevertSelected()
        {
            if (!CanEdit())
            {
                return;
            }

            if ((_respecMode) || (_working[_selectedIndex] > _confirmed[_selectedIndex] + MIN_QUANTITY))
            {
                DecreaseSelected();
                return;
            }

            if (_confirmed[_selectedIndex] <= 0)
            {
                return;
            }

            // 구매 초안과 재분배는 함께 확정할 수 없다. 기존 투자를 줄일 때 미구매 단계는 먼저 버린다.
            Array.Copy(_confirmed, _working, STAT_COUNT);
            _respecMode = true;
            ReduceAllocation();
        }

        /// <summary> 전체 확정 단계를 풀로 회수한다. 재분배 중에 다시 누르면 원래 배분으로 돌아간다 </summary>
        public void RedistributeAll()
        {
            if (!CanEdit())
            {
                return;
            }

            if (_respecMode)
            {
                CancelDraft();
                return;
            }

            Array.Clear(_working, 0, STAT_COUNT);
            _pool       = Sum(_confirmed);
            _respecMode = true;
            SetStatus($"능력치를 고르고 +로 {_pool}단계를 다시 나눈 뒤 재분배 확정을 누르세요. 투자한 정기는 반환되지 않습니다");
        }

        /// <summary> 보내지 않은 변경을 취소하고 마지막 서버 확정 또는 개발 예시 상태로 돌아간다 </summary>
        public void CancelDraft()
        {
            if (_requestInFlight)
            {
                return;
            }

            ResetDraft();
            SetStatus("확정 전 변경을 취소했습니다");
        }

        /// <summary> 유효한 강화 또는 재분배를 확정한다. 서버에서는 한 항목씩 응답과 스냅샷을 기다린다 </summary>
        public void Commit()
        {
            if (!CanEdit())
            {
                return;
            }

            if (!TryValidateDraft(out string reason))
            {
                SetStatus(reason);
                return;
            }

            if ((_respecMode) && (SameAllocation(_working, _confirmed)))
            {
                ResetDraft();
                SetStatus("배분이 같아 정기를 사용하지 않았습니다");
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_previewMode)
            {
                if ((TownGrowthClient.HasSnapshot) || (NetworkClient.isConnected) || (_hasServerSnapshot))
                {
                    _previewMode = false;
                    ReadServerState();
                    SetStatus("서버 상태를 확인한 뒤 다시 강화하세요");
                    return;
                }

                _essence -= DraftCost();
                Array.Copy(_working, _confirmed, STAT_COUNT);
                ResetDraft();
                SetStatus("미리보기에서 적용했습니다. 실제 재화와 저장에는 영향이 없습니다");
                return;
            }
#endif

            if ((!Application.isPlaying) || (!_hasServerSnapshot) || (!ClientRoles.IsTown()))
            {
                SetStatus("마을 서버에 연결한 뒤 강화할 수 있습니다");
                return;
            }

            if (GrowthWriteGuard.IsBlockedByOther(_pendingOperation))
            {
                SetStatus(GrowthWriteGuard.OTHER_WINDOW_TEXT);
                return;
            }

            Array.Copy(_working, _batchTarget, STAT_COUNT);
            if (_respecMode)
            {
                SendRespec();
                return;
            }

            SendNextTraining();
        }

        /// <summary> 처리 중에는 창을 유지하며 그 외에는 초안을 취소하고 닫는다 </summary>
        public void Close()
        {
            if (_requestInFlight)
            {
                SetStatus("강화 결과를 확인하는 중입니다");
                return;
            }

            ResetDraft();
            gameObject.SetActive(false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> 실제 스냅샷이 없는 개발 화면에 설정 원본과 저장하지 않는 예시 상태를 주입한다 </summary>
        public void ApplyPreviewSnapshot(long essence, int[] training, GrowthConfigSO config)
        {
            if ((config == null) || (essence < 0) || (!ValidAllocation(training, config)))
            {
                SetStatus("미리보기 데이터와 성장 설정을 확인하세요");
                return;
            }

            if ((_hasServerSnapshot) || (_requestInFlight) || (TownGrowthClient.HasSnapshot)
                || ((Application.isPlaying) && (NetworkClient.isConnected)))
            {
                return;
            }

            _growth      = config;
            _essence     = essence;
            _hasData     = true;
            _previewMode = true;
            Array.Copy(training, _confirmed, STAT_COUNT);
            ResetDraft();
            if (isActiveAndEnabled)
            {
                // 에디터에서 AddComponent가 직렬화 참조 주입보다 먼저 Awake를 부를 수 있다.
                BindButtons();
            }

            SetStatus(string.Empty);
        }
#endif

        private bool CanEdit()
        {
            return (_growth != null) && (_hasData) && (!_requestInFlight) && (!_awaitingFreshSnapshot);
        }

        private void ReduceAllocation()
        {
            if (_working[_selectedIndex] <= 0)
            {
                return;
            }

            --_working[_selectedIndex];
            _pool = Sum(_confirmed) - Sum(_working);
            SetStatus("회수한 단계를 다시 배분하세요. 투자한 정기는 반환되지 않습니다");
        }

        private void ReadServerState()
        {
            if (_growth == null)
            {
                RefreshAll();
                return;
            }

            if (_awaitingFreshSnapshot)
            {
                TownGrowthClient.RequestSnapshot();
                SetStatus("서버의 최신 강화 상태를 확인하고 있습니다");
                return;
            }

            if (TownGrowthClient.HasSnapshot)
            {
                OnSnapshotChanged(TownGrowthClient.Snapshot);
                return;
            }

            if (NetworkClient.isConnected)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _previewMode = false;
#endif
                _hasData = false;
                TownGrowthClient.RequestSnapshot();
                SetStatus("서버의 강화 상태를 기다리고 있습니다");
                return;
            }

            SetStatus(PreviewMode
                ? string.Empty
                : "마을 서버에 연결하면 강화 상태를 확인할 수 있습니다");
        }

        private bool TryValidateDraft(out string reason)
        {
            reason = string.Empty;
            if (!ValidAllocation(_working, _growth))
            {
                reason = "강화 단계가 항목 또는 전체 상한을 넘었습니다";
                return false;
            }

            int confirmedTotal = Sum(_confirmed);
            int workingTotal   = Sum(_working);
            if ((_respecMode) && ((_pool != 0) || (workingTotal != confirmedTotal)))
            {
                reason = $"배분 대기 {_pool}단계가 남았습니다. 능력치를 고르고 +로 모두 나눈 뒤 재분배 확정을 누르세요";
                return false;
            }

            if (!_respecMode)
            {
                for (int i = 0; i < STAT_COUNT; ++i)
                {
                    if (_working[i] < _confirmed[i])
                    {
                        reason = "기존 단계 변경은 재분배로 확정해야 합니다";
                        return false;
                    }
                }

                if (workingTotal == confirmedTotal)
                {
                    reason = "강화할 단계를 선택하세요";
                    return false;
                }
            }

            if (DraftCost() > _essence)
            {
                reason = "강화에 필요한 정기가 부족합니다";
                return false;
            }

            return true;
        }

        private long DraftCost()
        {
            if (_growth == null)
            {
                return 0;
            }

            int confirmedTotal = Sum(_confirmed);
            if (_respecMode)
            {
                return SameAllocation(_working, _confirmed) ? 0 : _growth.GetRespecCost(confirmedTotal);
            }

            long cost = 0;
            int workingTotal = Sum(_working);
            for (int step = confirmedTotal + 1; step <= workingTotal; ++step)
            {
                cost += _growth.GetTrainingStepCost(step);
            }

            return cost;
        }

        private void SendRespec()
        {
            Array.Copy(_batchTarget, _pendingExpected, STAT_COUNT);
            BeginRequest(EGrowthAction.TrainingRespec);
            _pendingOperation = TownGrowthClient.RespecTraining((int[])_batchTarget.Clone());
            AfterSend();
        }

        private void SendNextTraining()
        {
            if ((!Application.isPlaying) || (!_hasServerSnapshot) || (!ClientRoles.IsTown()))
            {
                ResetDraft();
                SetStatus("서버 연결이 끊겨 남은 강화를 중단했습니다");
                return;
            }

            int index = NO_STAT_INDEX;
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                if (_batchTarget[i] > _confirmed[i])
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                ResetDraft();
                SetStatus("강화를 적용했습니다");
                return;
            }

            Array.Copy(_batchTarget, _working, STAT_COUNT);
            if (!TryValidateDraft(out string reason))
            {
                ResetDraft();
                SetStatus(reason);
                return;
            }

            Array.Copy(_confirmed, _pendingExpected, STAT_COUNT);
            _pendingExpected[index] = _batchTarget[index];
            BeginRequest(EGrowthAction.Training);
            _pendingOperation = TownGrowthClient.Train(index, _batchTarget[index] - _confirmed[index]);
            AfterSend();
        }

        private void BeginRequest(EGrowthAction action)
        {
            _pendingAction        = action;
            _pendingRevision      = _revision;
            _requestInFlight      = true;
            _pendingAccepted      = false;
            _pendingSnapshotReady = false;
            _pendingOperation     = Guid.Empty;
        }

        private void AfterSend()
        {
            if (_pendingOperation == Guid.Empty)
            {
                ClearRequest();
                SetStatus("서버에 연결되지 않아 강화를 보낼 수 없습니다");
                return;
            }

            _requestWatcher = StartCoroutine(CoWatchRequest());
            SetStatus("강화 결과를 확인하고 있습니다");
        }

        private IEnumerator CoWatchRequest()
        {
            float deadline = Time.realtimeSinceStartup + Constants.Network.API_TIMEOUT_SECONDS;
            while (_requestInFlight)
            {
                if ((!NetworkClient.isConnected) || (Time.realtimeSinceStartup >= deadline))
                {
                    _requestWatcher = null;
                    StopBatchAndRefresh("응답을 확인하지 못해 남은 강화를 중단했습니다. 서버 상태를 다시 확인합니다");
                    yield break;
                }

                yield return null;
            }

            _requestWatcher = null;
        }

        private void OnSnapshotChanged(GrowthSnapshotMessage message)
        {
            if ((!Application.isPlaying) || (_growth == null))
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _previewMode = false;
#endif
            if ((message.Essence < 0) || (!ValidAllocation(message.Training, _growth)) || (message.LoadoutRevision < 0)
                || ((GameDB.IsInitialized) && (!string.Equals(message.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal))))
            {
                _hasData = false;
                SetStatus("서버 성장 상태와 설정이 맞지 않아 강화를 중단했습니다");
                return;
            }

            if ((_hasServerSnapshot) && (message.LoadoutRevision < _revision))
            {
                return;
            }

            _essence               = message.Essence;
            _revision              = message.LoadoutRevision;
            _hasData               = true;
            _hasServerSnapshot     = true;
            _awaitingFreshSnapshot = false;
            Array.Copy(message.Training, _confirmed, STAT_COUNT);
            if (_requestInFlight)
            {
                // 서버는 결과 메시지보다 스냅샷을 먼저 보낸다. 둘 다 확인하기 전에는 다음 항목을 보내지 않는다.
                _pendingSnapshotReady = (_revision > _pendingRevision) && (SameAllocation(_confirmed, _pendingExpected));
                TryFinishRequest();
                RefreshAll();
                return;
            }

            ResetDraft();
            SetStatus("능력치를 선택하고 강화할 단계를 조절하세요");
        }

        private void OnResultReceived(GrowthResultMessage message)
        {
            if ((!_requestInFlight) || (message.OperationId != _pendingOperation) || (message.Action != _pendingAction))
            {
                return;
            }

            if (!message.IsAccepted)
            {
                string reason = RejectText.Describe(message.Reason);
                StopBatchAndRefresh(string.IsNullOrEmpty(reason) ? "강화를 처리할 수 없습니다" : reason);
                return;
            }

            _pendingAccepted = true;
            if (_pendingSnapshotReady)
            {
                TryFinishRequest();
                return;
            }

            // 누락되거나 다른 작업의 스냅샷이면 새 상태를 확인한다. 미확인 상태에서 구매를 이어 가지 않는다.
            TownGrowthClient.RequestSnapshot();
            SetStatus("서버가 확정한 강화 상태를 확인하고 있습니다");
        }

        private void TryFinishRequest()
        {
            if ((!_requestInFlight) || (!_pendingAccepted) || (!_pendingSnapshotReady))
            {
                return;
            }

            bool wasRespec = _pendingAction == EGrowthAction.TrainingRespec;
            ClearRequest();
            if (wasRespec)
            {
                ResetDraft();
                SetStatus("재분배를 적용했습니다");
                return;
            }

            SendNextTraining();
        }

        private void StopBatchAndRefresh(string status)
        {
            ClearRequest();
            ResetDraft();
            _hasData               = false;
            _hasServerSnapshot     = false;
            _awaitingFreshSnapshot = true;
            if (NetworkClient.isConnected)
            {
                TownGrowthClient.RequestSnapshot();
            }

            SetStatus(status);
        }

        // 묶음 중단이나 시간 초과로 끝내면 서버 상태를 다시 읽고 새 요청으로 시작하므로 공용 잠금도 푼다.
        // 결과가 온 요청이면 잠금이 이미 풀려 아무 일도 없다
        private void ClearRequest()
        {
            if (_requestWatcher != null)
            {
                StopCoroutine(_requestWatcher);
                _requestWatcher = null;
            }

            TownGrowthClient.ReleaseWrite(_pendingOperation);
            _requestInFlight      = false;
            _pendingAccepted      = false;
            _pendingSnapshotReady = false;
            _pendingOperation     = Guid.Empty;
        }

        private void OnWriteLockChanged()
        {
            if ((Application.isPlaying) && (!_requestInFlight))
            {
                RefreshAll();
            }
        }

        private void ResetDraft()
        {
            Array.Copy(_confirmed, _working, STAT_COUNT);
            _pool       = 0;
            _respecMode = false;
            ApplyMinimumQuantity();
        }

        // 강화 수량은 1단계부터 시작한다. 항목 또는 전체 상한에 닿아 더 올릴 수 없으면 0으로 둔다
        private void ApplyMinimumQuantity()
        {
            if ((_respecMode) || (_growth == null) || (!_hasData) || (_working[_selectedIndex] != _confirmed[_selectedIndex]))
            {
                return;
            }

            if ((_working[_selectedIndex] + MIN_QUANTITY > _growth.TrainingStatCap)
                || (Sum(_working) + MIN_QUANTITY > _growth.TrainingTotalCap))
            {
                return;
            }

            _working[_selectedIndex] += MIN_QUANTITY;
        }

        private void SetStatus(string status)
        {
            _status = status;
            RefreshAll();
        }

        private void RefreshAll()
        {
            bool ready         = (_growth != null) && (_hasData);
            bool canEdit       = CanEdit();
            int statCap        = (_growth != null) ? _growth.TrainingStatCap : 0;
            int totalCap       = (_growth != null) ? _growth.TrainingTotalCap : 0;
            int workingTotal   = Sum(_working);
            int confirmedTotal = Sum(_confirmed);
            long cost          = ready ? DraftCost() : 0;

            if (_stageLabels != null)
            {
                for (int i = 0; i < Math.Min(_stageLabels.Length, STAT_COUNT); ++i)
                {
                    SetText(_stageLabels[i], ready ? $"{_working[i]} / {statCap}" : "-");
                }
            }

            if (_radar != null)
            {
                _radar.SetValues(_working, statCap, _selectedIndex);
            }

            SetText(_selectedTitleText, G_StatNames[_selectedIndex]);
            string stage = _working[_selectedIndex] == _confirmed[_selectedIndex]
                ? $"{_working[_selectedIndex]} / {statCap} 단계"
                : $"{_confirmed[_selectedIndex]} -> {_working[_selectedIndex]} 단계";
            SetText(_selectedStageText, ready ? stage : "서버 상태 대기");
            SetText(_selectedBeforeText, ready ? EffectSummary(_selectedIndex, _confirmed[_selectedIndex]) : "-");
            SetText(_selectedAfterText, ready ? EffectSummary(_selectedIndex, _working[_selectedIndex]) : "-");
            SetText(_quantityText, (_respecMode ? _working[_selectedIndex] : _working[_selectedIndex] - _confirmed[_selectedIndex]).ToString());
            SetText(_essenceText, ready ? $"{_essence:N0}" : "-");
            SetText(_totalInvestText, ready ? $"전체 강화 {workingTotal} / {totalCap}" : "전체 강화 -");
            SetText(_poolText, _respecMode ? $"배분 대기 {_pool} 단계" : "");
            SetText(_costText, ready ? $"필요 정기 {cost:N0}" : "필요 정기 -");
            SetText(_balanceText, ready
                ? (cost <= _essence ? $"강화 후 정기 {_essence - cost:N0}" : $"정기 부족 {cost - _essence:N0}")
                : "강화 후 정기 -");
            SetText(_statusText, _status);
            SetText(_respecButtonLabel, _respecMode ? "재분배 취소" : "전체 재분배");

            bool canIncrease = (ready) && (_working[_selectedIndex] < statCap)
                && (_respecMode ? (_pool > 0) : (workingTotal < totalCap));
            bool canDecrease = _respecMode
                ? _working[_selectedIndex] > 0
                : _working[_selectedIndex] > _confirmed[_selectedIndex] + MIN_QUANTITY;
            bool canRevert = _respecMode
                ? _working[_selectedIndex] > 0
                : (_confirmed[_selectedIndex] > 0) || canDecrease;
            bool hasDraft = (_respecMode) || (workingTotal > confirmedTotal);
            // 재분배 중에는 배분이 남아도 누를 수 있게 둔다. 누르면 남은 단계를 안내해 무엇을 해야 하는지 보이게 한다
            bool canCommit = (ready) && (hasDraft) && (cost <= _essence)
                && ((!Application.isPlaying) || (!GrowthWriteGuard.IsBlockedByOther(_pendingOperation)));
            SetInteractable(_minusButton, (canEdit) && (canDecrease));
            SetInteractable(_plusButton, (canEdit) && (canIncrease));
            SetInteractable(_trainButton, (canEdit) && (canCommit));
            SetInteractable(_revertButton, (canEdit) && (canRevert));
            SetInteractable(_respecButton, canEdit);
            SetInteractable(_closeButton, !_requestInFlight);
            if (_statButtons != null)
            {
                for (int i = 0; i < _statButtons.Length; ++i)
                {
                    SetInteractable(_statButtons[i], (canEdit) && (i < STAT_COUNT));
                }
            }

            if (_previewTag != null)
            {
                _previewTag.SetActive(PreviewMode);
            }

            TrainingModernLayout.Refresh(this, _selectedIndex, _confirmed[_selectedIndex], _working[_selectedIndex], _working, statCap, workingTotal, totalCap);
            if ((_respecMode) && (_trainButton != null))
            {
                TMP_Text label = _trainButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = (_pool > 0) ? $"{RESPEC_COMMIT_LABEL} ({_pool} 남음)" : RESPEC_COMMIT_LABEL;
                }
            }
        }

        private string EffectSummary(int index, int steps)
        {
            float gain = _growth.GetTrainingStat((ETrainingStat)index).GainPerStep * steps;
            switch ((ETrainingStat)index)
            {
                case ETrainingStat.CritChance:
                    return $"치명타 확률 +{gain * PERCENT:0.#}%p";
                case ETrainingStat.MaxEnergy:
                    return $"최대 기력 +{gain:0.#}";
                default:
                    return $"{G_StatNames[index]} +{gain * PERCENT:0.#}%";
            }
        }

        private void BindButtons()
        {
            UnbindButtons();
            if (!_selectActionsReady)
            {
                for (int i = 0; i < STAT_COUNT; ++i)
                {
                    int index = i;
                    _selectActions[i] = () => SelectStat(index);
                }

                _selectActionsReady = true;
            }

            if (_statButtons != null)
            {
                _boundStatButtons = (Button[])_statButtons.Clone();
                for (int i = 0; i < Math.Min(_boundStatButtons.Length, STAT_COUNT); ++i)
                {
                    AddListener(_boundStatButtons[i], _selectActions[i]);
                }
            }

            _boundMinusButton  = _minusButton;
            _boundPlusButton   = _plusButton;
            _boundTrainButton  = _trainButton;
            _boundRevertButton = _revertButton;
            _boundRespecButton = _respecButton;
            _boundCloseButton  = _closeButton;
            AddListener(_boundMinusButton, DecreaseSelected);
            AddListener(_boundPlusButton, IncreaseSelected);
            AddListener(_boundTrainButton, Commit);
            AddListener(_boundRevertButton, RevertSelected);
            AddListener(_boundRespecButton, RedistributeAll);
            AddListener(_boundCloseButton, Close);
            _respecButtonLabel = (_respecButton != null) ? _respecButton.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private void UnbindButtons()
        {
            if (_boundStatButtons != null)
            {
                for (int i = 0; i < Math.Min(_boundStatButtons.Length, STAT_COUNT); ++i)
                {
                    RemoveListener(_boundStatButtons[i], _selectActions[i]);
                }

                _boundStatButtons = null;
            }

            RemoveListener(_boundMinusButton, DecreaseSelected);
            RemoveListener(_boundPlusButton, IncreaseSelected);
            RemoveListener(_boundTrainButton, Commit);
            RemoveListener(_boundRevertButton, RevertSelected);
            RemoveListener(_boundRespecButton, RedistributeAll);
            RemoveListener(_boundCloseButton, Close);
        }

        private static bool ValidAllocation(int[] values, GrowthConfigSO config)
        {
            if ((config == null) || (values == null) || (values.Length != STAT_COUNT))
            {
                return false;
            }

            long total = 0;
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                if ((values[i] < 0) || (values[i] > config.TrainingStatCap))
                {
                    return false;
                }

                total += values[i];
            }

            return total <= config.TrainingTotalCap;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; ++i)
            {
                total += values[i];
            }

            return total;
        }

        private static bool SameAllocation(int[] first, int[] second)
        {
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private static void AddListener(Button button, UnityAction action)
        {
            if ((button != null) && (action != null))
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if ((button != null) && (action != null))
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
