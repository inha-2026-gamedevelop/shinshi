using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    /// <summary>
    /// 시험 배치를 사용자 씬이 아닌 임시 프리뷰 씬에 만들게 하는 명시적 범위. 테스트처럼 샌드박스 씬이 없는 곳에서만 쓴다.
    /// 빈 씬 경로나 임의의 미저장 씬은 범위로 인정하지 않으며 Dispose가 프리뷰 씬과 그 안의 시험 배치를 모두 없앤다
    /// </summary>
    public sealed class PresentationFixtureScope : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_NAME = "UiSandboxPresentationScope";

        private Scene _scene;
        private bool  _disposed;

        /****************************************
        *              Properties
        ****************************************/

        public Scene Scene
        {
            get { return _scene; }
        }

        public bool IsOpen
        {
            get { return (!_disposed) && (_scene.IsValid()); }
        }

        /****************************************
        *                Methods
        ****************************************/

        internal PresentationFixtureScope()
        {
            _scene      = EditorSceneManager.NewPreviewScene();
            _scene.name = SCENE_NAME;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            PresentationFixtureFactory.OnScopeClosed(this);
        }
    }
}
