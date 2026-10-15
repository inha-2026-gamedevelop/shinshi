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
    public static class WolbaekTalismanCapture
    {
        /****************************************
        *                Fields
        ****************************************/
        private const int WIDTH                  = 1280;
        private const int HEIGHT                 = 720;
        private const int SUPERSAMPLE            = 2;
        private const int HIDDEN_LAYER           = 30;
        private const int VISIBLE_LAYER          = 31;
        private const float CLOSE_FOV            = 34f;
        private const float WIDE_FOV             = 40f;
        private const float DETAIL_FOV           = 23f;
        private const int DEPTH_BITS             = 24;
        private const float SUCCESS_START        = 10f;
        private const float SUCCESS_END          = 12.7f;
        private const float WIDE_FOCUS_HEIGHT    = 0.8f;
        private const float DETAIL_FOCUS_HEIGHT  = 1.35f;
        private const float SUCCESS_FOCUS_HEIGHT = 1.4f;
        private const float CLOSE_FOCUS_HEIGHT   = 0.75f;
        private static readonly Vector3 _closeOffset = new Vector3(5.7f, 6.7f, -7.5f);
        private static readonly Vector3 _wideOffset  = new Vector3(10f, 20f, -26f);

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 전용 개발 씬의 실제 PlayMode Camera.Render 결과를 기록한다 </summary>
        public static void Capture(string filename, int panel, float seconds, bool restart = false, bool detail = false)
        {
            if ((!EditorApplication.isPlaying) || (string.IsNullOrEmpty(filename)))
            {
                throw new InvalidOperationException("PlayMode 캡처와 출력 경로가 필요하다");
            }
            WolbaekTalismanPreview preview = UnityEngine.Object.FindAnyObjectByType<WolbaekTalismanPreview>();
            Camera camera = Camera.main;
            if ((preview == null) || (camera == null))
            {
                throw new InvalidOperationException("사방 부적진 개발 씬을 먼저 열어야 한다");
            }
            if (restart)
            {
                preview.Begin(true);
            }
            preview.Sample(seconds);
            WolbaekTalismanPlayer[] players = UnityEngine.Object.FindObjectsByType<WolbaekTalismanPlayer>();
            Array.Sort(players, (left, right) => left.Settings.DefaultSign.CompareTo(right.Settings.DefaultSign));
            if ((panel < -1) || (panel >= players.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(panel));
            }
            bool rising = (seconds >= SUCCESS_START) && (seconds < SUCCESS_END);
            Vector3 point = panel < 0 ? Vector3.up * WIDE_FOCUS_HEIGHT : players[panel].transform.position
                + Vector3.up * (detail ? DETAIL_FOCUS_HEIGHT : rising ? SUCCESS_FOCUS_HEIGHT : CLOSE_FOCUS_HEIGHT);
            List<Renderer> renderers = new List<Renderer>();
            List<int> layers = new List<int>();
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
                camera.transform.position = point + (panel < 0 ? _wideOffset : _closeOffset);
                camera.transform.LookAt(point);
                camera.fieldOfView = panel < 0 ? WIDE_FOV : detail ? DETAIL_FOV : CLOSE_FOV;
                camera.cullingMask = (oldMask & ~(1 << HIDDEN_LAYER)) | (1 << VISIBLE_LAYER);
                for (int i = 0; i < players.Length; ++i)
                {
                    players[i].FaceGlyph(players[i].PlaybackId, camera.transform.position);
                    foreach (Renderer renderer in players[i].GetComponentsInChildren<Renderer>(true))
                    {
                        renderers.Add(renderer);
                        layers.Add(renderer.gameObject.layer);
                        renderer.gameObject.layer = (panel < 0) || (i == panel) ? VISIBLE_LAYER : HIDDEN_LAYER;
                        SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                        if (skin != null)
                        {
                            skin.forceMatrixRecalculationPerRender = true;
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
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                camera.cullingMask = oldMask;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgb;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(readback);
                UnityEngine.Object.DestroyImmediate(texture);
                for (int i = 0; i < players.Length; ++i)
                {
                    players[i].FaceGlyph(players[i].PlaybackId, oldPosition);
                }
            }
        }
    }
}
