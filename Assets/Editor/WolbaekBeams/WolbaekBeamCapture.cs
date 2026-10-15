// System
using System;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    public static class WolbaekBeamCapture
    {
        private const int PANEL_WIDTH = 960;
        private const int PANEL_HEIGHT = 540;
        private const int CAPTURE_LAYER = 31;
        private const int HIDDEN_LAYER = 30;
        private static readonly Vector3[] G_Targets =
        {
            new Vector3(-4.5f, 0f, -0.5f),
            new Vector3(2.75f, 0f, -0.5f),
            new Vector3(-4f, 0f, 6.5f),
            new Vector3(4f, 0f, 6.5f)
        };

        public static void CaptureBoard(string filename, float seconds, bool restart = true)
        {
            if (!EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("PlayMode 캡처만 지원");
            }

            WolbaekBeamPreview preview = UnityEngine.Object.FindAnyObjectByType<WolbaekBeamPreview>();
            if (restart)
            {
                preview.Begin(true);
                for (int i = 0; i <= Mathf.CeilToInt(seconds * 24f); ++i)
                {
                    preview.Sample(Mathf.Min(seconds, i / 24f));
                }
            }
            else
            {
                preview.Sample(seconds);
            }

            Camera camera = GameObject.Find("Beams_GameplayCamera").GetComponent<Camera>();
            WolbaekBeamPlayer[] players = UnityEngine.Object.FindObjectsByType<WolbaekBeamPlayer>();
            Renderer[][] groups = new Renderer[4][];
            int[][] layers = new int[4][];
            for (int i = 0; i < players.Length; ++i)
            {
                int kind = (int)players[i].Kind;
                groups[kind] = players[i].GetComponentsInChildren<Renderer>(true);
                layers[kind] = new int[groups[kind].Length];
                for (int r = 0; r < groups[kind].Length; ++r)
                {
                    layers[kind][r] = groups[kind][r].gameObject.layer;
                    SkinnedMeshRenderer skin = groups[kind][r] as SkinnedMeshRenderer;
                    if (skin != null)
                    {
                        skin.forceMatrixRecalculationPerRender = true;
                    }
                }
            }

            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            int mask = camera.cullingMask;
            float fov = camera.fieldOfView;
            RenderTexture previous = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(PANEL_WIDTH, PANEL_HEIGHT, 24, RenderTextureFormat.ARGBHalf);
            RenderTexture readback = RenderTexture.GetTemporary(PANEL_WIDTH, PANEL_HEIGHT, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            bool previousSrgbWrite = GL.sRGBWrite;
            Texture2D panel = new Texture2D(PANEL_WIDTH, PANEL_HEIGHT, TextureFormat.RGB24, false);
            Texture2D board = new Texture2D(PANEL_WIDTH * 2, PANEL_HEIGHT * 2, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.fieldOfView = 38f;
                camera.cullingMask = (mask & ~(1 << HIDDEN_LAYER)) | (1 << CAPTURE_LAYER);
                for (int kind = 0; kind < 4; ++kind)
                {
                    for (int group = 0; group < 4; ++group)
                    {
                        for (int r = 0; r < groups[group].Length; ++r)
                        {
                            bool visible = group == kind;
                            if ((kind == 1) && (group == 1) && (groups[group][r].transform.parent.name == "LineSlot_1"))
                            {
                                visible = false;
                            }
                            groups[group][r].gameObject.layer = visible ? CAPTURE_LAYER : HIDDEN_LAYER;
                        }
                    }

                    Vector3 offset = kind == 2 ? new Vector3(0.5f, 7.2f, -3.5f) : new Vector3(4.5f, 4.8f, -4.8f);
                    camera.transform.position = G_Targets[kind] + offset;
                    camera.transform.LookAt(G_Targets[kind]);
                    camera.Render();
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(target, readback);
                    RenderTexture.active = readback;
                    panel.ReadPixels(new Rect(0f, 0f, PANEL_WIDTH, PANEL_HEIGHT), 0, 0);
                    panel.Apply();
                    board.SetPixels(kind % 2 * PANEL_WIDTH, (1 - kind / 2) * PANEL_HEIGHT, PANEL_WIDTH, PANEL_HEIGHT, panel.GetPixels());
                }

                board.Apply();
                File.WriteAllBytes(filename, board.EncodeToPNG());
            }
            finally
            {
                for (int kind = 0; kind < 4; ++kind)
                {
                    for (int r = 0; r < groups[kind].Length; ++r)
                    {
                        groups[kind][r].gameObject.layer = layers[kind][r];
                    }
                }

                camera.transform.SetPositionAndRotation(position, rotation);
                camera.cullingMask = mask;
                camera.fieldOfView = fov;
                camera.targetTexture = previous;
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(readback);
                UnityEngine.Object.DestroyImmediate(panel);
                UnityEngine.Object.DestroyImmediate(board);
            }
        }
    }
}
