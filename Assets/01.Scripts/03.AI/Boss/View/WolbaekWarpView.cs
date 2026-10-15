// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 2페이즈 P07 워프 후 충격파의 표현이다. 서버는 시전 시작 틱에 보스 위치를 착지 위치로 옮기고 같은 틱에 착지 위치로 원형 예고를 알린다(BossWarpBlastPattern).
    // 그래서 복제 위치는 t=0에 이미 착지 위치다. 화면의 모델은 출발 위치에서 공중제비를 이어 가야 하므로 서버 판정 위치와 모델 표시 위치를 분리한다
    //   t=0    착지 위치 원형 예고 시작, 모델은 출발 위치에 고정
    //   t=1.0  분해, 모델 숨김, 모델 위치를 서버 위치로 옮긴다
    //   t=1.15 착지 위치에서 재결합
    //   t=1.4  모델 표시
    //   t=1.5  서버 타격 확정 사건(OnStruck)을 받은 경우에만 충격파를 재생한다. 예정 시각만으로 재생하지 않는다
    // 시험 씬의 t=2.5 출발 위치 복귀는 시험 전용이라 옮기지 않는다, 모델은 착지 위치에 남는다
    // 서버 사건에는 출발 위치가 없다. 시전 직전에 화면에 있던 안정 위치를 쓰고, 알 수 없으면(재접속 등) 분해 없이 착지 위치에 모델을 보인다
    /// <summary> 월백 워프 후 충격파의 분해, 재결합, 착지 예고, 충격파 확정을 서버 시각에 맞춰 호출하는 표현 </summary>
    public sealed class WolbaekWarpView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossView _bossView;
        [SerializeField] private WolbaekWarpPlayer _breakup;
        [SerializeField] private WolbaekWarpPlayer _reform;
        [SerializeField] private WolbaekWarpPlayer _landing;

        private Transform _effectsRoot;
        private int       _castId;
        private bool      _isCasting;
        private bool      _hasDeparture;
        private bool      _didBreakup;
        private bool      _didReform;
        private bool      _didShow;
        private double    _castStart;
        private float     _telegraphSeconds;
        private Vector3   _departure;
        private Vector3   _landed;
        private Bounds    _standingBounds;
        private uint      _breakupId;
        private uint      _reformId;
        private uint      _landingId;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중이거나 효과 잔류가 남아 있어 시계를 계속 전달해야 한다 </summary>
        public bool IsActive => _isCasting || (_breakup != null && _breakup.IsRunning) || (_reform != null && _reform.IsRunning)
            || (_landing != null && _landing.IsRunning);

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_bossView == null) || (_breakup == null) || (_reform == null) || (_landing == null))
            {
                Debug.LogError("[WolbaekWarpView] 보스 뷰 또는 워프 재생기 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            // 워프 재생기는 부모 회전 0, 스케일 1을 요구한다. 보스는 바라보는 방향으로 돌기 때문에 월드 좌표 루트로 옮긴다
            _effectsRoot = new GameObject("WolbaekWarpEffects").transform;
            _breakup.transform.SetParent(_effectsRoot, false);
            _reform.transform.SetParent(_effectsRoot, false);
            _landing.transform.SetParent(_effectsRoot, false);
        }

        private void OnDestroy()
        {
            if (_effectsRoot != null)
            {
                Destroy(_effectsRoot.gameObject);
            }
        }

        private void OnDisable()
        {
            Cancel();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고 시작과 타격 확정 알림을 받는다 </summary>
        public void OnCastPhase(BossTelegraphCast cast, EBossTelegraphPhase phase, double serverNow)
        {
            if ((cast == null) || (!WolbaekMotionCatalog.IsWarp(cast.PatternId)))
            {
                return;
            }

            if (phase == EBossTelegraphPhase.Telegraph)
            {
                Begin(cast, serverNow);
            }
            else if ((phase == EBossTelegraphPhase.Execute) && _isCasting && (_castId == cast.CastId))
            {
                // 서버가 타격을 확정했을 때만 충격파를 재생한다
                _landing.ConfirmStrike(_landingId, cast.ExecuteServerTime, serverNow);
            }
        }

        /// <summary> 시전이 정상 종료되거나 취소됐다, 취소면 남은 효과를 끊는다. 모델은 항상 보이게 하고 서버 위치를 따른다 </summary>
        public void OnCastEnded(int castId, bool isCancelled)
        {
            if ((!_isCasting) || (_castId != castId))
            {
                return;
            }

            ShowModel();
            _isCasting = false;

            if (isCancelled)
            {
                StopEffects();
            }
        }

        /// <summary> 매 프레임 서버 시계를 전달한다 </summary>
        public void Tick(double serverNow)
        {
            if (_isCasting)
            {
                UpdateStages(serverNow);
            }

            if (IsActive)
            {
                _breakup.Tick(_breakupId, serverNow);
                _reform.Tick(_reformId, serverNow);
                _landing.Tick(_landingId, serverNow);
            }
        }

        private void Begin(BossTelegraphCast cast, double serverNow)
        {
            Cancel();

            _castId           = cast.CastId;
            _castStart        = cast.Start.StartServerTime;
            _telegraphSeconds = cast.Start.TelegraphSeconds;
            _landed           = cast.Origin;
            _isCasting        = true;
            _didBreakup       = false;
            _didReform        = false;
            _didShow          = false;
            _breakupId        = 0;
            _reformId         = 0;

            // 착지 위치는 서버가 확정했으므로 t=0에 바로 예고를 시작한다. 늦게 받았으면 서버 시각 기준으로 이어서 그린다
            _landingId = _landing.PlayLandedWarning((ulong)cast.CastId, _landed, cast.Length, _castStart,
                _castStart + _telegraphSeconds, serverNow);

            _hasDeparture = _bossView.TryGetStablePosition(out _departure)
                && (WolbaekMotionPlan.ResolveWarpStage(serverNow - _castStart) == WolbaekMotionPlan.EWarpStage.Somersault);

            if (_hasDeparture)
            {
                _standingBounds = CalculateBounds();
                _bossView.BeginPositionHold(_departure);
            }
            else
            {
                // 분해 시각이 지난 뒤 받았거나 출발 위치를 모르면 모델은 이미 착지 위치에 있어야 한다
                _bossView.SnapToServerPosition();
            }
        }

        // 출발 위치를 알 수 없으면(재접속 등) 분해와 재결합 없이 모델을 착지 위치에 바로 보인다
        private void UpdateStages(double serverNow)
        {
            WolbaekMotionPlan.EWarpStage stage = WolbaekMotionPlan.ResolveWarpStage(serverNow - _castStart);

            if (!_hasDeparture)
            {
                if (!_didShow)
                {
                    ShowModel();
                }

                return;
            }

            if ((!_didBreakup) && (stage >= WolbaekMotionPlan.EWarpStage.Hidden))
            {
                _didBreakup = true;
                _breakupId = _breakup.PlayBody((ulong)_castId, CalculateBounds(), _castStart + WolbaekMotionPlan.WARP_BREAKUP_SECONDS, serverNow);
                SetModelVisible(false);

                // 숨겨진 사이에 모델 위치를 서버 위치로 옮긴다
                _bossView.EndPositionHold(true);
            }

            if ((!_didReform) && (stage >= WolbaekMotionPlan.EWarpStage.Reforming))
            {
                _didReform = true;
                Bounds landedBounds = _standingBounds;
                landedBounds.center += _landed - _departure;
                _reformId = _reform.PlayBody((ulong)_castId, landedBounds, _castStart + WolbaekMotionPlan.WARP_REFORM_SECONDS, serverNow);
            }

            if ((!_didShow) && (stage >= WolbaekMotionPlan.EWarpStage.Shown))
            {
                ShowModel();
            }
        }

        private void ShowModel()
        {
            _didShow = true;
            SetModelVisible(true);
            _bossView.EndPositionHold(true);
        }

        private void SetModelVisible(bool isVisible)
        {
            Renderer[] renderers = _bossView.ModelRenderers;

            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].enabled = isVisible;
            }
        }

        private Bounds CalculateBounds()
        {
            Renderer[] renderers = _bossView.ModelRenderers;
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; ++i)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private void StopEffects()
        {
            if ((_breakup == null) || (_reform == null) || (_landing == null))
            {
                return;
            }

            _breakup.Stop();
            _reform.Stop();
            _landing.Stop();
        }

        /// <summary> 전투를 다시 시작할 때 진행 중이던 워프 표현을 끊고 모델을 보이게 한다 </summary>
        public void Reset()
        {
            Cancel();
        }

        // 진행 중인 시전을 끊고 모델을 보이게 한다. 새 시전이 시작될 때와 비활성화 때 부른다
        private void Cancel()
        {
            if (_isCasting && (_bossView != null))
            {
                ShowModel();
                _isCasting = false;
            }

            StopEffects();
        }
    }
}
