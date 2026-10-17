// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

// 서드파티
using Unity.Cinemachine;

// 프로젝트
using Shinshi.AI;
using Shinshi.AI.Preview;

namespace Shinshi.Editor
{
    // 월백 2페이즈 전환 컷신 첫 시안(약 4.6초, 5초 반복)을 Timeline과 Cinemachine으로 만든다
    // 1 자세를 낮춘 월백과 흩어지는 보호막, 2 손에서 얼굴로 카메라 이동, 3 플레이어를 보며 멈춘 뒤 일어남, 4 부적 전개와 전장 노출
    // 전용 무릎 꿇기 유지 모션이 없어 Appear의 한쪽 무릎 구간(1.40~1.57초)을 느리게 재생해 대신하고, 일어나며 팔을 펼치는 Appear 후반을 부적 전개 손짓으로 쓴다
    // 열린 씬을 저장하거나 닫지 않도록 새 씬을 추가 모드로 만들어 그 씬만 저장한 뒤 닫는다
    // 서버 시간, 패턴, 판정, 정식 프리팹은 참조하거나 바꾸지 않는다
    /// <summary> 월백 2페이즈 전환 시안 씬과 Timeline을 만든다 </summary>
    public static class WolbaekPhase2TransitionBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float TIMELINE_LENGTH = 5.0f;

        private const string ROOT_NAME     = "WolbaekPhase2TransitionPreview";
        private const string DIRECTOR_NAME = "Phase2Transition Director";
        private const string HEAD_BONE     = "head";
        private const string HAND_BONE     = "hand_r";
        private const string CHEST_BONE    = "spine_03";
        private const string PELVIS_BONE   = "pelvis";

        private const string IDLE_PLAYER_CLIP = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/Clips/AN_P09_S_Idle.anim";
        private const string FLOOR_MATERIAL   = "Assets/00.Scenes/Dev_VFX/M_Dev_GroundDarkGrass.mat";

        private const string SHIELD_VFX      = "Assets/99.ThirdParty/VFX/Wolbaek/Blue/SB_Shield_Blue.prefab";
        private const string SHIELD_BREAK    = "Assets/99.ThirdParty/VFX/Wolbaek/Blue/SB_Shockwave_Pulses_Blue.prefab";
        private const string RUNE_FALL_VFX   = "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Rune_Fall.prefab";
        private const string VORTEX_VFX      = "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Vortex.prefab";

        // v02 A는 보호막과 같은 파란 계열로 맞춘다. 이미 만들어 둔 파란 사본을 참조만 하고 원본과 사본은 수정하지 않는다
        private const string RUNE_FALL_BLUE_VFX = "Assets/99.ThirdParty/VFX/Wolbaek/Blue/SB_Rune_Fall_Blue.prefab";
        private const string VORTEX_BLUE_VFX    = "Assets/99.ThirdParty/VFX/Wolbaek/Blue/SB_Vortex_Blue.prefab";

        // 월백 전장 지름 36m(전투 설계), 바닥 원판 두께
        private const float ARENA_DIAMETER  = 36f;
        private const float ARENA_THICKNESS = 0.05f;
        private const float LIGHT_INTENSITY = 1.25f;
        private const float DEFAULT_BLEND   = 0.5f;
        private const float SHIELD_SCALE    = 0.55f;

        // v02 모션 배치. 배속은 원본 대비 재생 속도다
        private const float MOTION_BLEND          = 0.2f;
        private const float V02A_START            = 1.44f;
        private const float V02A_SPEED            = 0.4f;

        // 카메라 컷과 VFX 시각은 attack12를 0.5배로 재생할 때 자세에 맞춰 정했다. 더 느리게 재생하면 이 구간을 같은 비율로 늘린다
        private const float V02A_REFERENCE_SPEED  = 0.5f;
        private const float ATTACK12_LENGTH_HINT  = 1.43f;

        // Rune_Fall은 시작 후 약 0.65초에 폭발한다. 늘어난 시간에서도 폭발이 펼침 순간에 오도록 이 시점을 기준으로 옮긴다
        private const float RUNE_FALL_BURST_DELAY = 0.65f;
        private const float V02B_PREPARE_START    = 1.44f;
        private const float V02B_PREPARE_FROM     = 0f;
        private const float V02B_PREPARE_TO       = 0.86f;
        private const float V02B_PREPARE_SPEED    = 0.5f;
        private const float V02B_DEPLOY_FROM      = 0.27f;
        private const float V02B_DEPLOY_TO        = 0.88f;
        private const float V02B_DEPLOY_SPEED     = 0.6f;
        private const float V02B_JOIN_BLEND       = 0.2f;
        private const float V02B_EXIT_BLEND       = 0.4f;

        // v03 공중 전환. 그로기에서 대기로 넘어가 천천히 떠오르고, 떠 있는 채 상체만 attack12로 손을 모은 뒤
        // Rune_Fall 오브가 떨어지는 순간 함께 내려꽂혀 지면 폭발과 착지를 맞춘다
        // 떠오름은 정식 AC_Wolbaek의 Combo_Up_Normal(move_goingUp), 하강은 미리보기 컨트롤러의 Combo_Down_Dynamic(move_goingDown_dynamic)이다
        private const string UP_NORMAL_GUID    = "3356dd556ba4c5f4caaa2e60b6aa82b8";
        private const string DOWN_DYNAMIC_GUID = "0d79a4e930a1004488c12edb991c2bd5";
        private const string GROGGY02_GUID     = "adc08c5f3d86f9645ae03bef41245edc";
        private const string UPPER_MASK_PATH   = WolbaekPhase2TransitionAssets.TIMELINE_DIR + "/AM_WolbaekUpperBody_Preview.mask";

        private const float V03_LENGTH          = 9.6f;
        private const float V03_GROGGY_END      = 2.1f;
        private const float V03_IDLE_START      = 1.5f;
        private const float V03_UP_START        = 2.8f;
        private const float V03_UP_SPEED        = 0.4f;
        private const float V03_UP_BLEND        = 0.4f;

        // 끝까지 떠오르면 머리(약 3.9m)가 오브(약 3.9m)와 겹친다. 골반 2.7m에서 멈추면 머리 위로 모은 손이 오브에 닿는다
        private const float V03_UP_HOLD_FROM    = 0.70f;
        private const float V03_UP_HOLD_TO      = 0.74f;

        // 상체 attack12는 0.4배. 0.86초(손을 머리 위에서 모은 끝)에서 팔을 아래로 풀며 하강을 시작한다
        private const float V03_GATHER_START    = 4.4f;
        private const float V03_GATHER_SPEED    = 0.4f;
        private const float V03_GATHER_RELEASE  = 0.86f;
        private const float V03_GATHER_TO       = 1.08f;
        private const float V03_GATHER_EASE_IN  = 0.6f;
        private const float V03_GATHER_EASE_OUT = 0.4f;

        // 카메라 컷 시작 시각. 그로기(낮은 와이드), 일어서며 떠오르기 시작(손 옆), 떠오른 얼굴(얼굴), 오브가 생기는 모습(플레이어 너머), 내려꽂힘(중간)
        private const float V03_CUT_HAND        = 2.1f;
        private const float V03_CUT_FACE        = 3.0f;
        private const float V03_CUT_HAND_END    = 3.7f;
        private const float V03_CUT_OVER        = 5.5f;
        private const float V03_CUT_MID         = 6.1f;

        // goingDown_dynamic에서 골반이 2.7m를 지나는 0.36초부터 쓴다. 0.667초가 착지, 0.8초가 끝
        private const float V03_DOWN_FROM       = 0.36f;
        private const float V03_DOWN_LAND       = 0.667f;
        private const float V03_DOWN_TO         = 0.80f;
        private const float V03_DOWN_BLEND      = 0.08f;

        // 오브는 1.82초에 떨어지기 시작해 2.0초에 닿는다(약 0.18초). 하강도 1.5배로 약 0.2초에 맞춰 함께 내려오게 한다
        private const float V03_DOWN_SPEED      = 1.5f;

        // Rune_Fall은 0.8초에 오브가 약 3.9m에 생기고 1.9~2.0초에 떨어져 2.0초에 지면에서 터진다
        private const float RUNE_FALL_IMPACT    = 2.0f;
        private const float V03_OVER_HEIGHT     = 2.9f;

        // 시작 보호막은 기존 0~0.75초 클립 안에서 0.1초부터 흐려져 0.7초에 보이지 않는다. 클립 끝(0.75초)에 한 프레임 만에 꺼지던 것을 대신한다
        private const float V03_SHIELD_END        = 0.75f;
        private const float V03_SHIELD_FADE_START = 0.1f;
        private const float V03_SHIELD_FADE_END   = 0.7f;

        // 바라보는 뼈(얼굴 컷은 머리, 중간 컷은 가슴)에서 카메라까지의 월드 간격. 높이 차를 거의 0으로 두어 정면에서 찍는다
        // 전투용 컷신 장비(시안과 같은 상수로 만들고, 보스 기준 좌표와 서버 시각으로 재생한다)
        private const string COMBAT_DIR          = "Assets/01.Scripts/03.AI/Boss/Cutscene/Phase2Transition";
        private const string COMBAT_TIMELINE     = COMBAT_DIR + "/TL_WolbaekPhase2Transition_Combat.playable";
        private const string COMBAT_RIG_PREFAB   = COMBAT_DIR + "/PF_WolbaekPhase2TransitionRig.prefab";
        private const string COMBAT_RIG_NAME     = "PF_WolbaekPhase2TransitionRig";
        private const int    COMBAT_CAMERA_PRIORITY = -1000; // 전투 카메라(우선순위 0)보다 낮아 Timeline 덮어쓰기 밖에서는 화면을 잡지 않는다

        private static readonly Vector3 G_V03FaceOffset = new Vector3(0.25f, -0.05f, 1.55f);
        private static readonly Vector3 G_V03MidOffset  = new Vector3(-2.9f, 0.05f, 4.2f);

        private static readonly Vector3 G_LightEuler   = new Vector3(38f, 210f, 0f);
        private static readonly Vector3 G_PlayerMain   = new Vector3(0f, 0f, 4.6f);
        private static readonly Vector3 G_PlayerLeft   = new Vector3(-3.6f, 0f, 5.6f);
        private static readonly Vector3 G_PlayerRight  = new Vector3(3.9f, 0f, 5.2f);

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 같은 카메라와 VFX에 모션만 다른 시안 </summary>
        public enum EVariant
        {
            V01Appear    = 0,
            V02ASingle   = 1,
            V02BTwoClips = 2,
            V03Float     = 3
        }

        // 카메라 한 개의 위치, 화각, 바라볼 뼈 이름(비어 있으면 전장 중심)
        private readonly struct ShotSpec
        {
            public readonly string  Name;
            public readonly Vector3 Position;
            public readonly float   Fov;
            public readonly string  LookBone;

            public ShotSpec(string name, Vector3 position, float fov, string lookBone)
            {
                Name     = name;
                Position = position;
                Fov      = fov;
                LookBone = lookBone;
            }
        }

        // 타임라인 위 클립 한 개의 시작, 끝, 앞 클립과 겹쳐 섞는 시간
        private readonly struct Span
        {
            public readonly float Start;
            public readonly float End;
            public readonly float BlendIn;

            public Span(float start, float end, float blendIn)
            {
                Start   = start;
                End     = end;
                BlendIn = blendIn;
            }
        }

        // 기준 시안(5초) 시각을 실제 타임라인 시각으로 옮긴다
        // 모션 시작 전은 그대로, 모션 구간은 배속 비율만큼 늘리고, 그 뒤는 늘어난 만큼 민다
        private sealed class TimeMap
        {
            private readonly float _pivot;
            private readonly float _referenceEnd;
            private readonly float _scale;

            public static readonly TimeMap Identity = new TimeMap(0f, 0f, 1f);

            public float Length => Map(TIMELINE_LENGTH);

            public TimeMap(float pivot, float referenceDuration, float scale)
            {
                _pivot = pivot;
                _referenceEnd = pivot + referenceDuration;
                _scale = scale;
            }

            public float Map(float t)
            {
                if (t <= _pivot)
                {
                    return t;
                }

                if (t <= _referenceEnd)
                {
                    return _pivot + ((t - _pivot) * _scale);
                }

                return _pivot + ((_referenceEnd - _pivot) * _scale) + (t - _referenceEnd);
            }

            // 블렌드 길이도 블렌드가 걸친 구간에 맞춰 늘린다
            public Span Map(Span span)
            {
                float start = Map(span.Start);
                return new Span(start, Map(span.End), start - Map(span.Start - span.BlendIn));
            }
        }

        private static TimeMap MapFor(EVariant variant)
        {
            if (variant != EVariant.V02ASingle)
            {
                return TimeMap.Identity;
            }

            AnimationClip gather = WolbaekPhase2TransitionAssets.LoadSuccubusClip("attack12");
            float length = (gather != null) ? gather.length : ATTACK12_LENGTH_HINT;
            return new TimeMap(V02A_START, length / V02A_REFERENCE_SPEED, V02A_REFERENCE_SPEED / V02A_SPEED);
        }

        private static readonly ShotSpec G_LowWide = new ShotSpec("CM_01_LowShieldBreak", new Vector3(2.7f, 0.55f, 3.3f), 34f, PELVIS_BONE);
        private static readonly ShotSpec G_Hand    = new ShotSpec("CM_02_HandSide",       new Vector3(-1.45f, 0.55f, 1.1f), 30f, HAND_BONE);
        private static readonly ShotSpec G_Face    = new ShotSpec("CM_03_FaceCloseUp",    new Vector3(0.25f, 0.85f, 1.55f), 26f, HEAD_BONE);
        private static readonly ShotSpec G_Over    = new ShotSpec("CM_04_OverPlayer",     new Vector3(0.55f, 1.55f, 5.7f), 30f, CHEST_BONE);
        private static readonly ShotSpec G_Mid     = new ShotSpec("CM_05_DeployMid",      new Vector3(-2.9f, 1.35f, 4.2f), 40f, CHEST_BONE);
        private static readonly ShotSpec G_Wide    = new ShotSpec("CM_06_ArenaPullOut",   new Vector3(-6.5f, 10.5f, 15.5f), 46f, string.Empty);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "2. Build Preview Scene (v01 Appear)")]
        public static void Build()
        {
            Build(EVariant.V01Appear);
        }

        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "2a. Build v02 A (attack12 single)")]
        public static void BuildV02A()
        {
            Build(EVariant.V02ASingle);
        }

        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "2b. Build v02 B (attack12 + attack09)")]
        public static void BuildV02B()
        {
            Build(EVariant.V02BTwoClips);
        }

        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "2c. Build v03 (float, gather, slam)")]
        public static void BuildV03()
        {
            Build(EVariant.V03Float);
        }

        public static string ScenePath(EVariant variant)
        {
            return WolbaekPhase2TransitionAssets.SCENE_PATH.Replace(".unity", Suffix(variant) + ".unity");
        }

        public static string TimelinePath(EVariant variant)
        {
            return WolbaekPhase2TransitionAssets.TIMELINE_PATH.Replace(".playable", Suffix(variant) + ".playable");
        }

        private static string Suffix(EVariant variant)
        {
            switch (variant)
            {
                case EVariant.V02ASingle:
                    return "_v02A";
                case EVariant.V02BTwoClips:
                    return "_v02B";
                case EVariant.V03Float:
                    return "_v03";
                default:
                    return string.Empty;
            }
        }

        private static void Build(EVariant variant)
        {
            string scenePath = ScenePath(variant);
            if (File.Exists(Path.Combine(WolbaekPhase2TransitionAssets.ProjectRoot(), scenePath)))
            {
                bool isOverwrite = EditorUtility.DisplayDialog(
                    "월백 2페이즈 전환 시안",
                    $"{Path.GetFileName(scenePath)}과 Timeline이 이미 있습니다. 다시 만들면 씬에서 손으로 바꾼 내용이 사라집니다. 덮어쓸까요?",
                    "덮어쓰기",
                    "취소");
                if (!isOverwrite)
                {
                    return;
                }
            }

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                BuildContents(scene, variant);
                EditorSceneManager.SaveScene(scene, scenePath);
                Debug.Log($"[P2Transition] 시안 씬 저장 {scenePath}, Timeline {TimelinePath(variant)}");
            }
            finally
            {
                if (previous.IsValid())
                {
                    SceneManager.SetActiveScene(previous);
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // 확정 v03과 같은 상수와 같은 트랙 생성 코드로 전투용 Timeline과 장비 프리팹을 만든다. 시안 씬, 바닥, 조명, 플레이어 모형, 테스트 UI는 넣지 않는다.
        // 보스 Animator와 카메라 브레인은 비워 두고 재생할 때 실제 보스와 전투 카메라에 연결한다. 시안처럼 반복하거나 자동 재생하지 않는다.
        // 열린 편집 씬을 건드리지 않도록 미리보기 씬에서 만들고 프리팹으로 저장한 뒤 닫는다
        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "5. Build Combat Cutscene Rig (v03)")]
        public static void BuildCombatRig()
        {
            if (File.Exists(Path.Combine(WolbaekPhase2TransitionAssets.ProjectRoot(), COMBAT_RIG_PREFAB)))
            {
                bool isOverwrite = EditorUtility.DisplayDialog(
                    "월백 2페이즈 전환 전투용 컷신",
                    $"{Path.GetFileName(COMBAT_RIG_PREFAB)}과 Timeline을 v03 상수로 다시 만듭니다. 손으로 바꾼 내용이 사라집니다. 덮어쓸까요?",
                    "덮어쓰기",
                    "취소");
                if (!isOverwrite)
                {
                    return;
                }
            }

            EnsureFolder(COMBAT_DIR);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject(COMBAT_RIG_NAME);
                SceneManager.MoveGameObjectToScene(root, scene);
                TimelineAsset timeline = BuildCombatRigContents(scene, root);

                // 프로젝트 전체 SaveAssets는 다른 작업의 저장 안 된 에셋까지 써 버리므로 이 Timeline과 프리팹만 저장한다
                AssetDatabase.SaveAssetIfDirty(timeline);
                PrefabUtility.SaveAsPrefabAsset(root, COMBAT_RIG_PREFAB);
                Debug.Log($"[P2Transition] 전투용 컷신 장비 저장 {COMBAT_RIG_PREFAB}, Timeline {COMBAT_TIMELINE}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static string CombatRigPrefabPath => COMBAT_RIG_PREFAB;

        private static TimelineAsset BuildCombatRigContents(Scene scene, GameObject root)
        {
            Transform center = new GameObject("Center (전장 풀샷 시선)").transform;
            center.SetParent(root.transform, false);
            center.localPosition = new Vector3(0f, 0.6f, 0f);

            Transform vfxRoot = new GameObject("VfxRoot (Timeline Control 생성 위치)").transform;
            vfxRoot.SetParent(root.transform, false);

            Transform shieldRoot = new GameObject("ShieldRoot (보호막 크기 조절)").transform;
            shieldRoot.SetParent(root.transform, false);
            shieldRoot.localScale = Vector3.one * SHIELD_SCALE;

            Transform cameras = new GameObject("Cameras (전투 카메라보다 낮은 우선순위)").transform;
            cameras.SetParent(root.transform, false);

            ShotSpec over = new ShotSpec(G_Over.Name, new Vector3(G_Over.Position.x, V03_OVER_HEIGHT, G_Over.Position.z), G_Over.Fov, G_Over.LookBone);
            ShotSpec[] specs = { G_LowWide, G_Hand, G_Face, over, G_Mid, G_Wide };
            Dictionary<string, CinemachineCamera> shots = new Dictionary<string, CinemachineCamera>();
            WolbaekPhase2TransitionRig.ShotSetup[] setups = new WolbaekPhase2TransitionRig.ShotSetup[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                CinemachineCamera shot = CreateCombatShot(cameras, specs[i], center);
                shots[specs[i].Name] = shot;

                CinemachineFollow follow = null;
                Vector3 offset = Vector3.zero;
                if (specs[i].Name == G_Face.Name)
                {
                    offset = G_V03FaceOffset;
                }
                else if (specs[i].Name == G_Mid.Name)
                {
                    offset = G_V03MidOffset;
                }

                if (offset != Vector3.zero)
                {
                    follow = AddCombatWorldFollow(shot, offset);
                }

                setups[i] = new WolbaekPhase2TransitionRig.ShotSetup(shot, specs[i].LookBone, FallbackBone(specs[i].LookBone), follow, offset);
            }

            GameObject directorObject = new GameObject("Director (서버 경과 시간으로 직접 평가)");
            directorObject.transform.SetParent(root.transform, false);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            TimelineAsset timeline = CreateTimelineAsset(COMBAT_TIMELINE, V03_LENGTH);
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            director.timeUpdateMode = DirectorUpdateMode.Manual;

            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "Wolbaek Motion");
            track.trackOffset = TrackOffset.ApplySceneOffsets;
            AnimationClip idle = WolbaekPhase2TransitionAssets.LoadByGuid<AnimationClip>(WolbaekPhase2TransitionAssets.IDLE_CLIP_GUID);
            BuildV03(timeline, track, idle, V03_LENGTH);
            BuildCameraTrackV03(timeline, director, null, shots);
            Transform ownShield = BuildVfxTracksV03(timeline, director, vfxRoot.gameObject, shieldRoot.gameObject, true);
            EditorUtility.SetDirty(timeline);

            // 정식 보호막을 넘겨받으면 런타임이 ownShield를 끈다. 이어 가는 투명도 곡선은 보호막 클립과 같은 상수로 넣는다
            WolbaekPhase2TransitionRig rig = root.AddComponent<WolbaekPhase2TransitionRig>();
            rig.EditorConfigure(director, center, setups, MeasurePreviewModelScale(scene),
                ownShield, V03_SHIELD_FADE_START, V03_SHIELD_FADE_END, V03_SHIELD_END);
            return timeline;
        }

        // 시안이 쓴 모델 프리팹을 같은 방식으로 놓고 Animator의 월드 크기를 잰다. 실제 보스 크기를 이 값으로 나눠 장비 비율을 정한다
        private static float MeasurePreviewModelScale(Scene scene)
        {
            Animator model = PreviewStage.SpawnWolbaek(scene, Vector3.zero, Quaternion.identity);
            if (model == null)
            {
                Debug.LogWarning("[P2Transition] 시안 모델을 찾지 못해 기준 크기를 1로 둔다");
                return 1f;
            }

            float scale = model.transform.lossyScale.y;
            GameObject instance = PrefabUtility.GetOutermostPrefabInstanceRoot(model.gameObject);
            Object.DestroyImmediate((instance != null) ? instance : model.gameObject);
            return scale;
        }

        private static CinemachineCamera CreateCombatShot(Transform parent, ShotSpec spec, Transform center)
        {
            GameObject cameraObject = new GameObject(spec.Name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = spec.Position;
            cameraObject.transform.localRotation = Quaternion.LookRotation(center.localPosition - spec.Position);
            CinemachineCamera shot = cameraObject.AddComponent<CinemachineCamera>();
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = spec.Fov;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = 300f;
            shot.Lens = lens;

            PrioritySettings priority = shot.Priority;
            priority.Enabled = true;
            priority.Value = COMBAT_CAMERA_PRIORITY;
            shot.Priority = priority;

            cameraObject.AddComponent<CinemachineHardLookAt>();
            return shot;
        }

        private static CinemachineFollow AddCombatWorldFollow(CinemachineCamera shot, Vector3 offset)
        {
            CinemachineFollow follow = shot.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = offset;
            Unity.Cinemachine.TargetTracking.TrackerSettings tracker = follow.TrackerSettings;
            tracker.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            tracker.PositionDamping = Vector3.zero;
            follow.TrackerSettings = tracker;
            return follow;
        }

        private static HumanBodyBones FallbackBone(string boneName)
        {
            switch (boneName)
            {
                case HEAD_BONE:
                    return HumanBodyBones.Head;
                case HAND_BONE:
                    return HumanBodyBones.RightHand;
                case CHEST_BONE:
                    return HumanBodyBones.UpperChest;
                case PELVIS_BONE:
                    return HumanBodyBones.Hips;
                default:
                    return HumanBodyBones.Hips;
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static void BuildContents(Scene scene, EVariant variant)
        {
            GameObject root = new GameObject(ROOT_NAME);

            CreateLight(root.transform);
            CreateArena(root.transform);

            Transform center = new GameObject("ArenaCenter").transform;
            center.SetParent(root.transform, false);
            center.localPosition = new Vector3(0f, 0.6f, 0f);

            Transform vfxRoot = new GameObject("VfxRoot (Timeline Control 생성 위치)").transform;
            vfxRoot.SetParent(root.transform, false);

            // 기믹 보호막 원본은 반경이 커서 근접 카메라를 덮는다. 시안에서만 부모 크기로 줄인다
            Transform shieldRoot = new GameObject("ShieldRoot (보호막 크기 조절)").transform;
            shieldRoot.SetParent(root.transform, false);
            shieldRoot.localScale = Vector3.one * SHIELD_SCALE;

            Animator wolbaek = PreviewStage.SpawnWolbaek(scene, Vector3.zero, Quaternion.identity);
            GameObject wolbaekRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(wolbaek.gameObject);
            if (wolbaekRoot == null)
            {
                wolbaekRoot = wolbaek.gameObject;
            }

            wolbaekRoot.transform.SetParent(root.transform, true);
            wolbaekRoot.name = "Wolbaek (모델 프리팹, 정식 PF_WolbaekBoss 아님)";

            Transform players = new GameObject("Players (자리 표시)").transform;
            players.SetParent(root.transform, false);
            Animator mainPlayer = SpawnPlayer(players, G_PlayerMain, "Player_Main");
            Animator leftPlayer = SpawnPlayer(players, G_PlayerLeft, "Player_Left");
            Animator rightPlayer = SpawnPlayer(players, G_PlayerRight, "Player_Right");

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;
            cameraObject.AddComponent<AudioListener>();
            CinemachineBrain brain = cameraObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, DEFAULT_BLEND);

            Transform cameras = new GameObject("Cameras").transform;
            cameras.SetParent(root.transform, false);
            Dictionary<string, CinemachineCamera> shots = new Dictionary<string, CinemachineCamera>();
            bool isFloat = variant == EVariant.V03Float;

            // v03은 떠 있는 동안 낮은 카메라가 턱을 올려다보게 된다. 플레이어 너머 컷은 계속 떠 있는 구간이라 가슴 높이로 올린다
            ShotSpec over = isFloat
                ? new ShotSpec(G_Over.Name, new Vector3(G_Over.Position.x, V03_OVER_HEIGHT, G_Over.Position.z), G_Over.Fov, G_Over.LookBone)
                : G_Over;
            ShotSpec[] specs = { G_LowWide, G_Hand, G_Face, over, G_Mid, G_Wide };
            for (int i = 0; i < specs.Length; ++i)
            {
                shots[specs[i].Name] = CreateShot(cameras, specs[i], wolbaek.transform, center);
            }

            // 떠오름과 하강에 걸친 얼굴, 중간 컷은 바라보는 뼈를 월드 기준 같은 간격으로 따라가 어느 높이에서도 정면 구도를 지킨다
            if (isFloat)
            {
                FollowInWorld(shots[G_Face.Name], G_V03FaceOffset);
                FollowInWorld(shots[G_Mid.Name], G_V03MidOffset);
            }

            cameraObject.transform.SetPositionAndRotation(G_LowWide.Position, shots[G_LowWide.Name].transform.rotation);

            GameObject directorObject = new GameObject(DIRECTOR_NAME);
            directorObject.transform.SetParent(root.transform, false);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();

            TimeMap map = MapFor(variant);
            float length = isFloat ? V03_LENGTH : map.Length;
            TimelineAsset timeline = CreateTimelineAsset(TimelinePath(variant), length);
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Loop;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;

            BuildWolbaekTrack(timeline, director, wolbaek, variant, length);
            BuildPlayerTrack(timeline, director, mainPlayer, "Player_Main Idle", length);
            BuildPlayerTrack(timeline, director, leftPlayer, "Player_Left Idle", length);
            BuildPlayerTrack(timeline, director, rightPlayer, "Player_Right Idle", length);
            if (isFloat)
            {
                BuildCameraTrackV03(timeline, director, brain, shots);
                BuildVfxTracksV03(timeline, director, vfxRoot.gameObject, shieldRoot.gameObject, false);
            }
            else
            {
                BuildCameraTrack(timeline, director, brain, shots, map);
                BuildVfxTracks(timeline, director, vfxRoot.gameObject, shieldRoot.gameObject, variant == EVariant.V02ASingle, map);
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
        }

        private static TimelineAsset CreateTimelineAsset(string path, float length)
        {
            if (!AssetDatabase.IsValidFolder(WolbaekPhase2TransitionAssets.TIMELINE_DIR))
            {
                string parent = Path.GetDirectoryName(WolbaekPhase2TransitionAssets.TIMELINE_DIR).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(WolbaekPhase2TransitionAssets.TIMELINE_DIR));
            }

            AssetDatabase.DeleteAsset(path);
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = Path.GetFileNameWithoutExtension(path);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = length;
            AssetDatabase.CreateAsset(timeline, path);
            return timeline;
        }

        private static void CreateLight(Transform parent)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(G_LightEuler);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = LIGHT_INTENSITY;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateArena(Transform parent)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Arena Floor (지름 36m 자리 표시)";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -ARENA_THICKNESS, 0f);
            floor.transform.localScale = new Vector3(ARENA_DIAMETER, ARENA_THICKNESS, ARENA_DIAMETER);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FLOOR_MATERIAL);
            if (material != null)
            {
                floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        // 플레이어 대기 클립의 루트 값이 씬 위치를 덮어 원점으로 모이는 문제가 있어, 자리 표시 부모를 두고 그 아래에서 원점 기준으로 재생한다
        private static Animator SpawnPlayer(Transform parent, Vector3 position, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekPhase2TransitionAssets.PLAYER_PREFAB_GUID_PATH);
            if (prefab == null)
            {
                return null;
            }

            Transform holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            Vector3 toBoss = -position;
            toBoss.y = 0f;
            holder.SetPositionAndRotation(position, Quaternion.LookRotation(toBoss.normalized));

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
            instance.name = name + " Model (PF_PlayerP09)";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Animator animator = instance.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            return animator;
        }

        private static CinemachineCamera CreateShot(Transform parent, ShotSpec spec, Transform wolbaek, Transform center)
        {
            GameObject cameraObject = new GameObject(spec.Name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = spec.Position;
            CinemachineCamera shot = cameraObject.AddComponent<CinemachineCamera>();
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = spec.Fov;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = 300f;
            shot.Lens = lens;

            Transform look = string.IsNullOrEmpty(spec.LookBone) ? center : FindDeep(wolbaek, spec.LookBone);
            if (look == null)
            {
                look = center;
            }

            shot.Target.TrackingTarget = look;
            cameraObject.AddComponent<CinemachineHardLookAt>();
            cameraObject.transform.rotation = Quaternion.LookRotation(look.position - spec.Position);
            return shot;
        }

        // 편집 모드 캡처는 프레임 간격이 일정하지 않아 감쇠를 두면 결과가 흔들린다. 감쇠 없이 바로 따라간다
        private static void FollowInWorld(CinemachineCamera shot, Vector3 offset)
        {
            Transform target = shot.Target.TrackingTarget;
            if (target == null)
            {
                return;
            }

            CinemachineFollow follow = shot.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = offset;
            Unity.Cinemachine.TargetTracking.TrackerSettings tracker = follow.TrackerSettings;
            tracker.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            tracker.PositionDamping = Vector3.zero;
            follow.TrackerSettings = tracker;
            shot.transform.position = target.position + offset;
            shot.transform.rotation = Quaternion.LookRotation(-offset);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void BuildWolbaekTrack(TimelineAsset timeline, PlayableDirector director, Animator wolbaek, EVariant variant, float length)
        {
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "Wolbaek Motion");
            track.trackOffset = TrackOffset.ApplySceneOffsets;
            director.SetGenericBinding(track, wolbaek);

            AnimationClip idle = WolbaekPhase2TransitionAssets.LoadByGuid<AnimationClip>(WolbaekPhase2TransitionAssets.IDLE_CLIP_GUID);
            switch (variant)
            {
                case EVariant.V02ASingle:
                    BuildV02A(track, idle, length);
                    break;
                case EVariant.V02BTwoClips:
                    BuildV02B(track, idle);
                    break;
                case EVariant.V03Float:
                    BuildV03(timeline, track, idle, length);
                    break;
                default:
                    BuildV01(track, idle);
                    break;
            }
        }

        private static void BuildV01(AnimationTrack track, AnimationClip idle)
        {
            AnimationClip appear = WolbaekPhase2TransitionAssets.LoadSuccubusClip("Appear");

            // Appear 1.40초 이전은 바닥 아래에서 솟아오르는 등장 구간이라 쓰지 않는다
            // 1.40~1.57초는 한쪽 무릎을 꿇고 팔을 옆으로 뻗은 채 고개를 드는 구간이다. 전용 유지 모션이 없어 아주 느리게 재생한다
            AddMotion(track, appear, "1 낮은 자세, 고개 숙임 (Appear 느리게)", new Span(0f, 1.3f, 0f), 1.40f, 1.46f);
            AddMotion(track, appear, "2 손과 고개 듦 (Appear 느리게)", new Span(1.3f, 2.5f, 0f), 1.46f, 1.54f);
            AddMotion(track, appear, "3 플레이어를 보며 멈춤 (Appear 느리게)", new Span(2.5f, 2.9f, 0f), 1.54f, 1.57f);
            AddMotion(track, appear, "3-4 일어나며 팔을 펼침 (Appear)", new Span(2.9f, 3.8f, 0f), 1.57f, 2.30f);
            AddMotion(track, idle, "대기 (AN_Wolbaek_Idle)", new Span(3.7f, TIMELINE_LENGTH, 0.1f), 0f, TIMELINE_LENGTH - 3.7f);
        }

        // A: attack12 한 클립을 느리게 재생한다. 왼손을 머리 위로 올려 모으고(0.07~0.50), 두 손을 머리 위에서 합친 뒤(0.57~0.86)
        //    아래로 펼친다(0.93~1.00). 펼치는 순간이 CM_05 컷과 부적 정렬 VFX 폭발에 오도록 시작 시각을 정했다
        //    0.5배 기준으로 맞춘 카메라와 VFX 시각은 TimeMap이 0.4배 길이에 맞춰 늘린다
        private static void BuildV02A(AnimationTrack track, AnimationClip idle, float length)
        {
            AnimationClip gather = WolbaekPhase2TransitionAssets.LoadSuccubusClip("attack12");
            float end = V02A_START + (gather.length / V02A_SPEED);
            AddMotion(track, idle, "1 보호막이 깨지는 동안 서서 대기 (AN_Wolbaek_Idle)", new Span(0f, V02A_START + MOTION_BLEND, 0f), 0f, V02A_START + MOTION_BLEND);
            AddMotion(track, gather, $"2-4 손을 올려 모으고 펼침 (attack12 {V02A_SPEED:0.0#}배)", new Span(V02A_START, end, MOTION_BLEND), 0f, gather.length);
            AddMotion(track, idle, "대기 (AN_Wolbaek_Idle)", new Span(end - MOTION_BLEND, length, MOTION_BLEND), 0f, length - end + MOTION_BLEND);
        }

        // B: 준비는 attack12 앞부분(왼손을 올려 머리 위에서 두 손을 모음, 0~0.86초), 전개는 attack09(두 팔을 크게 펼쳐 유지)로 나눈다
        //    combo01_3(가슴 앞 교차)도 시험했지만 CM_03 낮은 얼굴 컷에서 팔이 얼굴을 가려 제외했다
        //    attack09는 0.88초 이후 오른발을 끌며 대기 자세로 돌아가 발이 미끄러져 보이므로 그 전에 대기 클립으로 넘긴다
        private static void BuildV02B(AnimationTrack track, AnimationClip idle)
        {
            AnimationClip prepare = WolbaekPhase2TransitionAssets.LoadSuccubusClip("attack12");
            AnimationClip deploy = WolbaekPhase2TransitionAssets.LoadSuccubusClip("attack09");
            float prepareEnd = V02B_PREPARE_START + ((V02B_PREPARE_TO - V02B_PREPARE_FROM) / V02B_PREPARE_SPEED);
            float deployStart = prepareEnd - V02B_JOIN_BLEND;
            float deployEnd = deployStart + ((V02B_DEPLOY_TO - V02B_DEPLOY_FROM) / V02B_DEPLOY_SPEED);
            AddMotion(track, idle, "1 보호막이 깨지는 동안 서서 대기 (AN_Wolbaek_Idle)", new Span(0f, V02B_PREPARE_START + MOTION_BLEND, 0f), 0f, V02B_PREPARE_START + MOTION_BLEND);
            AddMotion(track, prepare, "2-3 손을 올려 머리 위에서 모음 (attack12 0~0.86, 0.5배)", new Span(V02B_PREPARE_START, prepareEnd, MOTION_BLEND), V02B_PREPARE_FROM, V02B_PREPARE_TO);
            AddMotion(track, deploy, "4 두 팔을 크게 펼침 (attack09 0.27~0.88, 0.6배)", new Span(deployStart, deployEnd, V02B_JOIN_BLEND), V02B_DEPLOY_FROM, V02B_DEPLOY_TO);
            AddMotion(track, idle, "대기 (AN_Wolbaek_Idle)", new Span(deployEnd - V02B_EXIT_BLEND, TIMELINE_LENGTH, V02B_EXIT_BLEND), 0f, TIMELINE_LENGTH - deployEnd + V02B_EXIT_BLEND);
        }

        // v03: 몸 아래(골반 높이, 다리)는 떠오름, 공중 유지, 하강 클립이 정하고, 상체는 위에 겹친 트랙이 attack12로 손을 모은다
        //      겹친 트랙은 상체 마스크라 attack12의 골반 높이를 쓰지 않으므로 땅에 서는 자세의 클립을 떠 있는 채로 쓸 수 있다
        private static void BuildV03(TimelineAsset timeline, AnimationTrack track, AnimationClip idle, float length)
        {
            AnimationClip groggy = LoadClipByGuid(GROGGY02_GUID);
            AnimationClip up = LoadClipByGuid(UP_NORMAL_GUID);
            AnimationClip down = LoadClipByGuid(DOWN_DYNAMIC_GUID);
            AnimationClip gather = WolbaekPhase2TransitionAssets.LoadSuccubusClip("attack12");

            float upEnd = V03_UP_START + (V03_UP_HOLD_FROM / V03_UP_SPEED);
            float downStart = DescentStart();
            float downEnd = downStart + ((V03_DOWN_TO - V03_DOWN_FROM) / V03_DOWN_SPEED);

            float groggyLength = (groggy != null) ? groggy.length : V03_GROGGY_END;
            AddMotion(track, groggy, "1 보호막이 깨진 뒤 그로기 (down_groggy02_loop 느리게)", new Span(0f, V03_GROGGY_END, 0f), 0f, groggyLength);
            AddMotion(track, idle, "2 그로기에서 대기로 (AN_Wolbaek_Idle)", new Span(V03_IDLE_START, V03_UP_START + V03_UP_BLEND, V03_GROGGY_END - V03_IDLE_START), 0f, V03_UP_START + V03_UP_BLEND - V03_IDLE_START);
            AddMotion(track, up, $"3 천천히 떠오름 (move_goingUp 0~0.70, {V03_UP_SPEED:0.0#}배)", new Span(V03_UP_START, upEnd, V03_UP_BLEND), 0f, V03_UP_HOLD_FROM);
            AddMotion(track, up, "4 공중 유지 (move_goingUp 0.70~0.74 정지에 가깝게)", new Span(upEnd - 0.067f, downStart + V03_DOWN_BLEND, 0.067f), V03_UP_HOLD_FROM, V03_UP_HOLD_TO);
            AddMotion(track, down, "5 오브와 함께 내려꽂힘 (move_goingDown_dynamic 0.36~0.80, 1.5배)", new Span(downStart, downEnd, V03_DOWN_BLEND), V03_DOWN_FROM, V03_DOWN_TO);
            AddMotion(track, idle, "6 착지 후 대기 (AN_Wolbaek_Idle)", new Span(downEnd - 0.08f, length, 0.08f), 0f, length - downEnd + 0.08f);

            AnimationTrack upper = timeline.CreateTrack<AnimationTrack>(track, "Wolbaek Upper Body (attack12)");
            upper.avatarMask = UpperBodyMask();
            upper.applyAvatarMask = true;
            float gatherEnd = V03_GATHER_START + (V03_GATHER_TO / V03_GATHER_SPEED);
            AddOverride(upper, gather, $"상체 손을 올려 모으고 펼침 (attack12 0~1.08, {V03_GATHER_SPEED:0.0#}배)", V03_GATHER_START, gatherEnd, 0f, V03_GATHER_TO, V03_GATHER_EASE_IN, V03_GATHER_EASE_OUT);
        }

        // 팔을 풀기 시작하는 attack12 0.86초에 하강을 시작한다
        private static float DescentStart()
        {
            return V03_GATHER_START + (V03_GATHER_RELEASE / V03_GATHER_SPEED);
        }

        // 하강 클립의 착지 시각. Rune_Fall 지면 폭발을 여기에 맞춘다
        private static float ImpactTime()
        {
            return DescentStart() + ((V03_DOWN_LAND - V03_DOWN_FROM) / V03_DOWN_SPEED);
        }

        private static AnimationClip LoadClipByGuid(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : WolbaekPhase2TransitionAssets.LoadClip(path);
        }

        // 상체(척추 위쪽 근육 제외한 머리와 팔, 손)만 남긴 시안 전용 마스크. 골반 위치와 척추, 다리는 아래 트랙을 따른다
        private static AvatarMask UpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UPPER_MASK_PATH);
            if (mask != null)
            {
                return mask;
            }

            mask = new AvatarMask();
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; ++part)
            {
                bool isUpper = (part == AvatarMaskBodyPart.Head)
                    || (part == AvatarMaskBodyPart.LeftArm) || (part == AvatarMaskBodyPart.RightArm)
                    || (part == AvatarMaskBodyPart.LeftFingers) || (part == AvatarMaskBodyPart.RightFingers)
                    || (part == AvatarMaskBodyPart.LeftHandIK) || (part == AvatarMaskBodyPart.RightHandIK);
                mask.SetHumanoidBodyPartActive(part, isUpper);
            }

            AssetDatabase.CreateAsset(mask, UPPER_MASK_PATH);
            return mask;
        }

        private static void AddOverride(AnimationTrack track, AnimationClip clip, string name, float start, float end, float sourceStart, float sourceEnd, float easeIn, float easeOut)
        {
            if (clip == null)
            {
                Debug.LogWarning($"[P2Transition] 클립 없음: {name}");
                return;
            }

            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.displayName = name;
            timelineClip.start = start;
            timelineClip.duration = end - start;
            timelineClip.clipIn = sourceStart;
            timelineClip.timeScale = (sourceEnd - sourceStart) / (end - start);
            timelineClip.easeInDuration = easeIn;
            timelineClip.easeOutDuration = easeOut;

            AnimationPlayableAsset asset = (AnimationPlayableAsset)timelineClip.asset;
            asset.loop = AnimationPlayableAsset.LoopMode.Off;
            asset.removeStartOffset = false;
        }

        private static void AddMotion(AnimationTrack track, AnimationClip clip, string name, Span span, float sourceStart, float sourceEnd)
        {
            if (clip == null)
            {
                Debug.LogWarning($"[P2Transition] 클립 없음: {name}");
                return;
            }

            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.displayName = name;
            timelineClip.start = span.Start;
            timelineClip.duration = span.End - span.Start;
            timelineClip.clipIn = sourceStart;
            timelineClip.timeScale = (sourceEnd - sourceStart) / (span.End - span.Start);
            timelineClip.blendInDuration = span.BlendIn;

            AnimationPlayableAsset asset = (AnimationPlayableAsset)timelineClip.asset;
            asset.loop = AnimationPlayableAsset.LoopMode.Off;

            // 같은 원본을 여러 조각으로 나눠 쓰므로 조각마다 시작 위치를 지우면 조각 경계에서 위치가 튄다. 원본 루트 값을 그대로 이어 쓴다
            asset.removeStartOffset = false;
            SetPreviousBlendOut(track, timelineClip, span.BlendIn);
        }

        private static void SetPreviousBlendOut(TrackAsset track, TimelineClip current, float blend)
        {
            if (blend <= 0f)
            {
                return;
            }

            foreach (TimelineClip other in track.GetClips())
            {
                if ((other != current) && (other.end > current.start) && (other.start < current.start))
                {
                    other.blendOutDuration = blend;
                }
            }
        }

        private static void BuildPlayerTrack(TimelineAsset timeline, PlayableDirector director, Animator player, string name, float length)
        {
            AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IDLE_PLAYER_CLIP);
            if ((player == null) || (idle == null))
            {
                return;
            }

            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, name);
            track.trackOffset = TrackOffset.ApplyTransformOffsets;
            track.position = Vector3.zero;
            track.rotation = Quaternion.identity;
            director.SetGenericBinding(track, player);
            TimelineClip clip = track.CreateClip(idle);
            clip.start = 0f;
            clip.duration = length;
            ((AnimationPlayableAsset)clip.asset).loop = AnimationPlayableAsset.LoopMode.On;
        }

        private static void BuildCameraTrack(TimelineAsset timeline, PlayableDirector director, CinemachineBrain brain, Dictionary<string, CinemachineCamera> shots, TimeMap map)
        {
            CinemachineTrack track = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
            director.SetGenericBinding(track, brain);

            AddShot(track, director, shots[G_LowWide.Name], map.Map(new Span(0f, 1.3f, 0f)));
            AddShot(track, director, shots[G_Hand.Name], map.Map(new Span(1.3f, 2.3f, 0f)));
            AddShot(track, director, shots[G_Face.Name], map.Map(new Span(1.6f, 2.9f, 0.7f)));
            AddShot(track, director, shots[G_Over.Name], map.Map(new Span(2.9f, 3.25f, 0f)));
            AddShot(track, director, shots[G_Mid.Name], map.Map(new Span(3.25f, 4.6f, 0f)));
            AddShot(track, director, shots[G_Wide.Name], map.Map(new Span(3.5f, TIMELINE_LENGTH, 1.1f)));
        }

        // v03: 컷 순서와 카메라는 같고 시각만 새 흐름에 맞춘다
        // 1 그로기(낮은 와이드) 2 일어서며 떠오르기 시작(손 옆) 3 떠오른 얼굴과 손 올림(얼굴, 떠오른 높이로 올림)
        // 4 오브가 생기는 모습(플레이어 너머) 5 손을 모아 내려꽂힘과 폭발(중간) 6 전장 풀샷
        private static void BuildCameraTrackV03(TimelineAsset timeline, PlayableDirector director, CinemachineBrain brain, Dictionary<string, CinemachineCamera> shots)
        {
            CinemachineTrack track = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
            director.SetGenericBinding(track, brain);

            float impact = ImpactTime();
            AddShot(track, director, shots[G_LowWide.Name], new Span(0f, V03_CUT_HAND, 0f));
            AddShot(track, director, shots[G_Hand.Name], new Span(V03_CUT_HAND, V03_CUT_HAND_END, 0f));
            AddShot(track, director, shots[G_Face.Name], new Span(V03_CUT_FACE, V03_CUT_OVER, 0.7f));
            AddShot(track, director, shots[G_Over.Name], new Span(V03_CUT_OVER, V03_CUT_MID, 0f));
            AddShot(track, director, shots[G_Mid.Name], new Span(V03_CUT_MID, impact + 0.6f, 0f));
            AddShot(track, director, shots[G_Wide.Name], new Span(impact + 0.25f, V03_LENGTH, 1.1f));
        }

        // Rune_Fall은 시작부터 재생해 지면 원(0.1~1.4초), 위로 빨려 오르는 기운과 오브(0.8~1.9초)까지 보이고, 2.0초 폭발이 착지와 겹치게 둔다
        // isCombat이 참이면 보호막 투명도에 정식 어셈블리 트랙(CutsceneParticleFadeTrack)을 쓴다. 시각과 곡선은 같다
        private static Transform BuildVfxTracksV03(TimelineAsset timeline, PlayableDirector director, GameObject vfxRoot, GameObject shieldRoot, bool isCombat)
        {
            float impact = ImpactTime();

            // 보호막만 따로 담는 부모. 같은 ShieldRoot 아래 있는 파괴 효과(0.55초~)는 페이드 대상에서 빠진다. 위치와 크기는 ShieldRoot 그대로다
            GameObject shieldOnly = new GameObject("ShieldFadeRoot (보호막 인스턴스만, 투명도 트랙 바인딩)");
            shieldOnly.transform.SetParent(shieldRoot.transform, false);
            Span shieldSpan = new Span(0f, V03_SHIELD_END, 0f);
            AddVfx(timeline, director, shieldOnly, SHIELD_VFX, "VFX 보호막 (SB_Shield_Blue, 기존 기믹 보호막)", shieldSpan, 2.0f);
            if (isCombat)
            {
                AddCombatShieldFade(timeline, director, shieldOnly.transform, shieldSpan);
            }
            else
            {
                AddShieldFade(timeline, director, shieldOnly.transform, shieldSpan);
            }

            AddVfx(timeline, director, shieldRoot, SHIELD_BREAK, "VFX 보호막 흩어짐 (SB_Shockwave_Pulses_Blue)", new Span(0.55f, 2.2f, 0f), 0f);
            AddVfx(timeline, director, vfxRoot, RUNE_FALL_BLUE_VFX, "VFX 오브 낙하와 폭발 (SB_Rune_Fall_Blue)", new Span(impact - RUNE_FALL_IMPACT, V03_LENGTH, 0f), 0f);
            AddVfx(timeline, director, vfxRoot, VORTEX_BLUE_VFX, "VFX 전장 기운 (SB_Vortex_Blue)", new Span(impact, V03_LENGTH, 0f), 0.6f);
            return shieldOnly.transform;
        }

        private static void AddShot(CinemachineTrack track, PlayableDirector director, CinemachineCamera camera, Span span)
        {
            TimelineClip clip = track.CreateClip<CinemachineShot>();
            clip.displayName = camera.name;
            clip.start = span.Start;
            clip.duration = span.End - span.Start;
            clip.blendInDuration = span.BlendIn;

            CinemachineShot shot = (CinemachineShot)clip.asset;
            shot.DisplayName = camera.name;
            string key = GUID.Generate().ToString();
            shot.VirtualCamera.exposedName = key;
            director.SetReferenceValue(key, camera);
            SetPreviousBlendOut(track, clip, span.BlendIn);
        }

        // VFX는 실제 시간으로 재생되므로 길이를 늘리지 않고, 맞춰야 하는 순간(폭발, 시작)만 늘어난 시간으로 옮긴다
        private static void BuildVfxTracks(TimelineAsset timeline, PlayableDirector director, GameObject vfxRoot, GameObject shieldRoot, bool blue, TimeMap map)
        {
            string runeFall = blue ? RUNE_FALL_BLUE_VFX : RUNE_FALL_VFX;
            string vortex   = blue ? VORTEX_BLUE_VFX : VORTEX_VFX;
            float length = map.Length;
            float runeStart = map.Map(2.6f + RUNE_FALL_BURST_DELAY) - RUNE_FALL_BURST_DELAY;
            AddVfx(timeline, director, shieldRoot, SHIELD_VFX, "VFX 보호막 (SB_Shield_Blue, 기존 기믹 보호막)", new Span(0f, 0.75f, 0f), 2.0f);
            AddVfx(timeline, director, shieldRoot, SHIELD_BREAK, "VFX 보호막 흩어짐 (SB_Shockwave_Pulses_Blue)", new Span(0.55f, map.Map(2.2f), 0f), 0f);
            AddVfx(timeline, director, vfxRoot, runeFall, $"VFX 부적 정렬 대체 ({Path.GetFileNameWithoutExtension(runeFall)})", new Span(runeStart, length, 0f), 1.35f);
            AddVfx(timeline, director, vfxRoot, vortex, $"VFX 전장 기운 ({Path.GetFileNameWithoutExtension(vortex)})", new Span(map.Map(3.3f), length, 0f), 0.6f);
        }

        // 보호막 클립과 같은 구간에 투명도 클립을 둔다. 보호막 VFX 트랙 바로 뒤에 만들어 그 트랙의 파티클 시뮬레이션 다음에 덮어쓴다
        private static void AddShieldFade(TimelineAsset timeline, PlayableDirector director, Transform shieldOnly, Span span)
        {
            PreviewParticleFadeTrack track = timeline.CreateTrack<PreviewParticleFadeTrack>(null, "VFX 보호막 투명도 (경과 시간 기준)");
            director.SetGenericBinding(track, shieldOnly);
            TimelineClip clip = track.CreateClip<PreviewParticleFadeClip>();
            clip.displayName = "보호막 서서히 흐려짐";
            clip.start = span.Start;
            clip.duration = span.End - span.Start;

            PreviewParticleFadeClip fade = (PreviewParticleFadeClip)clip.asset;
            fade.FadeStart = V03_SHIELD_FADE_START;
            fade.FadeEnd = V03_SHIELD_FADE_END;
        }

        private static void AddCombatShieldFade(TimelineAsset timeline, PlayableDirector director, Transform shieldOnly, Span span)
        {
            CutsceneParticleFadeTrack track = timeline.CreateTrack<CutsceneParticleFadeTrack>(null, "VFX 보호막 투명도 (경과 시간 기준)");
            director.SetGenericBinding(track, shieldOnly);
            TimelineClip clip = track.CreateClip<CutsceneParticleFadeClip>();
            clip.displayName = "보호막 서서히 흐려짐";
            clip.start = span.Start;
            clip.duration = span.End - span.Start;

            CutsceneParticleFadeClip fade = (CutsceneParticleFadeClip)clip.asset;
            fade.FadeStart = V03_SHIELD_FADE_START;
            fade.FadeEnd = V03_SHIELD_FADE_END;
        }

        private static void AddVfx(TimelineAsset timeline, PlayableDirector director, GameObject parent, string prefabPath, string name, Span span, float clipIn)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[P2Transition] VFX 없음: {prefabPath}");
                return;
            }

            ControlTrack track = timeline.CreateTrack<ControlTrack>(null, name);
            TimelineClip clip = track.CreateClip<ControlPlayableAsset>();
            clip.displayName = prefab.name;
            clip.start = span.Start;
            clip.duration = span.End - span.Start;
            clip.clipIn = clipIn;

            ControlPlayableAsset control = (ControlPlayableAsset)clip.asset;
            control.prefabGameObject = prefab;
            control.updateParticle = true;
            control.updateDirector = false;
            control.updateITimeControl = false;
            control.searchHierarchy = false;
            control.active = true;
            control.postPlayback = ActivationControlPlayable.PostPlaybackState.Revert;
            string key = GUID.Generate().ToString();
            control.sourceGameObject.exposedName = key;
            director.SetReferenceValue(key, parent);
        }
    }
}
