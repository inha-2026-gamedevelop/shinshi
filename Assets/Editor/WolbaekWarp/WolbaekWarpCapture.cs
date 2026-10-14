// System
using System;
using System.IO;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    public static class WolbaekWarpCapture
    {
        private const int WIDTH = 1280;
        private const int HEIGHT = 720;
        private const int HIDDEN_LAYER = 30;
        private const int VISIBLE_LAYER = 31;
        private const float BODY_FOV = 27f;
        private const float LANDING_FOV = 41f;
        private const float LANDING_TARGET_DOWN = 0.8f;
        private static readonly Vector3 G_BodyOffset = new Vector3(2.8f, 1.55f, -4.1f);
        private static readonly Vector3 G_LandingOffset = new Vector3(6f, 6.4f, -7.7f);

        public static void Capture(string filename, int panel, float seconds, bool restart = true)
        {
            if (!EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("PlayMode 캡처만 지원");
            }
            WolbaekWarpPreview preview = UnityEngine.Object.FindAnyObjectByType<WolbaekWarpPreview>();
            if (restart)
            {
                preview.Begin(true);
            }
            preview.Sample(seconds);
            Camera camera = GameObject.Find("Warp_GameplayCamera").GetComponent<Camera>();
            WolbaekWarpPlayer[] players = UnityEngine.Object.FindObjectsByType<WolbaekWarpPlayer>();
            List<Renderer> renderers = new List<Renderer>();
            List<int> layers = new List<int>();
            EWolbaekWarpKind targetKind = panel < 2 ? EWolbaekWarpKind.Breakup : panel == 2 || panel == 5 ? EWolbaekWarpKind.Reform : EWolbaekWarpKind.Landing;
            foreach (WolbaekWarpPlayer player in players)
            {
                foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
                {
                    renderers.Add(renderer);
                    layers.Add(renderer.gameObject.layer);
                    renderer.gameObject.layer = player.Kind == targetKind ? VISIBLE_LAYER : HIDDEN_LAYER;
                    SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                    if (skin != null)
                    {
                        skin.forceMatrixRecalculationPerRender = true;
                    }
                }
            }
            string[] names = { "Development_Neutral_ReformedBody", "Development_Neutral_LandedBody" };
            for (int i = 0; i < names.Length; ++i)
            {
                GameObject body = GameObject.Find(names[i]);
                if (body == null)
                {
                    continue;
                }
                foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                {
                    renderers.Add(renderer);
                    layers.Add(renderer.gameObject.layer);
                    renderer.gameObject.layer = (i == 0 && panel == 2) || (i == 1 && panel >= 3) ? VISIBLE_LAYER : HIDDEN_LAYER;
                }
            }
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            int oldMask = camera.cullingMask;
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            RenderTexture target = RenderTexture.GetTemporary(WIDTH * 2, HEIGHT * 2, 24, RenderTextureFormat.ARGBHalf);
            target.filterMode = FilterMode.Bilinear;
            RenderTexture readback = RenderTexture.GetTemporary(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            try
            {
                Vector3 point = panel < 2 ? new Vector3(-5f, 0.95f, 0f) : panel == 2 || panel == 5 ? Vector3.up * (panel == 5 ? 1.8f : 0.95f) : new Vector3(7f, panel == 4 ? 0.85f : 0.3f, 0f);
                camera.transform.position = point + (panel == 3 ? G_LandingOffset : G_BodyOffset);
                camera.transform.LookAt(panel == 3 ? point - Vector3.up * LANDING_TARGET_DOWN : point);
                camera.fieldOfView = panel == 3 ? LANDING_FOV : panel == 5 ? 12f : BODY_FOV;
                camera.cullingMask = (oldMask & ~(1 << HIDDEN_LAYER)) | (1 << VISIBLE_LAYER);
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
                for (int i = 0; i < renderers.Count; ++i)
                {
                    renderers[i].gameObject.layer = layers[i];
                }
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                camera.cullingMask = oldMask;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgb;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(readback);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
