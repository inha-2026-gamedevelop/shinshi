// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 보스 외형을 그리는 표현이다. 서버가 정한 위치와 방향을 받아 따라가고, 예고 표현 진입점(BossTelegraphPresenter)이
    // 알리는 시전 단계(예고, 발동, 끝)와 돌진 값으로 임시 모션을 만든다. 서버 값만 읽고 판정에는 관여하지 않는다.
    // 모션은 애니메이션 클립이 없는 동안 쓰는 휴머노이드 본 직접 조작이다. 컨트롤러가 붙으면 그 위에 덧붙여 계속 쓸 수 있고
    // 진짜 모션이 들어오면 이 클래스의 모션 부분만 걷어내면 된다.
    // 본을 세계 좌표 축으로 돌려서 본마다 다른 로컬 축 방향을 신경 쓰지 않는다
    /// <summary> 월백 외형 표현, 위치와 방향을 따라가고 시전 단계에 맞춰 임시 모션을 보여 준다 </summary>
    public sealed class BossView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        // 표현 상수다, 밸런스 값이 아니다
        private const float  POSITION_SMOOTH      = 20f;  // 1/초, 서버 갱신 사이를 부드럽게 이을 때만 쓴다
        private const float  TURN_SMOOTH          = 12f;  // 1/초
        private const float  GATHER_SMOOTH        = 6f;   // 1/초, 예고 동안 천천히 모으는 자세로
        private const float  RELEASE_SMOOTH       = 22f;  // 1/초, 발동은 순간적으로 튕긴다
        private const float  IDLE_SMOOTH          = 7f;   // 1/초
        private const float  RELEASE_SECONDS      = 0.35f; // 초, 발동 자세를 유지하는 시간
        private const float  BREATH_RATE          = 1.6f; // rad/초
        private const float  BREATH_PITCH         = 1.5f; // 도
        private const float  BREATH_LIFT          = 0.02f; // m
        private const float  DOWN_PITCH           = -80f; // 도, 쓰러질 때 뒤로 눕는 각도
        private const float  DOWN_LIFT            = 0.2f; // m
        private const float  DASH_END_MARGIN      = 0.15f; // 초, 돌진 끝 뒤에 돌진 자세를 풀기까지
        private const float  DOWN_ARM_SIDE        = 30f;  // 도
        private const float  DASH_PIVOT_PITCH     = 28f;  // 도, 돌진은 몸 전체를 앞으로 눕힌다
        private const float  DASH_SPINE_PITCH     = 14f;  // 도
        private const float  DASH_HEAD_PITCH      = -10f; // 도
        private const float  DASH_ARM_SIDE        = 20f;  // 도
        private const float  DASH_ARM_FORWARD     = -40f; // 도, 팔을 뒤로 젖힌다
        private const float  DASH_WINDUP_PITCH    = -12f; // 도, 돌진 예고 동안 몸을 뒤로 모은다
        private const float  RELEASE_ARM_SCALE    = 1.5f; // 발동 때 팔을 모으던 각도보다 더 내미는 배율
        private const float  HALF                 = 0.5f;
        private const float  DIRECTION_EPSILON    = 0.0001f;
        // 클라이언트 어셈블리는 서버 어셈블리의 패턴 팩토리 상수를 볼 수 없어 장판 패턴의 ID를 따로 둔다. 모양은 원형이어도 자세가 달라서다
        private const string AREA_PATTERN_ID     = "wolbaek_p05";
        private const string AREA_PHASE_TWO_ID   = Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P04_ID; // 2페이즈 지정 장판도 같은 던지는 자세다
        private const string URP_LIT_SHADER       = "Universal Render Pipeline/Lit";
        private const string PROP_SRC_TEXTURE     = "_MainTex";
        private const string PROP_SRC_COLOR       = "_Color";
        private const string PROP_SRC_SMOOTH      = "_Glossiness";
        private const string PROP_BASE_MAP        = "_BaseMap";
        private const string PROP_BASE_COLOR      = "_BaseColor";
        private const string PROP_BUMP_MAP        = "_BumpMap";
        private const string PROP_SMOOTHNESS      = "_Smoothness";
        private const string PROP_CULL            = "_Cull";
        private const string PROP_ALPHA_CLIP      = "_AlphaClip";
        private const string PROP_CUTOFF          = "_Cutoff";
        private const string KEYWORD_NORMAL       = "_NORMALMAP";
        private const string KEYWORD_ALPHATEST    = "_ALPHATEST_ON";
        private const float  ALPHA_CUTOFF_DEFAULT = 0.5f;
        private const float  FLAG_ON              = 1f;

        /****************************************
        *                Types
        ****************************************/

        // 한 순간의 자세, 각도는 모두 도이고 본 하나가 아니라 몸 전체의 뜻으로 쓴다
        private struct Pose
        {
            public float PivotPitch;   // 몸 전체가 앞으로 숙이는 각도, 음수는 뒤로
            public float SpinePitch;   // 상체가 앞으로 숙이는 각도
            public float HeadPitch;    // 고개가 숙이는 각도, 음수는 든다
            public float ArmSide;      // 양팔이 옆으로 벌어지는 각도
            public float ArmForward;   // 양팔이 앞으로 드는 각도, 음수는 뒤로
            public float Lift;         // 몸이 위로 뜨는 높이

            public static Pose Lerp(in Pose from, in Pose to, float t)
            {
                return new Pose
                {
                    PivotPitch = Mathf.Lerp(from.PivotPitch, to.PivotPitch, t),
                    SpinePitch = Mathf.Lerp(from.SpinePitch, to.SpinePitch, t),
                    HeadPitch  = Mathf.Lerp(from.HeadPitch, to.HeadPitch, t),
                    ArmSide    = Mathf.Lerp(from.ArmSide, to.ArmSide, t),
                    ArmForward = Mathf.Lerp(from.ArmForward, to.ArmForward, t),
                    Lift       = Mathf.Lerp(from.Lift, to.Lift, t)
                };
            }
        }

        // 휴머노이드 본 하나와 기본 자세의 로컬 회전
        private struct BoneRef
        {
            public Transform  Bone;
            public Quaternion Bind;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("연결")]
        [SerializeField] private Transform _pivot;    // 몸 전체를 숙이고 띄우는 자식, 모델이 이 아래에 있다
        [SerializeField] private Animator  _animator; // 휴머노이드 아바타를 가진 모델의 애니메이터

        [Header("자세")]
        [SerializeField] private Vector3 _gatherSideArms = new Vector3(-6f, 60f, 25f);   // 부채꼴, 직선: 몸을 뒤로 젖히고 팔을 벌린다. x 상체, y 팔 옆, z 팔 앞
        [SerializeField] private Vector3 _gatherCircle   = new Vector3(8f, 25f, -10f);   // 원형: 웅크려 모은다
        [SerializeField] private Vector3 _gatherThrow    = new Vector3(-10f, 20f, 55f);  // 투사체, 장판: 팔을 앞으로 든다

        private readonly List<BoneRef>  _bones            = new List<BoneRef>();
        private readonly List<Material> _runtimeMaterials = new List<Material>();

        private BossTelegraphPresenter _presenter;
        private Func<double>           _serverClock;
        private BoneRef                _spine;
        private BoneRef                _chest;
        private BoneRef                _head;
        private BoneRef                _leftArm;
        private BoneRef                _rightArm;
        private bool                   _hasBones;
        private bool                   _resetsBind;
        private bool                   _hasClip;

        private Vector3    _targetPosition;
        private Vector3    _targetForward = Vector3.forward;
        private bool       _hasPose;

        private int                _castId;
        private bool               _hasCast;
        private bool               _isDefeated;
        private Pose               _current;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_pivot == null) || (_animator == null))
            {
                Debug.LogError("[BossView] 몸 피벗 또는 애니메이터 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            ConvertToUrpLit(_pivot.GetComponentsInChildren<Renderer>(), _runtimeMaterials);
            CacheBones();
        }

        private void OnDestroy()
        {
            Unbind();
            DestroyRuntimeMaterials();
        }

        private void Update()
        {
            FollowPose(Time.deltaTime);
        }

        // 본은 애니메이터가 쓴 뒤에 얹어야 하므로 LateUpdate다
        private void LateUpdate()
        {
            if (!_hasBones)
            {
                return;
            }

            UpdateMotion(Time.deltaTime);
            ApplyPose(in _current);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고 표현 진입점에 연결한다, 시전 단계 알림으로 모션을 정하고 시각은 serverClock으로 읽는다. 비우면 로컬 시각이다 </summary>
        public void Bind(BossTelegraphPresenter presenter, Func<double> serverClock)
        {
            Unbind();

            _presenter   = presenter;
            _serverClock = serverClock;

            if (_presenter != null)
            {
                _presenter.PhaseChanged += OnPhaseChanged;
            }
        }

        /// <summary> 서버가 정한 위치와 방향, 처음 받으면 그 자리로 바로 옮기고 이후는 부드럽게 따라간다 </summary>
        public void SetPose(Vector3 position, Vector3 forward)
        {
            _targetPosition = position;

            forward.y = 0f;
            if (forward.sqrMagnitude > DIRECTION_EPSILON)
            {
                _targetForward = forward.normalized;
            }

            if (!_hasPose)
            {
                _hasPose           = true;
                transform.position = _targetPosition;
                transform.rotation = Quaternion.LookRotation(_targetForward, Vector3.up);
            }
        }

        /// <summary> 쓰러진 모습을 보인다, 보스 사망이다. 되돌리면 다시 일어선다 </summary>
        public void SetDefeated(bool isDefeated)
        {
            _isDefeated = isDefeated;
        }

        /// <summary> 모션과 위치 기억을 처음으로 되돌린다, 전투를 다시 시작할 때 쓴다 </summary>
        public void ResetView()
        {
            _hasPose    = false;
            _hasCast    = false;
            _isDefeated = false;
            _current    = default;
        }

        private void Unbind()
        {
            if (_presenter != null)
            {
                _presenter.PhaseChanged -= OnPhaseChanged;
            }

            _presenter = null;
        }

        private double ReadClock()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        // 새 시전이 시작되면 그것을 따라가고, 따라가던 시전이 끝나면 놓는다
        private void OnPhaseChanged(int castId, EBossTelegraphPhase phase)
        {
            if (phase == EBossTelegraphPhase.Telegraph)
            {
                _castId  = castId;
                _hasCast = true;
                return;
            }

            if ((_hasCast) && (castId == _castId) &&
                ((phase == EBossTelegraphPhase.Finished) || (phase == EBossTelegraphPhase.Cancelled)))
            {
                _hasCast = false;
            }
        }

        // 위치는 서버 값을 부드럽게 따라가되 돌진 중에는 서버가 보낸 시작 위치와 방향과 속도로 직접 계산한다.
        // 복제 위치는 돌진처럼 짧고 빠른 이동을 성기게 보내기 때문이다
        private void FollowPose(float deltaTime)
        {
            if (!_hasPose)
            {
                return;
            }

            Vector3 target = _targetPosition;
            if (TryGetDashPosition(out Vector3 dashPosition))
            {
                target = dashPosition;
            }

            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-POSITION_SMOOTH * deltaTime));

            Quaternion look = Quaternion.LookRotation(_targetForward, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-TURN_SMOOTH * deltaTime));
        }

        private bool TryGetDashPosition(out Vector3 position)
        {
            position = default;

            if ((!_hasCast) || (_presenter == null) || (!_presenter.TryGetVisuals(_castId, out BossTelegraphCast cast, out _, out _)))
            {
                return false;
            }

            if ((cast.Shape != EBossTelegraphShape.Dash) || (!cast.HasExecuted))
            {
                return false;
            }

            position = cast.Origin + (cast.Direction * cast.GetDashTraveled(ReadClock()));
            return true;
        }

        // 지금 시전 단계에 맞는 목표 자세로 현재 자세를 부드럽게 옮긴다
        private void UpdateMotion(float deltaTime)
        {
            Pose  target = IdlePose();
            float smooth = IDLE_SMOOTH;

            if (_isDefeated)
            {
                target = DownPose();
            }
            else if ((_hasCast) && (_presenter != null) &&
                (_presenter.TryGetVisuals(_castId, out BossTelegraphCast cast, out _, out _)))
            {
                target = CastPose(cast, ref smooth);
            }

            _current = Pose.Lerp(in _current, in target, 1f - Mathf.Exp(-smooth * deltaTime));
        }

        private Pose CastPose(BossTelegraphCast cast, ref float smooth)
        {
            Pose   idle = IdlePose();
            double now = ReadClock();

            if (!cast.HasExecuted)
            {
                smooth = GATHER_SMOOTH;
                Pose gather = GatherPose(cast.Shape, cast.PatternId);
                return Pose.Lerp(in idle, in gather, Mathf.SmoothStep(0f, 1f, cast.GetTelegraphProgress(now)));
            }

            smooth = RELEASE_SMOOTH;

            if (cast.Shape == EBossTelegraphShape.Dash)
            {
                bool isDashing = cast.GetExecuteAge(now) <= (cast.GetDashEndAge() + DASH_END_MARGIN);
                return isDashing ? DashPose() : idle;
            }

            // 발동 직후에만 자세를 내밀고 곧 풀어서 다음 예고의 모으기와 이어지게 한다
            if (cast.GetExecuteAge(now) > RELEASE_SECONDS)
            {
                smooth = IDLE_SMOOTH;
                return idle;
            }

            return ReleasePose(cast.Shape, cast.PatternId);
        }

        // 대기 클립이 호흡을 하므로 클립이 붙어 있으면 임시 호흡은 더하지 않는다
        private Pose IdlePose()
        {
            if (_hasClip)
            {
                return default;
            }

            float breath = Mathf.Sin(Time.time * BREATH_RATE);
            return new Pose
            {
                SpinePitch = breath * BREATH_PITCH,
                Lift       = (breath + 1f) * HALF * BREATH_LIFT
            };
        }

        // 예고가 끝나는 순간의 자세, 모양마다 다르게 모아서 어떤 공격인지 몸으로도 읽히게 한다
        private Pose GatherPose(EBossTelegraphShape shape, string patternId)
        {
            Vector3 values = _gatherSideArms;

            if (shape == EBossTelegraphShape.Circle)
            {
                // 장판(P05)도 원형 예고를 쓰지만 팔을 앞으로 뻗어 지정 위치를 가리킨다
                values = IsAreaPattern(patternId) ? _gatherThrow : _gatherCircle;
            }
            else if (shape == EBossTelegraphShape.Projectile)
            {
                values = _gatherThrow;
            }

            return new Pose
            {
                PivotPitch = (shape == EBossTelegraphShape.Dash) ? DASH_WINDUP_PITCH : 0f,
                SpinePitch = values.x,
                HeadPitch  = values.x * -HALF,
                ArmSide    = values.y,
                ArmForward = values.z,
                Lift       = BREATH_LIFT
            };
        }

        // 발동 순간의 자세, 모으던 반대 방향으로 튕겨 타격이 나가는 순간을 강조한다
        private Pose ReleasePose(EBossTelegraphShape shape, string patternId)
        {
            Vector3 values = _gatherSideArms;

            if (shape == EBossTelegraphShape.Circle)
            {
                values = IsAreaPattern(patternId) ? _gatherThrow : _gatherCircle;
            }
            else if (shape == EBossTelegraphShape.Projectile)
            {
                values = _gatherThrow;
            }

            return new Pose
            {
                SpinePitch = -values.x + (values.x * -HALF),
                HeadPitch  = values.x * HALF,
                ArmSide    = values.y * HALF,
                ArmForward = values.z * RELEASE_ARM_SCALE,
                Lift       = -BREATH_LIFT
            };
        }

        private static bool IsAreaPattern(string patternId)
        {
            return (patternId == AREA_PATTERN_ID) || (patternId == AREA_PHASE_TWO_ID);
        }

        private Pose DashPose()
        {
            return new Pose
            {
                PivotPitch = DASH_PIVOT_PITCH,
                SpinePitch = DASH_SPINE_PITCH,
                HeadPitch  = DASH_HEAD_PITCH,
                ArmSide    = DASH_ARM_SIDE,
                ArmForward = DASH_ARM_FORWARD,
                Lift       = 0f
            };
        }

        private Pose DownPose()
        {
            return new Pose
            {
                PivotPitch = DOWN_PITCH,
                ArmSide    = DOWN_ARM_SIDE,
                Lift       = DOWN_LIFT
            };
        }

        /****************************************
        *               Bones
        ****************************************/

        private void CacheBones()
        {
            _bones.Clear();

            _spine    = Cache(HumanBodyBones.Spine);
            _chest    = Cache(HumanBodyBones.Chest);
            _head     = Cache(HumanBodyBones.Head);
            _leftArm  = Cache(HumanBodyBones.LeftUpperArm);
            _rightArm = Cache(HumanBodyBones.RightUpperArm);

            _hasBones = (_spine.Bone != null) && (_leftArm.Bone != null) && (_rightArm.Bone != null);

            // 컨트롤러가 없으면 아무도 본을 되돌리지 않으므로 매 프레임 기본 자세에서 시작한다.
            // 컨트롤러가 붙으면 그 포즈 위에 덧붙인다
            _resetsBind = _animator.runtimeAnimatorController == null;
            _hasClip    = !_resetsBind;
        }

        private BoneRef Cache(HumanBodyBones id)
        {
            Transform bone = _animator.GetBoneTransform(id);
            var       item = new BoneRef { Bone = bone, Bind = (bone != null) ? bone.localRotation : Quaternion.identity };

            if (bone != null)
            {
                _bones.Add(item);
            }

            return item;
        }

        private void ApplyPose(in Pose pose)
        {
            _pivot.localRotation = Quaternion.Euler(pose.PivotPitch, 0f, 0f);
            _pivot.localPosition = new Vector3(0f, pose.Lift, 0f);

            if (_resetsBind)
            {
                for (int i = 0; i < _bones.Count; ++i)
                {
                    _bones[i].Bone.localRotation = _bones[i].Bind;
                }
            }

            Vector3 right   = transform.right;
            Vector3 forward = transform.forward;

            // 위에서 아래로 부모 먼저 돌려야 자식의 세계 회전에 부모 회전이 이미 반영돼 있다
            if (_chest.Bone != null)
            {
                Rotate(_spine.Bone, right, pose.SpinePitch * HALF);
                Rotate(_chest.Bone, right, pose.SpinePitch * HALF);
            }
            else
            {
                Rotate(_spine.Bone, right, pose.SpinePitch);
            }

            Rotate(_head.Bone, right, pose.HeadPitch);

            // 휴머노이드가 앞을 보고 서 있을 때 오른팔은 몸의 오른쪽에 있어 앞 축 둘레 양의 회전이 팔을 옆으로 올린다
            Rotate(_rightArm.Bone, forward, pose.ArmSide);
            Rotate(_leftArm.Bone, forward, -pose.ArmSide);
            Rotate(_rightArm.Bone, right, -pose.ArmForward);
            Rotate(_leftArm.Bone, right, -pose.ArmForward);
        }

        // 본을 세계 축 둘레로 돌린다, 본이 없거나 각도가 0이면 아무것도 하지 않는다
        private static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if ((bone == null) || (Mathf.Approximately(degrees, 0f)))
            {
                return;
            }

            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /****************************************
        *             Materials
        ****************************************/

        // 외부 모델의 재질은 내장 파이프라인용 Standard 셰이더라 URP에서 분홍색으로 깨진다. 원본 에셋은 고치지 않고
        // 이 인스턴스에만 같은 텍스처를 쓰는 URP Lit 재질을 입힌다. 정식 재질 가공은 아트 제작 절차에서 따로 한다
        // 만든 재질은 created에 담는다, 부른 쪽이 파괴할 책임을 진다. 프리워밍 어댑터가 같은 변환을 쓰려고 공개해 둔다
        /// <summary> 렌더러의 비 URP Lit 재질을 URP Lit 사본으로 바꾼다 </summary>
        public static void ConvertToUrpLit(Renderer[] renderers, List<Material> created)
        {
            Shader lit = Shader.Find(URP_LIT_SHADER);
            if (lit == null)
            {
                return;
            }

            var converted = new Dictionary<Material, Material>();
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; ++i)
                {
                    Material source = materials[i];
                    if ((source == null) || (source.shader == lit))
                    {
                        continue;
                    }

                    if (!converted.TryGetValue(source, out Material target))
                    {
                        target = CreateUrpMaterial(source, lit);
                        converted.Add(source, target);
                        created.Add(target);
                    }

                    materials[i] = target;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private void DestroyRuntimeMaterials()
        {
            for (int i = 0; i < _runtimeMaterials.Count; ++i)
            {
                if (_runtimeMaterials[i] != null)
                {
                    Destroy(_runtimeMaterials[i]);
                }
            }

            _runtimeMaterials.Clear();
        }

        private static Material CreateUrpMaterial(Material source, Shader lit)
        {
            var target = new Material(lit) { name = source.name + "_URP" };

            if (source.HasProperty(PROP_SRC_TEXTURE))
            {
                target.SetTexture(PROP_BASE_MAP, source.GetTexture(PROP_SRC_TEXTURE));
            }

            if (source.HasProperty(PROP_SRC_COLOR))
            {
                target.SetColor(PROP_BASE_COLOR, source.GetColor(PROP_SRC_COLOR));
            }

            if ((source.HasProperty(PROP_BUMP_MAP)) && (source.GetTexture(PROP_BUMP_MAP) != null))
            {
                target.SetTexture(PROP_BUMP_MAP, source.GetTexture(PROP_BUMP_MAP));
                target.EnableKeyword(KEYWORD_NORMAL);
            }

            if (source.HasProperty(PROP_SRC_SMOOTH))
            {
                target.SetFloat(PROP_SMOOTHNESS, source.GetFloat(PROP_SRC_SMOOTH));
            }

            target.SetFloat(PROP_CULL, (float)UnityEngine.Rendering.CullMode.Off);

            // 머리카락과 속눈썹은 알파 테스트 재질이라 같은 방식으로 자른다
            if (source.IsKeywordEnabled(KEYWORD_ALPHATEST))
            {
                target.SetFloat(PROP_ALPHA_CLIP, FLAG_ON);
                target.SetFloat(PROP_CUTOFF, source.HasProperty(PROP_CUTOFF) ? source.GetFloat(PROP_CUTOFF) : ALPHA_CUTOFF_DEFAULT);
                target.EnableKeyword(KEYWORD_ALPHATEST);
            }

            return target;
        }
    }
}
