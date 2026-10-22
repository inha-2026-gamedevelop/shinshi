// System
using System.Reflection;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> 시험 씬에서만 궁수 기본 공격 콤보를 1타 단발, 2타 3연발, 3타 강공격 단발로 바꿔 본다. 서버 코드는 그대로 두고 호스트가 서버 틱 앞뒤에서 끼어든다 </summary>
    public sealed class DevArcherComboTrial
    {
        // 서버는 단계마다 입력 하나에 화살 하나를 낸다. 여기서는 서버 판정기를 그대로 쓰되
        //   2타: 서버가 내는 첫 화살 뒤에 같은 방식의 화살을 정해진 간격으로 더 낸다. 모션은 1타 모션을 빠르게 다시 재생한다
        //   3타: 서버가 내는 화살 한 발 그대로다. 강공격 효과는 효과 뷰가 그린다
        // 이 클래스는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    MAX_EXTRA_SHOTS    = 7;
        private const int    STEP_COUNT         = 3;
        private const int    FIRST_STEP         = 0;
        private const int    BURST_STEP         = 1;
        private const int    LAST_STEP          = 2;
        private const string ACTION_SPEED_PARAM = "ActionSpeed";

        public struct Settings
        {
            public int   BurstShots;
            public float BurstGap;
            public float BurstAnimSpeedScale;
        }

        private struct ExtraShot
        {
            public double BumpAt;
            public double FireAt;
            public bool   IsBumped;
            public bool   IsActive;
        }

        private static readonly int _actionSpeedHash = Animator.StringToHash(ACTION_SPEED_PARAM);

        private readonly Settings          _settings;
        private readonly PlayerServer      _server;
        private readonly PlayerCombatState _combat;
        private readonly Animator          _animator;
        private readonly MethodInfo        _resolveBasicAttack;
        private readonly ExtraShot[]       _extras           = new ExtraShot[MAX_EXTRA_SHOTS];
        private readonly float[]           _shotCoefficients = new float[STEP_COUNT];

        private byte    _lastSequence;
        private byte    _dodgeSequence;
        private int     _castId;
        private Vector3 _aim = Vector3.forward;
        private float   _extraCoefficient;
        private int     _boostAtFrame;

        /****************************************
        *                Methods
        ****************************************/

        public DevArcherComboTrial(in Settings settings, PlayerServer server, PlayerCombatState combat, Animator animator)
        {
            _settings = settings;
            _server   = server;
            _combat   = combat;
            _animator = animator;

            _resolveBasicAttack = typeof(PlayerServer).GetMethod("ResolveBasicAttack", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_resolveBasicAttack == null)
            {
                Debug.LogError("[DevArcherComboTrial] PlayerServer의 화살 내부 멤버를 찾지 못했다. 서버 코드가 바뀌었는지 확인한다");
            }

            _lastSequence = combat.AttackSequence;
        }

        /// <summary> 연발 단계의 첫 화살과 나머지 화살이 같은 계수를 쓰도록 단계 계수를 발수로 나눈다. 돌려준 동작으로 종료 때 되돌린다 </summary>
        public System.Action DivideBurstCoefficient(PlayerConfigSO.BasicAttackSpec spec)
        {
            if ((!spec.HasCombo) || (spec.ComboStepCount <= LAST_STEP))
            {
                return null;
            }

            FieldInfo field = typeof(PlayerConfigSO.ComboStepSpec).GetField("_damageCoefficient",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                return null;
            }

            System.Action restore = null;
            for (int step = FIRST_STEP; step < STEP_COUNT; ++step)
            {
                PlayerConfigSO.ComboStepSpec stepSpec = spec.GetComboStep(step);
                float original = (float)field.GetValue(stepSpec);
                int   shots    = GetShots(step);

                _shotCoefficients[step] = original / shots;
                if (shots <= 1)
                {
                    continue;
                }

                field.SetValue(stepSpec, _shotCoefficients[step]);
                restore += () => field.SetValue(stepSpec, original);
            }

            return restore;
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
        }

        /// <summary> 애니메이션 뷰가 재생을 시작한 뒤에 부른다. 방금 다시 건 1타 모션의 속도만 높인다 </summary>
        public void ApplyAnimationBoost()
        {
            if ((_boostAtFrame <= 0) || (Time.frameCount < _boostAtFrame) || (_animator == null))
            {
                return;
            }

            _boostAtFrame = 0;
            _animator.SetFloat(_actionSpeedHash, _animator.GetFloat(_actionSpeedHash) * _settings.BurstAnimSpeedScale);
        }

        // 단계마다 한 번 입력으로 나가는 화살 수
        private int GetShots(int step)
        {
            return (step == BURST_STEP) ? Mathf.Max(1, _settings.BurstShots) : 1;
        }

        private void OnNewAttack(double now, Vector3 aim)
        {
            _aim           = aim;
            _dodgeSequence = _combat.DodgeSequence;
            _castId        = _combat.CastInstanceId;

            int step  = _combat.AttackComboStep;
            int shots = GetShots(step);
            if ((step > FIRST_STEP) && (step < STEP_COUNT) && (shots > 1))
            {
                StartBurst(now, GameDB.Player.GetBasicAttack(EPlayerClass.Archer), step, shots);
            }
        }

        // 서버가 이미 낸 첫 화살을 1발로 치고, 나머지는 같은 간격으로 뒤따라 낸다. 화살 표현은 승인 순서를 따라 준비 시간 뒤에 나가므로 순번을 미리 올린다
        private void StartBurst(double now, PlayerConfigSO.BasicAttackSpec spec, int step, int shots)
        {
            RestartFirstStepMotion();

            double firstFireAt = now + spec.GetWindup(step);
            double windupFirst = spec.GetWindup(FIRST_STEP);
            int    extraCount  = Mathf.Min(shots - 1, MAX_EXTRA_SHOTS);
            for (int i = 0; i < _extras.Length; ++i)
            {
                _extras[i].IsActive = false;
            }

            _extraCoefficient = _shotCoefficients[step];
            for (int i = 0; i < extraCount; ++i)
            {
                double fireAt = firstFireAt + ((i + 1) * _settings.BurstGap);
                _extras[i].FireAt   = fireAt;
                _extras[i].BumpAt   = System.Math.Max(now, fireAt - windupFirst);
                _extras[i].IsBumped = false;
                _extras[i].IsActive = true;
            }
        }

        // 서버가 정한 단계 대신 1단으로 공개해 애니메이션 뷰가 1타 모션을 재생하게 한다
        private void RestartFirstStepMotion()
        {
            _combat.BumpAttackSequence((byte)FIRST_STEP);
            _lastSequence = _combat.AttackSequence;
            _boostAtFrame = Time.frameCount + 1;
        }

        private void TickExtras(double now)
        {
            for (int i = 0; i < _extras.Length; ++i)
            {
                if (!_extras[i].IsActive)
                {
                    continue;
                }

                if ((!_extras[i].IsBumped) && (now >= _extras[i].BumpAt))
                {
                    RestartFirstStepMotion();
                    _extras[i].IsBumped = true;
                }

                if ((_extras[i].IsBumped) && (now >= _extras[i].FireAt))
                {
                    _extras[i].IsActive = false;
                    _resolveBasicAttack.Invoke(_server, new object[] { _aim, _extras[i].FireAt, _extraCoefficient });
                }
            }
        }

        // 회피, 기술 시전, 사망이면 아직 나가지 않은 화살을 버린다. 이미 나간 화살은 서버가 사거리까지 이어 간다
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
