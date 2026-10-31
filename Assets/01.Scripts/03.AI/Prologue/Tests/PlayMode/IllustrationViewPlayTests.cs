// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class IllustrationViewPlayTests
    {
        private IllustrationView _view;
        private int              _finishedCount;

        private static readonly IllustrationPage[] PAGES =
        {
            new IllustrationPage(null, "첫 번째 장의 글입니다."),
            new IllustrationPage(null, "두 번째 장의 글입니다.")
        };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _finishedCount = 0;
            _view = IllustrationView.Create();
            _view.Finished += () => ++_finishedCount;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_view != null)
            {
                Object.Destroy(_view.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Click_WhileTyping_DoesNotMoveToNextPage()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.HandleClick();

            Assert.AreEqual(0, _finishedCount);
            Assert.IsTrue(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Click_ThroughAllPages_FinishesOnce()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            // 장마다 글 완성 클릭 하나, 다음 클릭 하나
            _view.HandleClick();
            _view.HandleClick();
            _view.HandleClick();
            Assert.AreEqual(0, _finishedCount);

            _view.HandleClick();
            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
            Assert.IsFalse(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Skip_Midway_FinishesAtOnce()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.HandleSkip();
            _view.HandleSkip();

            Assert.AreEqual(1, _finishedCount);
        }

        [UnityTest]
        public IEnumerator Show_StartRevealedSinglePage_ClosesWithOneClick()
        {
            _view.Show(new[] { new IllustrationPage(null, "신시의 부적과 닮은 문양이다.") }, true, false);
            yield return null;

            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
        }

        private static Sprite MakeSprite(Color color)
        {
            var texture = new Texture2D(4, 4);
            var pixels  = new Color[16];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }

        private Image Previous()
        {
            Transform previous = _view.transform.Find("InkPrevious");
            Assert.IsNotNull(previous);
            return previous.GetComponent<Image>();
        }

        [UnityTest]
        public IEnumerator Ink_NextPageWithMaterial_ShowsPreviousImageUntilClicked()
        {
            Shader shader = Shader.Find("Shinshi/UI/InkWipe");
            Assert.IsNotNull(shader);
            _view.SetTransitionMaterial(new Material(shader));

            var pages = new[]
            {
                new IllustrationPage(MakeSprite(Color.red), "첫 장"),
                new IllustrationPage(MakeSprite(Color.blue), "둘째 장")
            };
            _view.Show(pages, false, true);
            yield return null;

            _view.HandleClick();
            Assert.IsFalse(Previous().enabled);

            _view.HandleClick();
            Assert.IsTrue(Previous().enabled);

            // 장이 넘어갈 때의 이전 그림은 새 그림과 같은 비율로 놓인다
            Assert.IsTrue(Previous().preserveAspect);

            // 지워지는 중에 누르면 전환이 바로 끝난다
            _view.HandleClick();
            Assert.IsFalse(Previous().enabled);
        }

        [UnityTest]
        public IEnumerator Ink_WithoutMaterial_ChangesPageWithoutPreviousImage()
        {
            var pages = new[]
            {
                new IllustrationPage(MakeSprite(Color.red), "첫 장"),
                new IllustrationPage(MakeSprite(Color.blue), "둘째 장")
            };
            _view.Show(pages, false, true);
            yield return null;

            _view.HandleClick();
            _view.HandleClick();

            Assert.IsFalse(Previous().enabled);
        }

        [UnityTest]
        public IEnumerator Ink_FirstPage_AppearsFromInkCoverThatClearsOnClick()
        {
            Shader shader = Shader.Find("Shinshi/UI/InkWipe");
            _view.SetTransitionMaterial(new Material(shader));

            _view.Show(new[] { new IllustrationPage(MakeSprite(Color.red), "한 장") }, false, true);
            yield return null;

            // 첫 장은 먹색 막이 깔린 채 시작해 걷히며 나타난다
            Image cover = Previous();
            Assert.IsTrue(cover.enabled);
            Assert.AreEqual(0.04f, cover.color.r, 0.001f);

            // 막은 화면 전체를 덮어야 하므로 비율을 유지해 정사각형으로 줄이지 않는다
            Assert.IsFalse(cover.preserveAspect);

            _view.HandleClick();

            Assert.IsFalse(cover.enabled);
        }

        [UnityTest]
        public IEnumerator Ink_ShowAgain_DoesNotContinueFromPreviousRunsLastImage()
        {
            Shader shader = Shader.Find("Shinshi/UI/InkWipe");
            _view.SetTransitionMaterial(new Material(shader));
            Sprite red = MakeSprite(Color.red);

            _view.Show(new[] { new IllustrationPage(red, "첫 구간") }, true, true);
            yield return null;
            _view.HandleClick();
            _view.Close();

            // 두 번째 구간의 첫 장도 이전 구간의 마지막 그림이 아니라 먹색 막에서 시작한다
            _view.Show(new[] { new IllustrationPage(MakeSprite(Color.blue), "둘째 구간") }, true, true);
            yield return null;

            Image cover = Previous();
            Assert.IsTrue(cover.enabled);
            Assert.AreEqual(0.04f, cover.color.r, 0.001f);
        }

        [UnityTest]
        public IEnumerator Ink_FinishedByTime_HidesPreviousImage()
        {
            Shader shader = Shader.Find("Shinshi/UI/InkWipe");
            _view.SetTransitionMaterial(new Material(shader));
            var pages = new[]
            {
                new IllustrationPage(MakeSprite(Color.red), "첫 장"),
                new IllustrationPage(MakeSprite(Color.blue), "둘째 장")
            };
            _view.Show(pages, true, true);
            yield return null;

            _view.HandleClick();
            Assert.IsTrue(Previous().enabled);

            // 전환 시간(기본 2.4초)이 지나면 스스로 사라진다
            float waited = 0f;
            while ((Previous().enabled) && (waited < 4f))
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(Previous().enabled);
        }

        private string TextAt(string path)
        {
            Transform target = _view.transform.Find(path);
            Assert.IsNotNull(target, path);
            return target.GetComponent<TMPro.TMP_Text>().text;
        }

        [UnityTest]
        public IEnumerator SilentPage_FirstClickGoesToNextPageAndShadeIsHidden()
        {
            var pages = new[]
            {
                new IllustrationPage(null, string.Empty),
                new IllustrationPage(null, "둘째 장")
            };
            _view.Show(pages, false, true);
            yield return null;

            Assert.IsFalse(_view.transform.Find("Shade").gameObject.activeSelf);

            // 출력할 글이 없으니 글 완성 단계 없이 첫 클릭이 바로 다음 장이다
            _view.HandleClick();

            Assert.AreEqual("둘째 장", TextAt("Panel/Body"));
            Assert.IsTrue(_view.transform.Find("Shade").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator SingleRevealedPage_HintSaysCloseInsteadOfNext()
        {
            _view.Show(new[] { new IllustrationPage(null, "신시의 부적과 닮은 문양이다.") }, true, false);
            yield return null;

            Assert.AreEqual("클릭하여 닫기", TextAt("Panel/Hint"));
        }

        [UnityTest]
        public IEnumerator MultiPage_HintSaysNextAfterTextIsRevealed()
        {
            _view.Show(new[] { new IllustrationPage(null, "첫 장"), new IllustrationPage(null, "둘째 장") }, true, true);
            yield return null;

            Assert.AreEqual("클릭하여 다음으로", TextAt("Panel/Hint"));
        }

        [UnityTest]
        public IEnumerator Show_WithSkipLabel_AppliesItToTheSkipButton()
        {
            _view.Show(PAGES, false, true, "도입 이야기 건너뛰기");
            yield return null;

            Assert.AreEqual("도입 이야기 건너뛰기", TextAt("Skip/Label"));
        }

        [UnityTest]
        public IEnumerator Show_WithoutSkipLabel_UsesDefaultLabel()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            Assert.AreEqual("건너뛰기", TextAt("Skip/Label"));
        }

        [UnityTest]
        public IEnumerator Close_KeepsViewActiveForOneFrameThenHidesIt()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.Close();

            // 닫는 클릭이 같은 프레임에 전투 입력으로 읽히지 않도록 한 프레임은 입력을 막은 채 남는다
            Assert.IsTrue(_view.gameObject.activeSelf);

            yield return null;
            yield return null;

            Assert.IsFalse(_view.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Show_RightAfterClose_StaysVisible()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.Close();
            _view.Show(PAGES, false, true);

            yield return null;
            yield return null;

            Assert.IsTrue(_view.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Click_AfterFinished_DoesNotFinishAgain()
        {
            _view.Show(new[] { new IllustrationPage(null, "한 장") }, true, false);
            yield return null;

            _view.HandleClick();
            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
        }
    }
}
