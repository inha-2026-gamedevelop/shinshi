#if UNITY_EDITOR
// System
using System;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 프로젝트 내부
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 스킬 쇼케이스 씬을 에디터 카메라로 프레임 단위 오프라인 렌더해 mp4로 녹화한다 </summary>
    public static class SkillShowcaseRecorder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    WIDTH       = 1280;
        private const int    HEIGHT      = 720;
        private const int    FRAME_RATE  = 30;
        private const int    DEPTH_BITS  = 24;
        private const float  FRAME_DELTA = 1f / FRAME_RATE;
        private const string VIDEO_EXT   = ".mp4";
        private const string MENU_RECORD = "Shinshi/Art/Skill Showcase/Record Video";
        private const string MENU_STOP   = "Shinshi/Art/Skill Showcase/Stop Recording";
        private const string LOG_PREFIX  = "[SkillShowcaseRecorder] ";

        private static MediaEncoder     _encoder    = null;
        private static RenderTexture    _target     = null;
        private static Texture2D        _readback   = null;
        private static DevSkillShowcase _showcase   = null;
        private static Camera           _camera     = null;
        private static bool             _manualOn   = false;
        private static bool             _subscribed = false;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsRecording { get; private set; }
        public static int Frame { get; private set; }
        public static int TotalFrames { get; private set; }
        public static string LastError { get; private set; } = string.Empty;
        public static string OutputPath { get; private set; } = string.Empty;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_RECORD)]
        private static void RecordFromMenu()
        {
            string path = EditorUtility.SaveFilePanel("Skill showcase video", string.Empty, "SkillShowcase", "mp4");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Start(path);
        }

        [MenuItem(MENU_STOP)]
        private static void StopFromMenu() { Stop(); }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 녹화를 시작한다, 시작하지 못하면 false를 돌려주고 LastError에 이유를 남긴다 </summary>
        public static bool Start(string outputPath)
        {
            if (IsRecording)
            {
                return Fail("이미 녹화 중이다");
            }

            if (!CheckEditorReady(out string stateError))
            {
                return Fail(stateError);
            }

            if ((string.IsNullOrEmpty(outputPath)) || (!Path.IsPathRooted(outputPath)))
            {
                return Fail("출력 경로는 절대 경로여야 한다: " + outputPath);
            }

            if (!string.Equals(Path.GetExtension(outputPath), VIDEO_EXT, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("출력 파일 확장자는 .mp4여야 한다: " + outputPath);
            }

            if (!TryFindShowcase(out DevSkillShowcase showcase, out string showcaseError))
            {
                return Fail(showcaseError);
            }

            int totalFrames = Mathf.CeilToInt(showcase.Duration * FRAME_RATE);
            if (totalFrames <= 0)
            {
                return Fail("쇼케이스 Duration이 0 이하다: " + showcase.Duration);
            }

            _showcase = showcase;
            _camera = showcase.ShowCamera;
            LastError = string.Empty;
            OutputPath = outputPath;
            Frame = 0;
            TotalFrames = totalFrames;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                VideoTrackAttributes video = new VideoTrackAttributes
                {
                    frameRate = new MediaRational(FRAME_RATE),
                    width = (uint)WIDTH,
                    height = (uint)HEIGHT,
                    includeAlpha = false,
                    bitRateMode = VideoBitrateMode.High
                };

                _encoder = new MediaEncoder(outputPath, video);
                _target = new RenderTexture(WIDTH, HEIGHT, DEPTH_BITS, RenderTextureFormat.ARGB32);
                _target.Create();
                // 인코더 AddFrame이 받는 CPU 텍스처 포맷은 RGBA32이고, includeAlpha가 꺼져 있어 알파는 버려진다
                _readback = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);

                _showcase.BeginManual();
                _manualOn = true;
            }
            catch (Exception e)
            {
                Cleanup();
                return Fail("녹화 시작 중 예외: " + e.Message);
            }

            IsRecording = true;
            Subscribe();
            return true;
        }

        /// <summary> 진행 중인 녹화를 중단하고 인코더와 임시 자원을 정리한다 </summary>
        public static void Stop()
        {
            if (!IsRecording)
            {
                return;
            }

            Cleanup();
        }

        /// <summary> 한 시점의 포즈를 렌더해 PNG 한 장으로 저장한다, 카메라와 포즈 검증용 </summary>
        public static void Capture(float time, string pngPath)
        {
            if (IsRecording)
            {
                Fail("녹화 중에는 단일 캡처를 할 수 없다");
                return;
            }

            if (!CheckEditorReady(out string stateError))
            {
                Fail(stateError);
                return;
            }

            if ((string.IsNullOrEmpty(pngPath)) || (!Path.IsPathRooted(pngPath)))
            {
                Fail("PNG 경로는 절대 경로여야 한다: " + pngPath);
                return;
            }

            if (!TryFindShowcase(out DevSkillShowcase showcase, out string showcaseError))
            {
                Fail(showcaseError);
                return;
            }

            LastError = string.Empty;
            RenderTexture target = null;
            Texture2D readback = null;
            bool manualOn = false;
            try
            {
                target = new RenderTexture(WIDTH, HEIGHT, DEPTH_BITS, RenderTextureFormat.ARGB32);
                target.Create();
                readback = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);

                showcase.BeginManual();
                manualOn = true;
                showcase.EvaluateFrame(time, 0f);
                RenderFrame(showcase.ShowCamera, target, readback);

                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, readback.EncodeToPNG());
            }
            catch (Exception e)
            {
                Fail("단일 캡처 중 예외: " + e.Message);
            }
            finally
            {
                if (manualOn)
                {
                    try
                    {
                        showcase.EndManual();
                    }
                    catch (Exception e)
                    {
                        Fail("EndManual 예외: " + e.Message);
                    }
                }

                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }
            }
        }

        private static bool CheckEditorReady(out string error)
        {
            error = string.Empty;
            if ((EditorApplication.isPlaying) || (EditorApplication.isPlayingOrWillChangePlaymode))
            {
                error = "플레이 모드에서는 녹화하지 않는다, EditMode에서 실행한다";
                return false;
            }

            if ((EditorApplication.isCompiling) || (EditorApplication.isUpdating))
            {
                error = "컴파일 또는 에셋 갱신 중이다";
                return false;
            }

            return true;
        }

        private static bool TryFindShowcase(out DevSkillShowcase showcase, out string error)
        {
            error = string.Empty;
            showcase = UnityEngine.Object.FindFirstObjectByType<DevSkillShowcase>();
            if (showcase == null)
            {
                error = "열린 씬에 DevSkillShowcase가 없다";
                return false;
            }

            if (showcase.ShowCamera == null)
            {
                error = "DevSkillShowcase.ShowCamera가 비어 있다";
                return false;
            }

            return true;
        }

        private static void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            EditorApplication.update += OnUpdate;
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            _subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            EditorApplication.update -= OnUpdate;
            EditorApplication.quitting -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            _subscribed = false;
        }

        private static void OnUpdate()
        {
            if (!IsRecording)
            {
                Unsubscribe();
                return;
            }

            if ((_showcase == null) || (_camera == null))
            {
                Fail("녹화 도중 DevSkillShowcase 또는 카메라가 사라졌다");
                Cleanup();
                return;
            }

            try
            {
                float time = Frame / (float)FRAME_RATE;
                _showcase.EvaluateFrame(time, FRAME_DELTA);
                RenderFrame(_camera, _target, _readback);
                _encoder.AddFrame(_readback);
                ++Frame;
            }
            catch (Exception e)
            {
                Fail("프레임 " + Frame + " 녹화 중 예외: " + e.Message);
                Cleanup();
                return;
            }

            if (Frame >= TotalFrames)
            {
                Cleanup();
            }
        }

        // Camera.Render 대신 렌더 요청을 쓴다, URP에서는 카메라를 지정한 RT로 직접 그리는 지원 경로가 이것이다
        private static void RenderFrame(Camera camera, RenderTexture target, Texture2D readback)
        {
            Canvas.ForceUpdateCanvases();

            UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = target
            };

            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                throw new InvalidOperationException("현재 렌더 파이프라인이 SingleCameraRequest를 지원하지 않는다");
            }

            RenderPipeline.SubmitRenderRequest(camera, request);

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, WIDTH, HEIGHT), 0, 0, false);
                readback.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        // EndManual은 샘플한 포즈를 초기화할 수 있어 마지막 프레임을 인코더에 넣은 뒤 정리 단계에서 한 번만 부른다
        private static void Cleanup()
        {
            Unsubscribe();
            IsRecording = false;

            if (_encoder != null)
            {
                try
                {
                    _encoder.Dispose();
                }
                catch (Exception e)
                {
                    Fail("인코더 종료 예외: " + e.Message);
                }

                _encoder = null;
            }

            if ((_manualOn) && (_showcase != null))
            {
                try
                {
                    _showcase.EndManual();
                }
                catch (Exception e)
                {
                    Fail("EndManual 예외: " + e.Message);
                }
            }

            _manualOn = false;
            _showcase = null;
            _camera = null;

            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }

            if (_readback != null)
            {
                UnityEngine.Object.DestroyImmediate(_readback);
                _readback = null;
            }
        }

        private static bool Fail(string message)
        {
            LastError = message;
            Debug.LogError(LOG_PREFIX + message);
            return false;
        }
    }
}
#endif
