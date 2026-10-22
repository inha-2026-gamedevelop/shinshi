// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.AI
{
    /// <summary> 일반 적 네트워크 개체의 클라이언트 표현, 종류별 원본 모델과 애니메이션, 피격 깜빡임, 공격 예고를 그린다. 위치는 NetworkTransform이 맡는다 </summary>
    [RequireComponent(typeof(EnemyNetState))]
    public sealed class EnemyNetworkView : ClientView
    {
        // 서버가 보낸 EnemyNetState만 읽고 판정과 위치는 건드리지 않는다.
        // 모델은 프리팹에 종류별로 함께 들어 있고 종류가 정해지면 해당 모델만 켠다. 모델 배열이 비면 역할 색 캡슐 대체 표현으로 그린다.
        // 풀에서 다시 켜질 때마다 이전 적의 예고, 깜빡임, 애니메이터 상태를 지워 다음 적에게 남지 않게 한다

        /****************************************
        *                Fields
        ****************************************/

        private const float TELEGRAPH_END_GRACE = 0.15f;
        private const float FLASH_SECONDS       = 0.12f;
        private const float DEAD_SCALE          = 0.2f;
        private const float LINE_WIDTH          = 0.08f;
        private const float MOVING_SPEED        = 0.2f;
        private const float FLASH_STRENGTH      = 0.5f;
        private const float DASH_INFER_SPEED    = 8f;    // m/s, 돌진형이 걷는 속도보다 훨씬 빠른 이동
        private const float DASH_HOLD_SECONDS   = 0.2f;  // 돌진 근거가 끊겨도 이 시간은 달리기를 유지한다
        private const float BRACE_RELEASE_SECONDS = 0.3f; // 예고도 돌진도 아닌데 준비 자세에 남아 있으면 풀어 주는 시간
        private const float RELEASE_FADE_SECONDS  = 0.1f;
        private const int   NO_MODEL            = -1;

        // 모델 배열 순서, 프리팹 빌더가 같은 순서로 채운다
        public const int MODEL_BEAST   = 0;
        public const int MODEL_VENDIGO = 1;

        private static bool s_quitting;

        private static readonly Color FLASH_TINT = Color.red;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int COLOR_ID      = Shader.PropertyToID("_Color");

        private static readonly int ANIM_MOVING  = Animator.StringToHash("Moving");
        private static readonly int ANIM_DEAD    = Animator.StringToHash("Dead");
        private static readonly int ANIM_ATTACK  = Animator.StringToHash("Attack");
        private static readonly int ANIM_DAMAGE  = Animator.StringToHash("Damage");
        private static readonly int ANIM_DASHING = Animator.StringToHash("Dashing");

        // 돌진형 컨트롤러의 상태 이름: 예고의 준비 자세(Attack)와 평소 자세(Idle)
        private static readonly int STATE_ATTACK = Animator.StringToHash("Attack");
        private static readonly int STATE_IDLE   = Animator.StringToHash("Idle");

        [SerializeField] private Transform    _visual;
        [SerializeField] private Renderer     _visualRenderer; // 모델이 없을 때만 쓰는 대체 캡슐
        [SerializeField] private LineRenderer _telegraphLine;

        [Header("원본 모델")]
        [SerializeField] private GameObject[] _modelRoots;
        [SerializeField] private Animator[]   _modelAnimators;

        private readonly Vector3[] _points = new Vector3[EnemyTelegraphGeometry.MAX_POINTS];

        private EnemyNetState         _state;
        private MaterialPropertyBlock _block;
        private Renderer[][]          _modelRenderers;
        private Color[][]             _flashColors;
        private bool[]                _hasDashingParam;
        private Animator              _animator;
        private Vector3               _visualBaseScale = Vector3.one;
        private Vector3               _lastPosition;
        private EEnemyRole            _appliedRole     = (EEnemyRole)(-1);
        private EEnemyState           _lastBehavior;
        private int                   _activeModel     = NO_MODEL;
        private float                 _lastHealth;
        private float                 _flashUntil;
        private bool                  _flashApplied;
        private bool                  _hasPose;
        private bool                  _animatorDirty;
        private bool                  _corpseLaunched;
        private bool                  _seenAlive;
        private float                 _dashUntil;
        private float                 _braceReleaseAt = -1f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            s_quitting = false;
            _state = GetComponent<EnemyNetState>();
            _block = new MaterialPropertyBlock();

            if (_visual != null)
            {
                _visualBaseScale = _visual.localScale;
            }

            CacheModels();
            HideTelegraph();
        }

        private void OnEnable()
        {
            if (IsServerBuild)
            {
                return;
            }

            _appliedRole   = (EEnemyRole)(-1);
            _flashUntil    = 0f;
            _lastHealth    = (_state != null) ? _state.Health : 0f;
            _hasPose       = false;
            _lastBehavior  = EEnemyState.Idle;
            _animatorDirty = true;
            _corpseLaunched = false;
            _seenAlive      = false;
            _dashUntil      = 0f;
            _braceReleaseAt = -1f;
            ClearFlash();
            HideTelegraph();
        }

        private void OnApplicationQuit()
        {
            s_quitting = true;
        }

        private void OnDisable()
        {
            HideTelegraph();
            ProbeLog($"OnDisable corpseLaunched={_corpseLaunched} seenAlive={_seenAlive}");
            LaunchOnRemoval();
        }

        private void OnDestroy()
        {
            ProbeLog($"OnDestroy corpseLaunched={_corpseLaunched} seenAlive={_seenAlive}");
            LaunchOnRemoval();
        }

        private void Update()
        {
            if (_state == null)
            {
                return;
            }

            ApplyHealthFlash();
            ApplyCorpse();
            ApplyRole();
            ApplyAnimation();
            ApplyFlash();
            ApplyTelegraph();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 모델마다 렌더러와 애니메이터 파라미터를 한 번만 모은다. 깜빡임 색은 원본 재질 색에서 만들어 원본 재질을 바꾸지 않는다
        private void CacheModels()
        {
            int count = (_modelRoots != null) ? _modelRoots.Length : 0;
            _modelRenderers  = new Renderer[count][];
            _flashColors     = new Color[count][];
            _hasDashingParam = new bool[count];

            List<Renderer> renderers = new List<Renderer>();
            List<Color>    colors    = new List<Color>();

            for (int i = 0; i < count; ++i)
            {
                renderers.Clear();
                colors.Clear();

                if (_modelRoots[i] != null)
                {
                    Renderer[] all = _modelRoots[i].GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < all.Length; ++r)
                    {
                        Material material = all[r].sharedMaterial;
                        if ((material != null) && (material.HasProperty(BASE_COLOR_ID)))
                        {
                            renderers.Add(all[r]);
                            colors.Add(Color.Lerp(material.GetColor(BASE_COLOR_ID), FLASH_TINT, FLASH_STRENGTH));
                        }
                    }
                }

                _modelRenderers[i] = renderers.ToArray();
                _flashColors[i]    = colors.ToArray();

                Animator animator = ((_modelAnimators != null) && (i < _modelAnimators.Length)) ? _modelAnimators[i] : null;
                if ((animator != null) && (animator.runtimeAnimatorController != null))
                {
                    AnimatorControllerParameter[] parameters = animator.parameters;
                    for (int p = 0; p < parameters.Length; ++p)
                    {
                        if (parameters[p].nameHash == ANIM_DASHING)
                        {
                            _hasDashingParam[i] = true;
                            break;
                        }
                    }
                }
            }
        }

        // 근접, 원거리, 정예는 전용 모델이 아직 없어 Beast를 함께 쓴다. 돌진형만 Vendigo다
        private static int ModelIndexOf(EEnemyRole role)
        {
            return (role == EEnemyRole.Charger) ? MODEL_VENDIGO : MODEL_BEAST;
        }

        private void ApplyRole()
        {
            EEnemyRole role = _state.Role;
            bool dead = _state.LifeState != EParticipantLifeState.Alive;
            bool hasModels = (_modelRoots != null) && (_modelRoots.Length > 0);

            if (_appliedRole != role)
            {
                _appliedRole = role;
                if (hasModels)
                {
                    SelectModel(ModelIndexOf(role));
                }
                else
                {
                    SetFallbackColor(EnemyRoleColors.Get(role));
                }
            }

            if (_visual != null)
            {
                // 모델은 죽음 애니메이션이 있어 줄이지 않고, 대체 캡슐만 줄인다
                float scale = EnemyRoleColors.GetScale(role) * ((dead && (!hasModels)) ? DEAD_SCALE : 1f);
                _visual.localScale = _visualBaseScale * scale;
            }
        }

        private void SelectModel(int index)
        {
            if ((index == _activeModel) || (index < 0) || (index >= _modelRoots.Length))
            {
                return;
            }

            ClearFlash();
            for (int i = 0; i < _modelRoots.Length; ++i)
            {
                if (_modelRoots[i] != null)
                {
                    _modelRoots[i].SetActive(i == index);
                }
            }

            _activeModel   = index;
            _animator      = ((_modelAnimators != null) && (index < _modelAnimators.Length)) ? _modelAnimators[index] : null;
            _animatorDirty = true;
        }

        // 서버 상태와 실제 이동량으로 애니메이터를 움직인다. 서버가 상태를 정하므로 여기서는 값을 읽어 파라미터로 옮길 뿐이다
        private void ApplyAnimation()
        {
            EEnemyState behavior = _state.Behavior;
            Vector3 position = transform.position;

            if ((_animator == null) || (!_animator.isActiveAndEnabled))
            {
                _lastPosition = position;
                _lastBehavior = behavior;
                _hasPose      = true;
                return;
            }

            if (_animatorDirty)
            {
                // 풀에서 재사용된 개체나 새로 켠 모델의 이전 동작을 지운다
                _animator.Rebind();
                _animator.Update(0f);
                _animatorDirty = false;
                _hasPose       = false;
                _lastBehavior  = EEnemyState.Idle;
            }

            float dt     = Time.deltaTime;
            float speed  = (_hasPose && (dt > 0f)) ? ((position - _lastPosition).magnitude / dt) : 0f;
            bool  dead   = (_state.LifeState != EParticipantLifeState.Alive) || (behavior == EEnemyState.Dead);
            bool  moving = (!dead) && (speed > MOVING_SPEED);

            _animator.SetBool(ANIM_MOVING, moving);
            _animator.SetBool(ANIM_DEAD, dead);
            if ((_activeModel >= 0) && _hasDashingParam[_activeModel])
            {
                bool dashing = ReadDashing(behavior, speed, dead);
                _animator.SetBool(ANIM_DASHING, dashing);
                ReleaseStuckBrace(behavior, dashing);
            }

            if ((!dead) && (behavior == EEnemyState.Telegraph) && (_lastBehavior != EEnemyState.Telegraph))
            {
                _animator.SetTrigger(ANIM_ATTACK);
            }

            // 예고 중 맞아 서버가 공격을 취소한 순간, 진행 중이던 공격 동작을 끊고 피격 동작으로 넘어간다
            if ((!dead) && (behavior == EEnemyState.Hit) && (_lastBehavior != EEnemyState.Hit))
            {
                _animator.SetTrigger(ANIM_DAMAGE);
            }

            if ((behavior != _lastBehavior) && (_activeModel >= 0))
            {
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                ProbeLog($"Behavior {_lastBehavior}->{behavior} dashingParam={_hasDashingParam[_activeModel]} "
                    + $"animHash={info.shortNameHash} inTransition={_animator.IsInTransition(0)} speed={speed:F2}");
            }

            _lastPosition = position;
            _lastBehavior = behavior;
            _hasPose      = true;
        }

        // 서버 행동이 Dash이면 달린다. 복제가 중간 상태를 놓쳐 행동이 예고에 머물러도, 준비 자세인 채 걷는 속도보다 훨씬 빠르게
        // 움직이면 돌진 중으로 본다. 근거가 끊겨도 잠깐은 달리기를 유지해 스냅샷 사이에 자세가 깜빡이지 않게 한다
        private bool ReadDashing(EEnemyState behavior, float speed, bool dead)
        {
            if (dead)
            {
                _dashUntil = 0f;
                return false;
            }

            if ((behavior == EEnemyState.Dash) || ((speed >= DASH_INFER_SPEED) && IsInBrace()))
            {
                _dashUntil = Time.time + DASH_HOLD_SECONDS;
            }

            return Time.time < _dashUntil;
        }

        // 준비 자세(Attack 상태)에 있거나 그리로 넘어가는 중인가
        private bool IsInBrace()
        {
            if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == STATE_ATTACK)
            {
                return true;
            }

            return _animator.IsInTransition(0) && (_animator.GetNextAnimatorStateInfo(0).shortNameHash == STATE_ATTACK);
        }

        // 준비 자세는 돌진 때만 빠져나오는 상태라 예고도 돌진도 아닌데 남아 있으면 평소 자세로 되돌린다
        private void ReleaseStuckBrace(EEnemyState behavior, bool dashing)
        {
            if (dashing || (behavior == EEnemyState.Telegraph) || (!IsInBrace()))
            {
                _braceReleaseAt = -1f;
                return;
            }

            if (_braceReleaseAt < 0f)
            {
                _braceReleaseAt = Time.time + BRACE_RELEASE_SECONDS;
                return;
            }

            if ((Time.time >= _braceReleaseAt) && _animator.HasState(0, STATE_IDLE))
            {
                ProbeLog("준비 자세에 남아 평소 자세로 복귀");
                _animator.CrossFadeInFixedTime(STATE_IDLE, RELEASE_FADE_SECONDS, 0);
                _braceReleaseAt = -1f;
            }
        }

        // 죽음을 처음 본 프레임에 모델 복제본을 날려 보내고 원본은 숨긴다. 개체는 곧 풀로 돌아가므로 시체는 따로 남긴다.
        // 처치 방향 계약이 없어 내 플레이어 반대쪽으로 날리고, 내 플레이어를 모르면 개체의 뒤쪽으로 날린다(표현 전용 기본값).
        // 모델이 없는 대체 캡슐은 날리지 않는다
        private void ApplyCorpse()
        {
            if (_state.LifeState == EParticipantLifeState.Alive)
            {
                _seenAlive = true;
                return;
            }

            // 풀에서 켜진 직후 이전 생애의 죽음 상태가 남아 있어도 산 모습을 한 번 본 개체만 시체로 만든다
            if (_corpseLaunched || (!_seenAlive) || (_activeModel < 0))
            {
                return;
            }

            ProbeLog("Dead 상태 수신, 시체 발사");
            LaunchCorpse();
        }

        // 서버는 처치된 적을 상태 전달 없이 곧바로 내리므로 클라이언트는 사망 상태를 못 받고 제거만 받는다.
        // 연결이 살아 있고 씬이 내려가는 중이 아닐 때의 제거를 처치로 보고 시체를 날린다. 던전 종료로 산 채 내려가는 개체도 시체가 될 수 있다
        private void LaunchOnRemoval()
        {
            if (_corpseLaunched || (!_seenAlive) || (_activeModel < 0) || s_quitting || (!NetworkClient.active)
                || (!gameObject.scene.isLoaded) || IsSortieClosing())
            {
                return;
            }

            ProbeLog("제거 시점 시체 발사");
            LaunchCorpse();
        }

        // 출정 종료나 내 이탈이 이미 알려진 뒤의 제거는 던전 정리라 처치가 아니다
        private static bool IsSortieClosing()
        {
            if (!SortieClient.HasStatus)
            {
                return false;
            }

            SortieStatusMessage status = SortieClient.Current;
            return status.HasPersonalClose || (status.Phase >= ESortiePhase.Ended);
        }

        private void LaunchCorpse()
        {
            _corpseLaunched = true;
            if (EnemyCorpse.Launch(_modelRoots[_activeModel], ReadFlyDirection()))
            {
                _modelRoots[_activeModel].SetActive(false);
                _activeModel = NO_MODEL; // 적용한 역할은 그대로 둬 이번 생애에는 모델이 다시 켜지지 않고, 풀에서 다시 켜질 때 OnEnable이 지운다
                _animator    = null;
            }
        }

        // 서버가 방향을 알리지 않으므로 이 클라이언트의 플레이어에게서 멀어지는 쪽을 쓴다. 없으면 영벡터로 넘겨 시체가 정한 기본 방향을 쓰게 한다
        private Vector3 ReadFlyDirection()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return Vector3.zero;
            }

            return transform.position - local.transform.position;
        }

        // 임시 진단. 실제 던전에서 사망 상태가 클라이언트에 도달하는지 확인한 뒤 지운다
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void ProbeLog(string message)
        {
            if (_state != null)
            {
                Debug.Log($"[EnemyDeathProbe] combatId={_state.CombatId} life={_state.LifeState} hp={_state.Health}/{_state.MaxHealth} behavior={_state.Behavior} {message}");
            }
        }

        private void ApplyHealthFlash()
        {
            float health = _state.Health;
            if ((health < _lastHealth) && (_state.LifeState == EParticipantLifeState.Alive))
            {
                _flashUntil = Time.time + FLASH_SECONDS;
                PlayHitReaction();
            }

            _lastHealth = health;
        }

        // 공격을 끊지 않는 피격만 동작으로 보인다. 예고, 돌진, 경직 중 피격은 서버가 상태로 알린다
        private void PlayHitReaction()
        {
            if ((_animator == null) || (!_animator.isActiveAndEnabled))
            {
                return;
            }

            EEnemyState behavior = _state.Behavior;
            if ((behavior == EEnemyState.Telegraph) || (behavior == EEnemyState.Dash)
                || (behavior == EEnemyState.Hit) || (behavior == EEnemyState.Dead))
            {
                return;
            }

            _animator.SetTrigger(ANIM_DAMAGE);
        }

        // 깜빡임은 원본 재질 색에 붉은 기운을 섞은 값을 프로퍼티 블록으로 잠시 얹고, 끝나면 블록을 지워 원본 재질로 돌린다
        private void ApplyFlash()
        {
            bool flashing = Time.time < _flashUntil;
            if (flashing == _flashApplied)
            {
                return;
            }

            if (_activeModel < 0)
            {
                _flashApplied = flashing;
                SetFallbackColor(flashing ? Color.white : EnemyRoleColors.Get(_appliedRole));
                return;
            }

            if (!flashing)
            {
                ClearFlash();
                return;
            }

            Renderer[] renderers = _modelRenderers[_activeModel];
            Color[]    colors    = _flashColors[_activeModel];
            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].GetPropertyBlock(_block);
                _block.SetColor(BASE_COLOR_ID, colors[i]);
                renderers[i].SetPropertyBlock(_block);
            }

            _flashApplied = true;
        }

        private void ClearFlash()
        {
            if (_modelRenderers != null)
            {
                for (int m = 0; m < _modelRenderers.Length; ++m)
                {
                    Renderer[] renderers = _modelRenderers[m];
                    for (int i = 0; i < renderers.Length; ++i)
                    {
                        renderers[i].SetPropertyBlock(null);
                    }
                }
            }

            _flashApplied = false;
        }

        private void ApplyTelegraph()
        {
            if ((_telegraphLine == null) || (!_state.TryGetTelegraph(out EnemyTelegraphCue cue)))
            {
                HideTelegraph();
                return;
            }

            double now = NetworkTime.time;
            if (now > (cue.EndServerTime + TELEGRAPH_END_GRACE))
            {
                HideTelegraph();
                return;
            }

            int count = EnemyTelegraphGeometry.Fill(_points, cue);
            if (count <= 0)
            {
                HideTelegraph();
                return;
            }

            _telegraphLine.positionCount = count;
            _telegraphLine.SetPositions(_points);

            Color color = Color.Lerp(Color.yellow, Color.red, cue.Progress(now));
            _telegraphLine.startColor = color;
            _telegraphLine.endColor   = color;
            _telegraphLine.startWidth = LINE_WIDTH;
            _telegraphLine.endWidth   = LINE_WIDTH;
            _telegraphLine.enabled    = true;
        }

        private void HideTelegraph()
        {
            if (_telegraphLine != null)
            {
                _telegraphLine.enabled = false;
            }
        }

        // 모델이 없을 때만 쓰는 대체 캡슐 색
        private void SetFallbackColor(Color color)
        {
            if (_visualRenderer == null)
            {
                return;
            }

            _visualRenderer.GetPropertyBlock(_block);
            _block.SetColor(BASE_COLOR_ID, color);
            _block.SetColor(COLOR_ID, color);
            _visualRenderer.SetPropertyBlock(_block);
        }
    }
}
