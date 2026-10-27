// System
using System.Reflection;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> 시험 씬에서만 마검사 기본 공격을 1타 단발, 2타 3연타, 3타 5연타, 4타 강타 단발로 바꿔 본다. 서버 코드는 그대로 두고 호스트가 서버 틱 뒤에서 끼어든다 </summary>
    public sealed class DevMageknightComboTrial
    {
        // 서버는 근접 기본 공격을 입력 하나에 한 번만 판정한다. 모션 반복과 효과 재생은 정식 데이터(타수, 간격)를 읽는 클라이언트 뷰가 하므로
        // 여기서는 데이터를 메모리에서만 바꾸고, 서버가 내는 첫 판정 뒤에 남은 타를 같은 방식으로 정해진 간격에 더 낸다
        // 이 클래스는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int   MAX_EXTRA_HITS = 7;
        private const float RESET_SECONDS  = 1.2f;
        private const float EFFECT_SECONDS = 3f;
        private const int   POOL_SIZE      = 4;

        // 단계별 한 번의 판정 계수, 단계 총 피해는 계수 곱하기 타수다. 강타는 5연타 합보다 조금 높게 둔다
        private static readonly PlayerConfigSO.ComboStepSpec[] _steps =
        {
            new PlayerConfigSO.ComboStepSpec(0.9f, 0.7f, 0f, false),
            new PlayerConfigSO.ComboStepSpec(0.5f, 0.95f, 0f, false, 3, 0.15f, 0, 2f),
            new PlayerConfigSO.ComboStepSpec(0.4f, 1.15f, 0f, false, 5, 0.12f, 1, 2.5f),
            new PlayerConfigSO.ComboStepSpec(2.5f, 1.4f, 0f, true, 1, 0f, 2, 1f, true)
        };

        private struct ExtraHit
        {
            public double FireAt;
            public int    Group;
            public bool   IsActive;
        }

        private readonly PlayerServer      _server;
        private readonly PlayerCombatState _combat;
        private readonly Transform         _actor;
        private readonly MageknightVfxPool _pool;
        private readonly Transform         _poolRoot;
        private readonly MethodInfo        _resolveBasicAttack;
        private readonly ExtraHit[]        _extras = new ExtraHit[MAX_EXTRA_HITS];

        private byte    _lastSequence;
        private byte    _dodgeSequence;
        private int     _castId;
        private Vector3 _aim = Vector3.forward;
        private float   _coefficient;

        /****************************************
        *                Methods
        ****************************************/

        // 원본 마검사 효과 뷰는 단계마다 한 번만 재생하므로, 첫 타는 뷰에 맡기고 추가 타의 효과만 여기서 같은 프리팹으로 따로 재생한다
        public DevMageknightComboTrial(PlayerServer server, PlayerCombatState combat, Transform actor, GameObject basicPrefab)
        {
            _server = server;
            _combat = combat;
            _actor  = actor;

            if (basicPrefab != null)
            {
                _poolRoot = new GameObject("DevMageknightComboVfxPool").transform;
                _pool     = new MageknightVfxPool(basicPrefab, _poolRoot, POOL_SIZE);
            }

            _resolveBasicAttack = typeof(PlayerServer).GetMethod("ResolveBasicAttack", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_resolveBasicAttack == null)
            {
                Debug.LogError("[DevMageknightComboTrial] PlayerServer의 기본 공격 내부 멤버를 찾지 못했다. 서버 코드가 바뀌었는지 확인한다");
            }

            _lastSequence = combat.AttackSequence;
        }

        /// <summary> 마검사 기본 공격 명세의 콤보 단계를 시험용 4단으로 메모리에서만 바꾼다. 돌려준 동작으로 종료 때 되돌린다 </summary>
        public static System.Action ApplyData(PlayerConfigSO.BasicAttackSpec spec)
        {
            FieldInfo steps = typeof(PlayerConfigSO.BasicAttackSpec).GetField("_comboSteps", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo reset = typeof(PlayerConfigSO.BasicAttackSpec).GetField("_comboResetSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
            if ((steps == null) || (reset == null))
            {
                Debug.LogError("[DevMageknightComboTrial] 기본 공격 명세의 콤보 필드를 찾지 못했다. 데이터 구조가 바뀌었는지 확인한다");
                return null;
            }

            object originalSteps = steps.GetValue(spec);
            object originalReset = reset.GetValue(spec);
            steps.SetValue(spec, (PlayerConfigSO.ComboStepSpec[])_steps.Clone());
            reset.SetValue(spec, RESET_SECONDS);
            return () =>
            {
                steps.SetValue(spec, originalSteps);
                reset.SetValue(spec, originalReset);
            };
        }

        /// <summary> 서버 공격 틱 직후에 부른다. aim은 호스트가 이번에 보낸 기본 공격 방향이다 </summary>
        public void AfterAttackTick(double now, Vector3 aim)
        {
            if (IsInterrupted())
            {
                CancelAll();
            }

            byte sequence = _combat.AttackSequence;
            if (sequence != _lastSequence)
            {
                _lastSequence = sequence;
                OnNewAttack(now, aim);
            }

            TickExtras(now);
            _pool?.Tick(now);
        }

        /// <summary> 호스트가 사라질 때 부른다. 추가 타 효과 풀을 정리한다 </summary>
        public void Dispose()
        {
            if (_pool != null)
            {
                _pool.StopAll();
            }

            if (_poolRoot != null)
            {
                Object.Destroy(_poolRoot.gameObject);
            }
        }

        // 서버가 이미 낸 첫 판정을 1타로 치고, 나머지는 같은 간격으로 뒤따라 낸다
        private void OnNewAttack(double now, Vector3 aim)
        {
            _aim           = aim;
            _dodgeSequence = _combat.DodgeSequence;
            _castId        = _combat.CastInstanceId;

            for (int i = 0; i < _extras.Length; ++i)
            {
                _extras[i].IsActive = false;
            }

            PlayerConfigSO.BasicAttackSpec spec = GameDB.Player.GetBasicAttack(EPlayerClass.Mageknight);
            int step  = _combat.AttackComboStep;
            int shots = Mathf.Clamp(spec.GetShotCount(step), 1, MAX_EXTRA_HITS + 1);
            if (shots <= 1)
            {
                return;
            }

            _coefficient = spec.GetDamageCoefficient(step);
            double firstFireAt = now + spec.GetWindup(step);
            // 앞 세 묶음은 슬래시, 마지막 묶음은 강타 전용이라 연타에서는 슬래시 셋을 돌려 쓴다. 이웃한 두 타는 같은 모양이 되지 않는다
            int mixable = (_pool != null) ? Mathf.Max(1, _pool.GroupCount - 1) : 1;
            for (int i = 0; i < shots - 1; ++i)
            {
                _extras[i].FireAt   = firstFireAt + ((i + 1) * spec.GetShotInterval(step));
                _extras[i].Group    = (step + i + 1) % mixable;
                _extras[i].IsActive = true;
            }
        }

        private void TickExtras(double now)
        {
            for (int i = 0; i < _extras.Length; ++i)
            {
                if ((_extras[i].IsActive) && (now >= _extras[i].FireAt))
                {
                    _extras[i].IsActive = false;
                    _resolveBasicAttack.Invoke(_server, new object[] { _aim, _extras[i].FireAt, _coefficient });
                    PlayExtraEffect(_extras[i].Group, now);
                }
            }
        }

        private void PlayExtraEffect(int group, double now)
        {
            if ((_pool == null) || (!_pool.HasPrefab))
            {
                return;
            }

            Vector3 direction = _aim;
            direction.y = 0f;
            Quaternion rotation = (direction.sqrMagnitude > Mathf.Epsilon) ? Quaternion.LookRotation(direction) : Quaternion.identity;
            _pool.PlayBrief(_actor.position, rotation, now, EFFECT_SECONDS, group);
        }

        // 회피, 기술 시전, 사망이면 아직 나가지 않은 타를 버린다
        private bool IsInterrupted()
        {
            return (_combat.LifeState != EParticipantLifeState.Alive) || (_combat.DodgeSequence != _dodgeSequence)
                || (_combat.CastInstanceId != _castId);
        }

        private void CancelAll()
        {
            for (int i = 0; i < _extras.Length; ++i)
            {
                _extras[i].IsActive = false;
            }

            _dodgeSequence = _combat.DodgeSequence;
            _castId        = _combat.CastInstanceId;
        }
    }
}
