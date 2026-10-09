using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    /// <summary> 테스트 전용 임시 씬. 사용자가 열어 둔 씬에는 오브젝트를 만들지 않고, 정리할 때 이전 활성 씬을 되돌린다. 사용자 씬은 닫지도 저장하지도 않는다 </summary>
    internal sealed class UiSandboxTestScene : IDisposable
    {
        private readonly Scene _scene;
        private readonly Scene _previousActive;
        private bool _disposed = false;

        private UiSandboxTestScene(Scene scene, Scene previousActive)
        {
            _scene          = scene;
            _previousActive = previousActive;
        }

        public Scene Scene
        {
            get { return _scene; }
        }

        /// <summary> 열린 씬 목록에 나타나지 않는 미리보기 씬. 사용자 씬과 활성 씬에 영향이 없다 </summary>
        public static UiSandboxTestScene CreatePreview()
        {
            return new UiSandboxTestScene(EditorSceneManager.NewPreviewScene(), SceneManager.GetActiveScene());
        }

        /// <summary> 최상위 오브젝트는 이 씬에 만든다. parent가 있으면 그 아래에 둔다 </summary>
        public GameObject Spawn(string name, GameObject parent = null)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent.transform);
            }
            else
            {
                SceneManager.MoveGameObjectToScene(go, _scene);
            }

            return go;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if ((_previousActive.IsValid()) && (_previousActive.isLoaded) && (SceneManager.GetActiveScene() != _previousActive))
            {
                SceneManager.SetActiveScene(_previousActive);
            }

            if (!_scene.IsValid())
            {
                return;
            }

            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
