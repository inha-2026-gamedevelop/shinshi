// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Playables;

// 서드파티
using Unity.Cinemachine;

namespace Shinshi.AI
{
    // 확정 시안(v03)과 같은 Timeline, 카메라 위치, VFX 배치를 보스 한 명 기준 좌표로 묶은 정식 컷신 장비다. 시안 빌더가 시안과 같은 상수로 만든다.
    // 시안은 보스가 원점에서 +Z를 보던 장면이므로, 장비 루트를 실제 보스 위치와 방향에 두면 자식 카메라와 VFX가 그대로 보스 기준이 된다.
    // 원점이나 시안 위치에 고정하지 않고, 실전 보스를 시안 위치로 옮기지도 않는다.
    //
    // 시안 모델과 실제 보스 모델의 크기가 다르면 장비 전체를 같은 비율로 키운다. 정식 PF_WolbaekBoss는 피벗 크기가 2라
    // 장비를 1배로 두면 얼굴 근접 컷의 카메라가 보스 몸 안에 들어간다. 휴머노이드 루트 이동도 모델 크기를 따라 커지므로 떠오르는 높이와 맞는다.
    //
    // 정식 런타임 어셈블리(Shinshi.Runtime)는 Unity.Timeline을 참조하지 않는다. 그래서 트랙 타입 대신 출력 대상 타입으로만 바인딩한다
    /// <summary> 월백 2페이즈 전환 컷신 장비, 실제 보스 Animator와 전투 카메라 브레인에 연결해 서버 경과 시간으로 평가한다 </summary>
    [DisallowMultipleComponent]
    public sealed class WolbaekPhase2TransitionRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_SCALE = 0.01f;

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 컷 카메라 하나와 시안에서 바라보던 대상 </summary>
        [Serializable]
        public struct ShotSetup
        {
            [SerializeField] private CinemachineCamera _camera;
            [SerializeField] private string            _lookBone;     // 시안이 바라보던 뼈 이름, 비면 장비 중심을 본다
            [SerializeField] private HumanBodyBones    _fallbackBone; // 이름이 다른 모델이면 쓰는 휴머노이드 뼈
            [SerializeField] private CinemachineFollow _worldFollow;  // 떠오른 높이에서도 정면을 지키려고 뼈를 월드 축 간격으로 따라가는 컷만 있다
            [SerializeField] private Vector3           _worldOffset;  // 시안 크기와 시안 방향(+Z 정면) 기준 간격

            public CinemachineCamera Camera => _camera;
            public string LookBone => _lookBone;
            public HumanBodyBones FallbackBone => _fallbackBone;
            public CinemachineFollow WorldFollow => _worldFollow;
            public Vector3 WorldOffset => _worldOffset;

            public ShotSetup(CinemachineCamera camera, string lookBone, HumanBodyBones fallbackBone, CinemachineFollow worldFollow, Vector3 worldOffset)
            {
                _camera       = camera;
                _lookBone     = lookBone;
                _fallbackBone = fallbackBone;
                _worldFollow  = worldFollow;
                _worldOffset  = worldOffset;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayableDirector _director;
        [SerializeField] private Transform        _center;                    // 전장 풀샷이 바라보는 점, 시안의 ArenaCenter
        [SerializeField] private ShotSetup[]      _shots = new ShotSetup[0];
        [SerializeField] private float            _referenceModelScale = 1f; // 시안 모델 Animator의 월드 크기

        [Header("시작 보호막")]
        [SerializeField] private Transform        _ownShieldRoot;            // 컷신 자체 보호막 인스턴스의 부모, 정식 보호막을 넘겨받으면 끈다
        [SerializeField] private float            _shieldFadeStart;          // 아래 셋은 확정 v03 보호막 클립과 같은 값이다(빌더가 같은 상수로 넣는다)
        [SerializeField] private float            _shieldFadeEnd;
        [SerializeField] private float            _shieldEnd;

        private float _appliedScale = 1f;

        /****************************************
        *              Properties
        ****************************************/

        public PlayableDirector Director => _director;

        /// <summary> Timeline 길이(초), 서버 구간 길이와 다를 수 있다 </summary>
        public double Duration => (_director != null) ? _director.duration : 0d;

        /// <summary> 실제 보스 크기에 맞춰 장비에 곱한 비율 </summary>
        public float AppliedScale => _appliedScale;

        public int ShotCount => _shots.Length;

        public bool IsGraphValid => (_director != null) && _director.playableGraph.IsValid();

        /// <summary> 시작 보호막이 끝나는 Timeline 시각(초), 이 뒤로는 보호막이 없다 </summary>
        public float ShieldEnd => _shieldEnd;

        /// <summary> Timeline 시각 time의 시작 보호막 투명도. 컷신 보호막 투명도 트랙과 같은 곡선이다 </summary>
        public float ShieldVisibility(double time)
        {
            if (time >= _shieldEnd)
            {
                return 0f;
            }

            if (_shieldFadeEnd <= _shieldFadeStart)
            {
                return (time < _shieldFadeEnd) ? 1f : 0f;
            }

            float progress = Mathf.Clamp01((float)((time - _shieldFadeStart) / (_shieldFadeEnd - _shieldFadeStart)));
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        /****************************************
        *                Methods
        ****************************************/

        // useOwnShield가 거짓이면 정식 보호막을 넘겨받는 경우다. 자체 보호막 부모를 그래프 생성 전에 꺼 두면 Timeline이 인스턴스를 만들고
        // 투명도를 돌려도 화면에는 그리지 않는다. 확정 Timeline 에셋(공유 에셋)의 트랙을 끄지 않으려고 이 방식을 쓴다
        /// <summary> 실제 보스 Animator와 카메라 브레인에 연결하고 그래프를 만든다. brain이 없으면 카메라 컷만 빠진다 </summary>
        public bool Bind(Animator boss, CinemachineBrain brain, bool useOwnShield)
        {
            if ((_director == null) || (_director.playableAsset == null) || (boss == null))
            {
                return false;
            }

            _appliedScale = Mathf.Max(MIN_SCALE, boss.transform.lossyScale.y / Mathf.Max(MIN_SCALE, _referenceModelScale));
            transform.localScale = Vector3.one * _appliedScale;

            BindShots(boss);
            BindOutputs(boss, brain);
            if (_ownShieldRoot != null)
            {
                _ownShieldRoot.gameObject.SetActive(useOwnShield);
            }

            // 서버 경과 시간으로 매 프레임 직접 평가한다. 시안의 반복 재생과 자동 재생은 쓰지 않는다
            _director.playOnAwake       = false;
            _director.timeUpdateMode    = DirectorUpdateMode.Manual;
            _director.extrapolationMode = DirectorWrapMode.None;
            _director.RebuildGraph();
            return true;
        }

        public void Evaluate(double time)
        {
            if (!IsGraphValid)
            {
                return;
            }

            _director.time = time;
            _director.Evaluate();
        }

        // 그래프를 없애면 카메라 덮어쓰기가 풀리고, Animator 출력이 빠지고, ControlTrack이 만든 VFX 인스턴스가 지워진다
        /// <summary> Timeline 그래프를 없앤다. 장비 오브젝트와 카메라는 남는다 </summary>
        public void StopGraph()
        {
            if (_director == null)
            {
                return;
            }

            _director.Stop();
        }

        private void BindShots(Animator boss)
        {
            Transform model = boss.transform;
            for (int i = 0; i < _shots.Length; ++i)
            {
                ShotSetup shot = _shots[i];
                if (shot.Camera == null)
                {
                    continue;
                }

                Transform look = ResolveLook(boss, shot);
                shot.Camera.Target.TrackingTarget = look;

                if (shot.WorldFollow != null)
                {
                    shot.WorldFollow.FollowOffset = transform.rotation * (shot.WorldOffset * _appliedScale);
                }
            }
        }

        private Transform ResolveLook(Animator boss, ShotSetup shot)
        {
            if (string.IsNullOrEmpty(shot.LookBone))
            {
                return (_center != null) ? _center : transform;
            }

            Transform bone = FindChild(boss.transform, shot.LookBone);
            if ((bone == null) && boss.isHuman)
            {
                bone = boss.GetBoneTransform(shot.FallbackBone);
            }

            return (bone != null) ? bone : boss.transform;
        }

        private void BindOutputs(Animator boss, CinemachineBrain brain)
        {
            foreach (PlayableBinding output in _director.playableAsset.outputs)
            {
                if ((output.sourceObject == null) || (output.outputTargetType == null))
                {
                    continue;
                }

                if (output.outputTargetType == typeof(Animator))
                {
                    _director.SetGenericBinding(output.sourceObject, boss);
                }
                else if (output.outputTargetType == typeof(CinemachineBrain))
                {
                    _director.SetGenericBinding(output.sourceObject, brain);
                }
            }
        }

        // 컷신 시작 때 한 번만 부른다
        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        /// <summary> 시안 빌더가 장비 프리팹을 만들 때만 쓴다 </summary>
        public void EditorConfigure(PlayableDirector director, Transform center, ShotSetup[] shots, float referenceModelScale,
            Transform ownShieldRoot, float shieldFadeStart, float shieldFadeEnd, float shieldEnd)
        {
            _director            = director;
            _center              = center;
            _shots               = shots;
            _referenceModelScale = referenceModelScale;
            _ownShieldRoot       = ownShieldRoot;
            _shieldFadeStart     = shieldFadeStart;
            _shieldFadeEnd       = shieldFadeEnd;
            _shieldEnd           = shieldEnd;
        }
#endif
    }
}
