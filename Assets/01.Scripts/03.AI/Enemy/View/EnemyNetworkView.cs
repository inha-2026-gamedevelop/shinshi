// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

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
        private const int   NO_MODEL            = -1;

        // 모델 배열 순서, 프리팹 빌더가 같은 순서로 채운다
        public const int MODEL_BEAST   = 0;
        public const int MODEL_VENDIGO = 1;

        private static readonly Color FLASH_TINT = Color.red;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int COLOR_ID      = Shader.PropertyToID("_Color");

        private static readonly int ANIM_MOVING  = Animator.StringToHash("Moving");
        private static readonly int ANIM_DEAD    = Animator.StringToHash("Dead");
        private static readonly int ANIM_ATTACK  = Animator.StringToHash("Attack");
        private static readonly int ANIM_DAMAGE  = Animator.StringToHash("Damage");
        private static readonly int ANIM_DASHING = Animator.StringToHash("Dashing");

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

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
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
            ClearFlash();
            HideTelegraph();
        }

        private void OnDisable()
        {
            HideTelegraph();
        }

        private void Update()
        {
            if (_state == null)
            {
                return;
            }

            ApplyHealthFlash();
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
                _animator.SetBool(ANIM_DASHING, behavior == EEnemyState.Dash);
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

            _lastPosition = position;
            _lastBehavior = behavior;
            _hasPose      = true;
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
