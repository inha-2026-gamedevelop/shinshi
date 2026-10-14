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
    public static class WolbaekWardCapture
    {
        /****************************************
        *                Fields
        ****************************************/
        private const int WIDTH = 1280;
        private const int HEIGHT = 720;
        private const int SUPERSAMPLE = 2;
        private const int HIDDEN_LAYER = 30;
        private const int VISIBLE_LAYER = 31;
        private const int WARD_PANELS = 2;
        private const int PLAYERS_PER_PAIR = 2;
        private const int BLUE_LINK_PANEL = 3;
        private const int DEPTH_BITS = 24;
        private const float WIDE_FOV = 39f;
        private const float WARD_FOV = 34f;
        private const float LINK_FOV = 34f;
        private const float WIDE_TARGET_HEIGHT = 0.7f;
        private const float WARD_TARGET_HEIGHT = 0.65f;
        private const float LINK_TARGET_HEIGHT = 0.8f;
        private const float HALF = 0.5f;
        private static readonly Vector3 _wideOffset     = new Vector3(7f, 12f, -17f);
        private static readonly Vector3 _wardOffset     = new Vector3(5.7f, 6.7f, -7.5f);
        private static readonly Vector3 _linkOffset     = new Vector3(3.7f, 4.5f, -6.7f);
        private static readonly Vector3 _blueLinkOffset = new Vector3(-3.7f, 4.5f, -6.7f);

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 결계 전용 개발 씬의 실제 PlayMode 카메라 결과를 기록한다 </summary>
        public static void Capture(string filename, int panel, float seconds, bool restart = false)
        {
            if ((!EditorApplication.isPlaying) || string.IsNullOrEmpty(filename))
            {
                throw new InvalidOperationException("PlayMode와 출력 경로가 필요하다");
            }
            WolbaekWardPreview preview = UnityEngine.Object.FindAnyObjectByType<WolbaekWardPreview>();
            Camera camera = Camera.main;
            if ((preview == null) || (camera == null))
            {
                throw new InvalidOperationException("결계 전용 개발 씬을 열어야 한다");
            }
            if ((panel < -1) || (panel >= WARD_PANELS * PLAYERS_PER_PAIR))
            {
                throw new ArgumentOutOfRangeException(nameof(panel));
            }
            if (restart)
            {
                preview.Begin(true);
            }
            preview.Sample(seconds);
            Vector3 point = Vector3.up * WIDE_TARGET_HEIGHT;
            if ((panel >= 0) && (panel < WARD_PANELS))
            {
                point = preview.Wards[panel].CurrentSnapshot.WorldCenter + Vector3.up * WARD_TARGET_HEIGHT;
            }
            else if (panel >= WARD_PANELS)
            {
                int pair = panel - WARD_PANELS;
                point = (preview.Players[pair * PLAYERS_PER_PAIR].position
                    + preview.Players[pair * PLAYERS_PER_PAIR + 1].position) * HALF + Vector3.up * LINK_TARGET_HEIGHT;
            }
            List<Renderer> renderers = new List<Renderer>();
            List<int> layers = new List<int>();
            List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            List<bool> recalculate = new List<bool>();
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            int oldMask = camera.cullingMask;
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            RenderTexture target = RenderTexture.GetTemporary(WIDTH * SUPERSAMPLE, HEIGHT * SUPERSAMPLE, DEPTH_BITS, RenderTextureFormat.ARGBHalf);
            target.filterMode = FilterMode.Bilinear;
            RenderTexture readback = RenderTexture.GetTemporary(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = point + (panel < 0 ? _wideOffset : panel < WARD_PANELS
                    ? _wardOffset : panel == BLUE_LINK_PANEL ? _blueLinkOffset : _linkOffset);
                camera.transform.LookAt(point);
                camera.fieldOfView = panel < 0 ? WIDE_FOV : panel < WARD_PANELS ? WARD_FOV : LINK_FOV;
                camera.cullingMask = (oldMask & ~(1 << HIDDEN_LAYER)) | (1 << VISIBLE_LAYER);
                for (int i = 0; i < WARD_PANELS; ++i)
                {
                    foreach (Renderer renderer in preview.Wards[i].GetComponentsInChildren<Renderer>(true))
                    {
                        SetCaptureLayer(renderer, (panel < 0) || (panel == i), renderers, layers, skins, recalculate);
                    }
                    foreach (Renderer renderer in preview.Links[i].GetComponentsInChildren<Renderer>(true))
                    {
                        SetCaptureLayer(renderer, (panel < 0) || (panel == i + WARD_PANELS), renderers, layers, skins, recalculate);
                    }
                    for (int j = 0; j < PLAYERS_PER_PAIR; ++j)
                    {
                        foreach (Renderer renderer in preview.Players[i * PLAYERS_PER_PAIR + j].GetComponentsInChildren<Renderer>(true))
                        {
                            SetCaptureLayer(renderer, (panel < 0) || (panel % WARD_PANELS == i), renderers, layers, skins, recalculate);
                        }
                    }
                }
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
                for (int i = 0; i < skins.Count; ++i)
                {
                    skins[i].forceMatrixRecalculationPerRender = recalculate[i];
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

        private static void SetCaptureLayer(Renderer renderer, bool visible, List<Renderer> renderers, List<int> layers,
            List<SkinnedMeshRenderer> skins, List<bool> recalculate)
        {
            renderers.Add(renderer);
            layers.Add(renderer.gameObject.layer);
            renderer.gameObject.layer = visible ? VISIBLE_LAYER : HIDDEN_LAYER;
            SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
            if (skin == null)
            {
                return;
            }
            skins.Add(skin);
            recalculate.Add(skin.forceMatrixRecalculationPerRender);
            skin.forceMatrixRecalculationPerRender = true;
        }
    }
}
