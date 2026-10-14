// System
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 각 상태를 크게 보여 주는 실제 PlayMode 모션 렌더, 실시간 FPS 측정이 아니다 </summary>
    public static class WolbaekFoxfireMotionCapture
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FRAME_RATE = 24;
        private const int FRAME_COUNT = 168;
        private const float FLIGHT_SECONDS = 2.2f;
        private const float LOCKED_END = 3.4f;
        private const float IMPACT_END = 4.7f;
        private const float GOLD_END = 5.85f;
        private const float FLIGHT_STOP_TIME = 1.9f;
        private const float STRAIGHT_SECONDS = 0.8f;
        private const float FLIGHT_SPEED = 2.2f;
        private const float CURVE_RATE = 2.1f;
        private const double PREVIEW_IMPACT_TIME = 100d;

        /****************************************
        *                Methods
        ****************************************/

        public static string CaptureTour()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("실제 PlayMode에서 캡처합니다.");
            }
            WolbaekFoxfirePreviewBoard board = UnityEngine.Object.FindAnyObjectByType<WolbaekFoxfirePreviewBoard>();
            Camera camera = Camera.main;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            board.Stop();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekFoxfireBuilder.PREFAB);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "Temporary_FoxfireMotionCapture";
            WolbaekFoxfirePlayer player = instance.GetComponent<WolbaekFoxfirePlayer>();
            SkinnedMeshRenderer[] skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer skin in skins)
            {
                // 여러 Camera.Render를 같은 Editor 프레임에서 호출해도 bone 행렬을 새로 평가한다.
                skin.forceMatrixRecalculationPerRender = true;
            }
            TMPro.TextMeshPro[] labels = UnityEngine.Object.FindObjectsByType<TMPro.TextMeshPro>();
            foreach (TMPro.TextMeshPro label in labels)
            {
                if (label.name.StartsWith("Label_"))
                {
                    label.enabled = false;
                }
            }
            MethodInfo capture = CaptureMethod();
            string folder = Path.GetFullPath(Path.Combine(WolbaekFoxfireBuilder.EVIDENCE, "unity-motion-final-v05"));
            Directory.CreateDirectory(folder);
            List<string> states = new List<string>(FRAME_COUNT);
            uint id = 0;
            int active = -1;
            try
            {
                for (int frame = 0; frame < FRAME_COUNT; ++frame)
                {
                    float time = (float)frame / FRAME_RATE;
                    int section = time < FLIGHT_SECONDS ? 0 : time < LOCKED_END ? 1
                        : time < IMPACT_END ? 2 : time < GOLD_END ? 3 : 4;
                    float local = time - (section == 0 ? 0f : section == 1 ? FLIGHT_SECONDS
                        : section == 2 ? LOCKED_END : section == 3 ? IMPACT_END : GOLD_END);
                    if (section != active)
                    {
                        player.Stop();
                        if (section == 0)
                        {
                            id = player.PlayFlight(FlightPosition(0f), FlightRotation(0f), 0d, 0d);
                            camera.transform.position = new Vector3(0f, 4.5f, -7f);
                            camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                            camera.fieldOfView = 55f;
                        }
                        else
                        {
                            EWolbaekFoxfireVariant variant = section == 3 ? EWolbaekFoxfireVariant.GoldTarget
                                : section == 4 ? EWolbaekFoxfireVariant.RedHazard : EWolbaekFoxfireVariant.Blue;
                            id = player.BeginTelegraph(Vector3.zero, variant, 0d, PREVIEW_IMPACT_TIME, 0d);
                            if (section == 2)
                            {
                                player.ConfirmImpact(id, 0d, 0d);
                            }
                            camera.transform.position = new Vector3(0f, 4.5f, -7f);
                            camera.transform.LookAt(new Vector3(0f, 1.35f, 0f));
                            camera.fieldOfView = 42f;
                        }
                        active = section;
                    }
                    if (section == 0)
                    {
                        float routeTime = Mathf.Min(local, FLIGHT_STOP_TIME);
                        player.SetFlightPose(id, FlightPosition(routeTime), FlightRotation(routeTime));
                    }
                    player.Tick(id, local);
                    states.Add(frame + " | " + time + " | " + section + " | " + player.State);
                    capture.Invoke(null, new object[] { camera, "frame_" + frame.ToString("D4") + ".png",
                        1, false, false, 0, folder });
                }
                File.WriteAllLines(Path.Combine(folder, "frame-states.txt"), states);
                return folder + "; frames=" + FRAME_COUNT + "; fps=" + FRAME_RATE;
            }
            finally
            {
                player.Stop();
                UnityEngine.Object.Destroy(instance);
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                foreach (TMPro.TextMeshPro label in labels)
                {
                    if (label.name.StartsWith("Label_"))
                    {
                        label.enabled = true;
                    }
                }
                board.Begin(false);
            }
        }

        private static Vector3 FlightPosition(float time)
        {
            if (time <= STRAIGHT_SECONDS)
            {
                return new Vector3(-1.6f + time * FLIGHT_SPEED, 0.9f, 0f);
            }
            float phase = (time - STRAIGHT_SECONDS) * CURVE_RATE;
            float radius = FLIGHT_SPEED / CURVE_RATE;
            return new Vector3(-1.6f + STRAIGHT_SECONDS * FLIGHT_SPEED + radius * Mathf.Sin(phase),
                0.9f + Mathf.Sin(phase * 0.5f) * 0.1f, radius * (1f - Mathf.Cos(phase)));
        }

        private static Quaternion FlightRotation(float time)
        {
            if (time <= STRAIGHT_SECONDS)
            {
                return Quaternion.Euler(0f, 90f, 0f);
            }
            float phase = (time - STRAIGHT_SECONDS) * CURVE_RATE;
            return Quaternion.LookRotation(new Vector3(Mathf.Cos(phase), Mathf.Cos(phase * 0.5f) * 0.0477f,
                Mathf.Sin(phase)), Vector3.up);
        }

        private static MethodInfo CaptureMethod()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type helper = assembly.GetType("MCPForUnity.Runtime.Helpers.ScreenshotUtility");
                if (helper != null)
                {
                    return helper.GetMethod("CaptureFromCameraToProjectFolder");
                }
            }
            throw new InvalidOperationException("설치된 Unity MCP 카메라 캡처 도구를 찾지 못했습니다.");
        }
    }
}
