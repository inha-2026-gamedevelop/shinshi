// Unity
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class ReleaseSceneUiTests
    {
        // 레이아웃 가져오기 도구가 만든 상자는 raycastTarget이 꺼져 있다. 그 위에 Button만 붙이면 코드 호출로는 동작하지만
        // 마우스 클릭이 닿지 않는다(릴리즈 빌드 캐릭터 선택 화면에서 실제로 발생)

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(Constants.Scene.LOBBY)]
        [TestCase(Constants.Scene.CHARACTER_SELECT)]
        [TestCase(Constants.Scene.CHARACTER_CREATE)]
        public void Selectables_InReleaseScene_ReceivePointerRaycasts(string sceneName)
        {
            string path  = Constants.Scene.RELEASE_FOLDER + sceneName + Constants.Scene.EXTENSION;
            Scene  scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int checkedCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
                    {
                        Assert.IsNotNull(selectable.targetGraphic, $"{sceneName}/{selectable.name}: targetGraphic이 없다");
                        Assert.IsTrue(selectable.targetGraphic.raycastTarget,
                            $"{sceneName}/{selectable.name}: targetGraphic의 raycastTarget이 꺼져 클릭이 닿지 않는다");
                        ++checkedCount;
                    }
                }

                Assert.Greater(checkedCount, 0, $"{sceneName}: 검사할 버튼이나 입력란이 없다");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
