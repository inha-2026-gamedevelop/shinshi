// System
using System;
using System.IO;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 실제 PlayMode API 검증과 Unity 화면 프레임 캡처를 수행한다 </summary>
    public static class WolbaekFoxfireVerification
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CAPTURE_FRAMES = 50;
        private const float CAPTURE_FPS = 10f;
        private const double CAPTURE_INTERVAL = 0.13d;
        private static readonly List<string> G_Results = new List<string>();
        private static int G_Frame;
        private static double G_NextCapture;
        private static string G_Folder;
        private static bool G_Sequence;
        private static Camera G_Camera;
        private static Vector3 G_CameraPosition;
        private static Quaternion G_CameraRotation;
        private static float G_CameraFov;
        private static WolbaekFoxfirePreviewBoard G_Board;
        private static WolbaekFoxfirePlayer G_Player;
        private static readonly List<string> G_FrameStates = new List<string>();
        private static System.Reflection.MethodInfo G_CaptureMethod;

        /****************************************
        *                Methods
        ****************************************/

        public static string Verify()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("실제 PlayMode에서만 검증합니다.");
            }

            G_Results.Clear();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekFoxfireBuilder.PREFAB);
            GameObject instance = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, -100f, 0f), Quaternion.identity);
            WolbaekFoxfirePlayer player = instance.GetComponent<WolbaekFoxfirePlayer>();
            WolbaekFoxfireSettingsSO cloned = UnityEngine.Object.Instantiate(player.Settings);
            SerializedObject configuration = new SerializedObject(player);
            configuration.FindProperty("_settings").objectReferenceValue = cloned;
            configuration.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                uint id = player.BeginTelegraph(Vector3.zero, EWolbaekFoxfireVariant.Blue, 10d, 12d, 10d);
                player.Tick(id, 12.5d);
                Check((player.IsRunning) && (!player.IsConfirmed) && (player.State == EWolbaekFoxfireState.Locked),
                    "외부 확정 누락: 예정 시간 뒤에도 자동 폭발하지 않음");
                player.Tick(id, 13.1d);
                Check((!player.IsRunning) && (player.State == EWolbaekFoxfireState.Idle),
                    "확정 누락 grace 경과 후 정리");
                uint previous = id;
                id = player.BeginTelegraph(Vector3.zero, EWolbaekFoxfireVariant.Blue, 20d, 22d, 20d);
                Check(!player.ConfirmImpact(id, 21d, 20d), "미래 확정 거절");
                Check(!player.ConfirmImpact(previous, 21d, 21d), "재사용 이전 세대 확정 거절");
                Check(!player.Stop(previous), "이전 세대 취소가 새 재생을 지우지 않음");
                player.SamplePreview(0f);
                Check((player.PlaybackId == id) && (player.State == EWolbaekFoxfireState.Locked),
                    "Inspector 샘플링이 외부 재생을 덮어쓰지 않음");
                Check(player.ConfirmImpact(id, 22d, 22d), "정상 외부 확정으로 타격 시작");
                Check(!player.ConfirmImpact(id, 22d, 22d), "중복 확정 거절");
                player.Tick(id, 25d);
                Check(!player.IsRunning, "타격 수명 종료 및 정리");
                id = player.BeginTelegraph(Vector3.zero, EWolbaekFoxfireVariant.Blue, 0d, 10d, 0d);
                Check(player.SetGroundRadius(id, 2f), "외부 반경 입력 수용");
                player.Tick(id, 0.5d);
                bool radius = false;
                bool core = false;
                foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.name.StartsWith("Ground_"))
                    {
                        radius = Mathf.Abs(renderer.transform.localScale.x - 2f / 0.7865f) < 0.001f;
                    }

                    if (renderer.name.StartsWith("Core_"))
                    {
                        core = renderer.transform.localScale == Vector3.one;
                    }
                }

                Check((radius) && (core), "지면만 2m 반경으로 변경, 코어 높이/크기 유지");
                Check(!player.SetFlightPose(id, Vector3.one, Quaternion.identity), "잠금 상태 이동 요청 거절");
                player.Stop(id);
                Check(player.GetComponentsInChildren<Renderer>().Length == 0, "취소 후 활성 표현 없음");
                id = player.PlayFlight(Vector3.zero, Quaternion.identity, 0d, 0d);
                player.gameObject.SetActive(false);
                Check(!player.IsRunning, "비활성화 시 정리");
                player.gameObject.SetActive(true);
                Check(player.PlayFlight(new Vector3(float.NaN, 0f, 0f), Quaternion.identity, 0d, 0d) == 0,
                    "비유한 위치 거절");
                SerializedObject timing = new SerializedObject(cloned);
                timing.FindProperty("_fadeSeconds").floatValue = 2f;
                timing.ApplyModifiedPropertiesWithoutUndo();
                id = player.BeginTelegraph(Vector3.zero, EWolbaekFoxfireVariant.Blue, 30d, 32d, 30d);
                player.ConfirmImpact(id, 32d, 32d);
                timing.FindProperty("_fadeSeconds").floatValue = 0.01f;
                timing.ApplyModifiedPropertiesWithoutUndo();
                player.Tick(id, 32.8d);
                Check(player.IsRunning, "재생 중 시간 변경은 시작 스냅샷을 보존");
                id = player.BeginTelegraph(Vector3.zero, EWolbaekFoxfireVariant.Blue, 40d, 42d, 40d);
                player.ConfirmImpact(id, 42d, 42d);
                player.Tick(id, 42.8d);
                Check(!player.IsRunning, "다음 재생부터 변경한 fade 시간 적용");
                foreach (EWolbaekFoxfireVariant variant in new[] { EWolbaekFoxfireVariant.GoldTarget, EWolbaekFoxfireVariant.RedHazard })
                {
                    id = player.BeginTelegraph(Vector3.zero, variant, 0d, 1d, 0d);
                    player.ConfirmImpact(id, 1d, 1.2d);
                    Check((player.State == EWolbaekFoxfireState.Impact) && (player.GetComponentsInChildren<Renderer>().Length > 0),
                        variant + " 전용 색상 타격 레이어 참조");
                }

                string result = string.Join("\n", G_Results);
                File.WriteAllText(Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, "unity_playmode_verification.txt"), result);
                Debug.Log("[WolbaekFoxfire] 실제 PlayMode 검증 " + G_Results.Count + "건 PASS");
                return result;
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(cloned);
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
            {
                throw new InvalidOperationException("검증 실패: " + label);
            }

            G_Results.Add("PASS | " + label);
        }

        public static string VerifyMotion()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("실제 PlayMode에서만 검증합니다.");
            }
            G_Results.Clear();
            GameObject instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekFoxfireBuilder.PREFAB));
            WolbaekFoxfirePlayer player = instance.GetComponent<WolbaekFoxfirePlayer>();
            SkinnedMeshRenderer tail = null;
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name.Contains("Flight_Flame_Blue_Airy"))
                {
                    tail = renderer;
                }
            }
            Transform station = tail.bones[6];
            Vector3 rest = station.localPosition;
            System.Reflection.FieldInfo historyField = typeof(WolbaekFoxfirePlayer).GetField("_flightPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            try
            {
                uint id = player.PlayFlight(Vector3.zero, Quaternion.identity, 0d, 0d);
                Vector3 previous = station.localPosition;
                float largestJump = 0f;
                for (int frame = 1; frame <= 120; ++frame)
                {
                    float time = frame / 60f;
                    if (frame % 3 == 0)
                    {
                        float phase = time * 2f;
                        player.SetFlightPose(id, new Vector3(Mathf.Sin(phase) * 1.2f, 0f, (1f - Mathf.Cos(phase)) * 1.2f),
                            Quaternion.LookRotation(new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase))));
                    }
                    player.Tick(id, time);
                    if (frame > 30)
                    {
                        largestJump = Mathf.Max(largestJump, (station.localPosition - previous).magnitude);
                    }
                    previous = station.localPosition;
                }
                Check(largestJump < 0.35f, "20Hz pose / 60Hz Tick: 정지 프레임마다 rest로 튀지 않음, max=" + largestJump);
                Check((station.localPosition - rest).magnitude > 0.2f, "곡선 경로의 지연 위치가 원본 rest와 다름");
                player.Tick(id, 2d);
                object history = historyField.GetValue(player);
                System.Reflection.PropertyInfo count = history.GetType().GetProperty("Count",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                int before = (int)count.GetValue(history);
                player.Tick(id, 2d);
                Check(before == (int)count.GetValue(history), "같은 시각 Tick은 경로 샘플 중복을 만들지 않음");
                previous = station.localPosition;
                player.Tick(id, 2.0167d);
                Check((station.localPosition - previous).magnitude < 0.2f, "정지 직후 한 프레임에 rest로 복귀하지 않음");
                player.SetFlightPose(id, new Vector3(20f, 0f, 0f), Quaternion.identity);
                player.Tick(id, 2.1d);
                Check((int)count.GetValue(history) == 1 && (station.position - player.transform.position).magnitude < 4f,
                    "순간이동은 과거 궤적을 폐기하고 새 위치에서 시작");
                player.Tick(id, 5d);
                Check((int)count.GetValue(history) == 1, "긴 Tick 간격도 과거 궤적을 폐기");
                player.Stop();
                Check((station.localPosition - rest).sqrMagnitude < 0.000001f, "취소는 본의 원본 자세를 복원");
                id = player.PlayFlight(new Vector3(-20f, 0f, 0f), Quaternion.identity, 10d, 10d);
                Check((int)count.GetValue(history) == 1, "풀 재사용은 이전 세대 경로를 이어 쓰지 않음");
                string output = string.Join("\n", G_Results);
                File.WriteAllText(Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, "unity_motion_verification_v04.txt"), output);
                return output;
            }
            finally
            {
                player.Stop();
                UnityEngine.Object.Destroy(instance);
            }
        }

        public static string VerifyIndividualPrefabs()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("실제 PlayMode에서만 검증합니다.");
            }
            G_Results.Clear();
            HashSet<int> profiles = new HashSet<int>();
            string[] rootNames = { "Flight", "Locked", "Impact", "Gold", "Red" };
            for (int preset = 0; preset < rootNames.Length; ++preset)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekFoxfireBuilder.IndividualPrefab(preset));
                GameObject instance = UnityEngine.Object.Instantiate(prefab, Vector3.down * 100f, Quaternion.identity);
                WolbaekFoxfirePlayer player = instance.GetComponent<WolbaekFoxfirePlayer>();
                try
                {
                    Check((!player.IsRunning) && (instance.GetComponentsInChildren<Renderer>().Length == 0), rootNames[preset] + ": 자동 재생 없음");
                    Check(instance.transform.childCount == (preset == 2 ? 3 : 1), rootNames[preset] + ": 필요한 상태만 포함");
                    Check(profiles.Add(player.Settings.GetInstanceID()), rootNames[preset] + ": 독립 SO");
                    player.SamplePreview(0.25f);
                    EWolbaekFoxfireState expected = preset == 0 ? EWolbaekFoxfireState.Flight
                        : preset == 2 ? EWolbaekFoxfireState.Impact : EWolbaekFoxfireState.Locked;
                    Check((player.IsRunning) && (player.State == expected) && ActiveRoot(instance) == rootNames[preset],
                        rootNames[preset] + ": 기본 preview는 해당 상태만 표시");
                    player.SamplePreview(player.PreviewDuration + 0.1f);
                    Check((!player.IsRunning) && (instance.GetComponentsInChildren<Renderer>().Length == 0), rootNames[preset] + ": preview 종료 정리");
                    EWolbaekFoxfireVariant variant = preset == 3 ? EWolbaekFoxfireVariant.GoldTarget
                        : preset == 4 ? EWolbaekFoxfireVariant.RedHazard : EWolbaekFoxfireVariant.Blue;
                    uint id = preset == 0 ? player.PlayFlight(Vector3.zero, Quaternion.identity, 10d, 10d)
                        : preset == 2 ? player.PlayImpact(Vector3.zero, variant, 10d, 10.3d)
                        : player.BeginTelegraph(Vector3.zero, variant, 10d, 12d, 10d);
                    Check((id != 0) && (player.IsRunning) && (player.State == expected), rootNames[preset] + ": 외부 API 재생");
                    player.SamplePreview(0.1f);
                    Check((player.PlaybackId == id) && (player.State == expected), rootNames[preset] + ": preview가 외부 재생을 덮지 않음");
                    if (preset == 0)
                    {
                        Check((player.BeginTelegraph(Vector3.zero, variant, 10d, 12d, 10d) == 0)
                            && (!player.LockAt(id, Vector3.zero, variant, 10d, 12d, 10d))
                            && (player.PlayImpact(Vector3.zero, variant, 10d, 10d) == 0), "Flight: 미지원 예고/타격 거절");
                    }
                    else if (preset == 2)
                    {
                        Check((player.PlayFlight(Vector3.zero, Quaternion.identity, 10d, 10d) == 0)
                            && (player.BeginTelegraph(Vector3.zero, variant, 10d, 12d, 10d) == 0)
                            && (player.PlayImpact(Vector3.zero, variant, 11d, 10d) == 0), "Impact: 미지원 상태/미래 확정 거절");
                    }
                    else
                    {
                        player.Tick(id, 12.5d);
                        Check((!player.IsConfirmed) && (player.State == EWolbaekFoxfireState.Locked)
                            && (!player.ConfirmImpact(id, 12d, 12.5d)), rootNames[preset] + ": 자동 타격 없음, 별도 Impact 필요");
                        Check((player.PlayFlight(Vector3.zero, Quaternion.identity, 10d, 10d) == 0)
                            && (player.PlayImpact(Vector3.zero, variant, 12d, 12.5d) == 0), rootNames[preset] + ": 미지원 상태 거절");
                    }
                    Check((player.PlaybackId == id) && (player.IsRunning), rootNames[preset] + ": 거절은 기존 재생 보존");
                    Check((player.Stop(id)) && (!player.IsRunning) && (instance.GetComponentsInChildren<Renderer>().Length == 0),
                        rootNames[preset] + ": 취소/풀 정리");
                    if (preset == 2)
                    {
                        foreach (EWolbaekFoxfireVariant color in new[] { EWolbaekFoxfireVariant.Blue,
                            EWolbaekFoxfireVariant.GoldTarget, EWolbaekFoxfireVariant.RedHazard })
                        {
                            uint next = player.PlayImpact(Vector3.zero, color, 20d, 20.2d);
                            string root = color == EWolbaekFoxfireVariant.Blue ? "Impact"
                                : color == EWolbaekFoxfireVariant.GoldTarget ? "GoldImpact" : "RedImpact";
                            Check((next != 0) && (player.IsConfirmed) && (ActiveRoot(instance) == root), "Impact: " + color + " 실제 확정 시각으로 시작");
                            Check(!player.Stop(id), "Impact: 이전 ID 취소 거절 " + color);
                            player.Tick(next, 25d);
                            Check(!player.IsRunning, "Impact: " + color + " 수명 종료");
                        }
                    }
                }
                finally
                {
                    player.Stop();
                    UnityEngine.Object.Destroy(instance);
                }
            }
            GameObject combined = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekFoxfireBuilder.PREFAB), Vector3.down * 100f, Quaternion.identity);
            try
            {
                WolbaekFoxfirePlayer player = combined.GetComponent<WolbaekFoxfirePlayer>();
                Check(player.PreviewMode == EWolbaekFoxfirePreviewMode.Sequence, "통합 PF: 기존 직렬화 기본값 유지");
                player.SamplePreview(0.2f);
                Check(player.State == EWolbaekFoxfireState.Flight, "통합 PF: 기존 Flight preview");
                player.SamplePreview(player.Settings.PreviewFlightSeconds + 0.2f);
                Check(player.State == EWolbaekFoxfireState.Locked, "통합 PF: 기존 Locked preview");
                player.SamplePreview(player.Settings.PreviewFlightSeconds + player.Settings.PreviewLockedSeconds + 0.2f);
                Check(player.State == EWolbaekFoxfireState.Impact, "통합 PF: 기존 Impact preview");
                player.Stop();
            }
            finally
            {
                UnityEngine.Object.Destroy(combined);
            }
            string output = string.Join("\n", G_Results);
            File.WriteAllText(Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, "unity_individual_prefab_verification.txt"), output);
            return output;
        }

        private static string ActiveRoot(GameObject instance)
        {
            string active = null;
            foreach (Transform root in instance.transform)
            {
                if (root.gameObject.activeSelf)
                {
                    if (active != null)
                    {
                        return "MultipleRoots";
                    }
                    active = root.name;
                }
            }
            return active;
        }

        public static string BeginCapture(bool sequence)
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("PlayMode 화면만 캡처합니다.");
            }

            G_Board = UnityEngine.Object.FindAnyObjectByType<WolbaekFoxfirePreviewBoard>();
            G_Camera = Camera.main;
            G_CameraPosition = G_Camera.transform.position;
            G_CameraRotation = G_Camera.transform.rotation;
            G_CameraFov = G_Camera.fieldOfView;
            G_Sequence = sequence;
            G_Player = null;
            G_Frame = 0;
            G_FrameStates.Clear();
            G_Folder = Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, sequence ? "unity-sequence-frames" : "unity-board-frames");
            Directory.CreateDirectory(G_Folder);
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type helper = assembly.GetType("MCPForUnity.Runtime.Helpers.ScreenshotUtility");
                if (helper != null)
                {
                    G_CaptureMethod = helper.GetMethod("CaptureFromCameraToProjectFolder");
                    break;
                }
            }

            if (G_CaptureMethod == null)
            {
                throw new InvalidOperationException("설치된 Unity MCP 카메라 캡처 도구를 찾지 못했습니다.");
            }

            foreach (TMPro.TextMeshPro label in UnityEngine.Object.FindObjectsByType<TMPro.TextMeshPro>())
            {
                if (label.name.StartsWith("Label_"))
                {
                    label.enabled = !sequence;
                }
            }
            G_Board.Stop();
            if (sequence)
            {
                G_Player = GameObject.Find("Foxfire_Flight").GetComponent<WolbaekFoxfirePlayer>();
                G_Player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 50f, 0f));
                G_Camera.transform.position = new Vector3(0f, 7f, -11.5f);
                G_Camera.transform.LookAt(new Vector3(0f, 0.9f, -1.2f));
                G_Camera.fieldOfView = 48f;
            }
            else
            {
                G_Board.Begin(true);
            }

            Type gameView = Type.GetType("UnityEditor.GameView,UnityEditor");
            EditorWindow.GetWindow(gameView).Focus();
            G_NextCapture = EditorApplication.timeSinceStartup + CAPTURE_INTERVAL;
            EditorApplication.update -= CaptureStep;
            EditorApplication.update += CaptureStep;
            return G_Folder;
        }

        private static void CaptureStep()
        {
            if ((!Application.isPlaying) || (EditorApplication.timeSinceStartup < G_NextCapture))
            {
                return;
            }

            if (G_Frame >= CAPTURE_FRAMES)
            {
                EditorApplication.update -= CaptureStep;
                G_Camera.transform.SetPositionAndRotation(G_CameraPosition, G_CameraRotation);
                G_Camera.fieldOfView = G_CameraFov;
                G_Player?.Stop();
                foreach (TMPro.TextMeshPro label in UnityEngine.Object.FindObjectsByType<TMPro.TextMeshPro>())
                {
                    if (label.name.StartsWith("Label_"))
                    {
                        label.enabled = true;
                    }
                }
                G_Board.Begin(false);
                File.WriteAllLines(Path.Combine(G_Folder, "frame-states.txt"), G_FrameStates);
                Debug.Log("[WolbaekFoxfire] Unity 실제 화면 캡처 완료: " + G_Folder);
                return;
            }

            float seconds = G_Frame / CAPTURE_FPS;
            if (G_Sequence)
            {
                G_Player.SamplePreview(seconds);
                G_FrameStates.Add(G_Frame + " | " + seconds + " | " + G_Player.State);
            }
            else
            {
                G_Board.Sample(seconds);
                G_FrameStates.Add(G_Frame + " | " + seconds + " | ParallelFiveStates");
            }

            // 백그라운드 Editor에서 ScreenCapture는 다음 화면 갱신까지 여러 요청을 합친다.
            // 실제 도구와 같은 동기 Camera.Render 캡처를 쓰고 선택적 MCP 의존성은 에디터에만 둔다.
            G_CaptureMethod.Invoke(null, new object[] { G_Camera, "frame_" + G_Frame.ToString("D4") + ".png",
                1, false, false, 0, G_Folder });
            ++G_Frame;
            G_NextCapture = EditorApplication.timeSinceStartup + CAPTURE_INTERVAL;
        }

        public static string Dependencies()
        {
            string[] paths = AssetDatabase.GetDependencies(WolbaekFoxfireBuilder.PREFAB, true);
            List<string> lines = new List<string>();
            lines.Add("Prefab=" + WolbaekFoxfireBuilder.PREFAB);
            lines.Add("GUID=" + AssetDatabase.AssetPathToGUID(WolbaekFoxfireBuilder.PREFAB));
            foreach (string path in paths)
            {
                lines.Add(path + " | " + AssetDatabase.AssetPathToGUID(path));
            }

            string result = string.Join("\n", lines);
            File.WriteAllText(Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, "unity_dependencies.txt"), result);
            return result;
        }
    }
}
