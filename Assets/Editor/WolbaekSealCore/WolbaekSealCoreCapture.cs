// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 전용 씬의 실제 PlayMode 카메라를 표본 시각에서 캡처한다 </summary>
    public static class WolbaekSealCoreCapture
    {
        /****************************************
        *                Fields
        ****************************************/
        private const int WIDTH               = 1280;
        private const int HEIGHT              = 720;
        private const int HIDDEN_LAYER        = 30;

        private const int ALL_PANELS          = -1;
        private const int DROP_PANEL          = 2;
        private const int CORE_COUNT          = 2;
        private const int SUPERSAMPLE_SCALE   = 2;
        private const int RENDER_DEPTH_BITS   = 24;
        private const float DROP_FOCUS_HEIGHT = 4.2f;
        private const float CORE_FOCUS_HEIGHT = 1.5f;
        private const float OVERVIEW_CAMERA_X = 8f;
        private const float OVERVIEW_CAMERA_Y = 10f;
        private const float OVERVIEW_CAMERA_Z = -16f;
        private const float DROP_CAMERA_X     = 8f;
        private const float DROP_CAMERA_Y     = 4f;
        private const float DROP_CAMERA_Z     = -14f;
        private const float CORE_CAMERA_X     = 3.6f;
        private const float CORE_CAMERA_Y     = 1.6f;
        private const float CORE_CAMERA_Z     = -5.8f;
        private const float OVERVIEW_FOV      = 38f;
        private const float DETAIL_FOV        = 35f;

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> panel -1은 전경, 0과 1은 핵, 2는 낙하만 표시한다 </summary>
        public static void Capture(string filename, int panel, float seconds, bool restart, float orbitDegrees = 0f)
        {
            if ((!EditorApplication.isPlaying) || (panel < ALL_PANELS) || (panel > DROP_PANEL))
            {
                throw new InvalidOperationException("봉인핵 PlayMode와 유효한 패널이 필요합니다");
            }
            WolbaekSealCorePreview preview = UnityEngine.Object.FindAnyObjectByType<WolbaekSealCorePreview>();
            Camera camera = Camera.main;
            if ((preview == null) || (camera == null))
            {
                throw new InvalidOperationException("봉인핵 검수 씬이 필요합니다");
            }
            if (restart)
            {
                preview.Begin(true);
            }
            preview.Sample(seconds);
            Vector3 point = panel < 0 ? Vector3.up : panel == DROP_PANEL
                ? preview.Drop.transform.position + Vector3.up * DROP_FOCUS_HEIGHT
                : preview.Cores[panel].transform.position + Vector3.up * CORE_FOCUS_HEIGHT;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            int mask = camera.cullingMask;
            float fov = camera.fieldOfView;
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            List<Renderer> hidden = new List<Renderer>();
            List<int> layers = new List<int>();
            RenderTexture target = RenderTexture.GetTemporary(WIDTH * SUPERSAMPLE_SCALE, HEIGHT * SUPERSAMPLE_SCALE, RENDER_DEPTH_BITS, RenderTextureFormat.ARGBHalf);
            RenderTexture readback = RenderTexture.GetTemporary(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            try
            {
                camera.cullingMask &= ~(1 << HIDDEN_LAYER);
                for (int i = 0; i < CORE_COUNT; ++i)
                {
                    if ((panel >= 0) && (panel != i))
                    {
                        Hide(preview.Cores[i].gameObject, hidden, layers);
                    }
                }
                if ((panel >= 0) && (panel != DROP_PANEL))
                {
                    Hide(preview.Drop.gameObject, hidden, layers);
                }
                camera.transform.position = point + Quaternion.Euler(0f, orbitDegrees, 0f) * (panel < 0 ? new Vector3(OVERVIEW_CAMERA_X, OVERVIEW_CAMERA_Y, OVERVIEW_CAMERA_Z)
                    : panel == DROP_PANEL ? new Vector3(DROP_CAMERA_X, DROP_CAMERA_Y, DROP_CAMERA_Z) : new Vector3(CORE_CAMERA_X, CORE_CAMERA_Y, CORE_CAMERA_Z));
                camera.transform.LookAt(point);
                camera.fieldOfView = panel < 0 ? OVERVIEW_FOV : DETAIL_FOV;
                camera.targetTexture = target;
                camera.Render();
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(target, readback);
                RenderTexture.active = readback;
                texture.ReadPixels(new Rect(0f, 0f, WIDTH, HEIGHT), 0, 0);
                texture.Apply();
                File.WriteAllBytes(filename, texture.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < hidden.Count; ++i)
                {
                    hidden[i].gameObject.layer = layers[i];
                }
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.cullingMask = mask;
                camera.fieldOfView = fov;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgb;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(readback);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void Hide(GameObject target, List<Renderer> hidden, List<int> layers)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                hidden.Add(renderer);
                layers.Add(renderer.gameObject.layer);
                renderer.gameObject.layer = HIDDEN_LAYER;
            }
        }
    }
}
