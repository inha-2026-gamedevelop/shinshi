// Unity
using UnityEngine;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 내 마검사의 섬광 일섬 시전 줌과 검파, 섬광 일섬 적중 흔들림을 카메라에 입힌다. 서버가 이미 보내는 피해 알림만 쓴다 </summary>
    public sealed class CombatCameraFeedback : ClientView
    {
        // 새 네트워크 멤버 없이 소유 플레이어가 받는 피해 알림과 공개 전투 상태(시전 번호, 슬롯, 확정 시각)로 판단한다.
        // 흔들림은 Cinemachine 카메라 오프셋 확장으로, 줌은 쿼터뷰 리그의 시야각 배율로 겹쳐 쓰므로 줌 단계 전환과 부딪치지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_SHAKE_SECONDS  = 0.2f;
        private const float DIRECTION_EPSILON_SQR  = 0.0001f;

        [Header("참조")]
        [SerializeField] private QuarterViewCameraRig _rig;
        [SerializeField] private CinemachineCamera    _camera;

        [Header("섬광 일섬 줌")]
        [SerializeField, Range(0f, 0.6f)] private float _blinkZoomAmount   = 0.2f;
        // 당기고 풀 때의 감쇠 시간, 클수록 부드럽고 느리게 따라간다
        [SerializeField, Min(0.01f)]      private float _zoomInSmooth      = 0.12f;
        [SerializeField, Min(0.01f)]      private float _zoomOutSmooth     = 0.2f;

        [Header("적중 흔들림")]
        [SerializeField, Min(0f)] private float _waveShake        = 0.2f;
        [SerializeField, Min(0f)] private float _blinkShakeScale  = 1.2f;
        [SerializeField, Min(0.01f)] private float _shakeSeconds  = DEFAULT_SHAKE_SECONDS;

        [Header("분류")]
        // 피해 알림 시각과 시전 시각 비교에 두는 여유
        [SerializeField, Min(0f)] private float _classifyMargin = 0.15f;

        private readonly CombatCameraEffects      _effects    = new CombatCameraEffects();
        private readonly MageknightHitClassifier  _classifier = new MageknightHitClassifier();
        private readonly DamageFeedbackReceiver   _receiver   = new DamageFeedbackReceiver();

        private NetworkIdentity         _boundPlayer = null;
        private PlayerInputState        _input       = null;
        private PlayerCombatState       _combat      = null;
        private CinemachineCameraOffset _offset      = null;
        private int                     _lastCastId  = 0;
        private bool                    _isConfigured = false;
        private float                   _blinkHoldSeconds = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_camera != null)
            {
                _offset = _camera.GetComponent<CinemachineCameraOffset>();
                if (_offset == null)
                {
                    _offset = _camera.gameObject.AddComponent<CinemachineCameraOffset>();
                }
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        // Cinemachine이 LateUpdate에서 카메라 상태를 만들기 전에 값을 넣어야 해서 Update에서 계산한다
        private void Update()
        {
            if ((_rig == null) || (_camera == null))
            {
                return;
            }

            EnsureBound();
            if ((_combat == null) || (_combat.PlayerClass != EPlayerClass.Mageknight))
            {
                ClearEffects();
                return;
            }

            if ((!_isConfigured) && (!TryConfigure()))
            {
                return;
            }

            ObserveCast();
            _effects.Tick(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
            _rig.SetCombatZoom(_effects.ZoomScale);
            if (_offset != null)
            {
                _offset.Offset = _effects.ShakeOffset;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 로컬 플레이어는 스폰 뒤에 정해지고 재접속하면 새 개체로 바뀌므로 바뀐 순간에만 다시 묶는다
        private void EnsureBound()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == _boundPlayer)
            {
                return;
            }

            Unbind();
            if (localPlayer == null)
            {
                return;
            }

            _boundPlayer = localPlayer;
            _input       = localPlayer.GetComponent<PlayerInputState>();
            _combat      = localPlayer.GetComponent<PlayerCombatState>();
            if (_input != null)
            {
                _input.DamageFeedbackReceived += OnDamageFeedback;
            }

            _classifier.Reset();
            _effects.Reset();
            _lastCastId = 0;
        }

        private void Unbind()
        {
            if (_input != null)
            {
                _input.DamageFeedbackReceived -= OnDamageFeedback;
            }

            _boundPlayer = null;
            _input       = null;
            _combat      = null;
            ClearEffects();
        }

        private void ClearEffects()
        {
            if (!_effects.IsActive)
            {
                return;
            }

            _effects.Reset();
            _rig.SetCombatZoom(1f);
            if (_offset != null)
            {
                _offset.Offset = Vector3.zero;
            }
        }

        // GameDB는 Awake 시점에 준비되지 않을 수 있어 초기화될 때까지 다음 프레임에 다시 시도한다
        private bool TryConfigure()
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            float waveSeconds  = 0f;
            float blinkSeconds = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_W, out SkillCatalogSO.SkillDefinition wave))
            {
                waveSeconds = (float)MageknightVfxTracker.WaveTravelSeconds(wave.Area.ProjectileSpeed, wave.Area.CastRange);
            }

            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_R, out SkillCatalogSO.SkillDefinition blink))
            {
                blinkSeconds      = blink.Timeline.Active;
                _blinkHoldSeconds = blink.Timeline.Windup + blink.Timeline.Active;
            }

            _classifier.Configure(waveSeconds, blinkSeconds, _classifyMargin);
            _isConfigured = true;
            return true;
        }

        // 새 시전이 공개되면 어느 기술인지 기록하고, 섬광 일섬이면 준비와 발동 동안 화면을 당긴다
        private void ObserveCast()
        {
            int castId = _combat.CastInstanceId;
            if ((castId == _lastCastId) || (_combat.SkillPhase == ESkillPhase.Idle) || (_combat.SkillKind != ESkillRequestKind.Regular))
            {
                return;
            }

            _lastCastId = castId;
            bool isWave  = _combat.SkillSlot == Constants.Combat.SKILL_SLOT_W;
            bool isBlink = _combat.SkillSlot == Constants.Combat.SKILL_SLOT_R;
            _classifier.ObserveCast(isWave, isBlink, castId, _combat.SkillResolveAt);

            if (isBlink)
            {
                _effects.ZoomHold(_blinkZoomAmount, _blinkHoldSeconds, _zoomInSmooth, _zoomOutSmooth, Time.unscaledTimeAsDouble);
            }
        }

        private void OnDamageFeedback(DamageFeedback feedback)
        {
            if ((_input == null) || (!_input.isOwned) || (_combat == null) || (_combat.PlayerClass != EPlayerClass.Mageknight))
            {
                return;
            }

            double now = DamageFeedbackRules.ReceptionTime(NetworkTime.time, NetworkClient.bufferTime);
            if (!_receiver.TryAccept(in feedback, _input.SessionEpoch, now))
            {
                return;
            }

            MageknightHitClassifier.EKind kind = _classifier.Classify(feedback.ServerTime);
            if (kind == MageknightHitClassifier.EKind.Other)
            {
                return;
            }

            float strength = (kind == MageknightHitClassifier.EKind.Blink) ? (_waveShake * _blinkShakeScale) : _waveShake;
            _effects.Kick(ToCameraSpace(feedback.WorldPosition), strength, _shakeSeconds, Time.unscaledTimeAsDouble);
        }

        // 맞은 쪽으로 밀리도록 플레이어에서 적중 지점으로 향하는 방향을 카메라 좌표로 바꾼다
        private Vector3 ToCameraSpace(Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - _boundPlayer.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < DIRECTION_EPSILON_SQR)
            {
                direction = _camera.transform.forward;
            }

            return _camera.transform.InverseTransformDirection(direction.normalized);
        }
    }
}
