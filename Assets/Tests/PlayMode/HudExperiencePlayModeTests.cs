// System
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third Party
using NUnit.Framework;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.UI;
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public sealed class HudExperiencePlayModeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private GameObject _root;
        private CharacterLevelConfigSO _config;
        private GameObject _player;

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator ExperienceNotice_FadesReusesTweenAndResetsOnActualDisable()
        {
            _config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            CharacterLevelSettings settings = _config.ToSettings();
            _root = new GameObject("ExperiencePlayModeTest", typeof(RectTransform));
            HudPlayerStatusView status = _root.AddComponent<HudPlayerStatusView>();
            status.SetExperience(settings, 2850);
            HudExperienceView view = _root.GetComponentInChildren<HudExperienceView>();
            Assert.IsNotNull(view);
            Assert.IsFalse(view.NoticeVisible);
            int childCount = _root.GetComponentsInChildren<RectTransform>(true).Length;
            FieldInfo tweenField = typeof(HudExperienceView).GetField("_noticeTween",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNull(tweenField.GetValue(view));

            status.SetExperience(settings, 4250);
            Assert.IsTrue(view.NoticeVisible);
            object tween = tweenField.GetValue(view);
            Assert.IsNotNull(tween);
            // 표시 유지 1.6초 + 페이드 0.4초의 실제 unscaled 재생 완료를 기다린다.
            yield return new WaitForSecondsRealtime(2.2f);
            Assert.IsFalse(view.NoticeVisible);

            status.SetExperience(settings, 5925);
            Assert.IsTrue(view.NoticeVisible);
            Assert.AreSame(tween, tweenField.GetValue(view));
            _root.SetActive(false);
            yield return null;
            Assert.IsFalse(view.NoticeVisible);
            _root.SetActive(true);
            yield return null;
            status.SetExperience(settings, 120350);
            Assert.IsFalse(view.NoticeVisible);
            Assert.AreEqual("EXP MAX", view.DisplayText);
            Assert.AreEqual(childCount, _root.GetComponentsInChildren<RectTransform>(true).Length);
            Assert.AreEqual(1, _root.GetComponentsInChildren<HudExperienceView>(true).Length);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_player != null)
            {
                LocalPlayerHudBinder binder = _player.GetComponent<LocalPlayerHudBinder>();
                if (binder != null)
                {
                    typeof(LocalPlayerHudBinder).GetMethod("Unbind", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(binder, null);
                }
                Object.Destroy(_player);
            }
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            if (_config != null)
            {
                Object.Destroy(_config);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Fallback_ReplacedInSameFrame_IsNotReselectedAndIsDestroyedAfterYield()
        {
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.NonPublic;
            _player = new GameObject("FallbackPlayModeTest", typeof(Mirror.NetworkIdentity));
            LocalPlayerHudBinder binder = _player.AddComponent<LocalPlayerHudBinder>();
            // 미스폰 Mirror 개체는 네트워크 Update를 실행하지 않고 HUD 소유/파괴 경로만 검사한다.
            _player.GetComponent<PlayerInputState>().enabled = false;
            binder.enabled = false;
            MethodInfo resolve = typeof(LocalPlayerHudBinder).GetMethod("ResolveStatusHud", FLAGS);
            FieldInfo owned = typeof(LocalPlayerHudBinder).GetField("_ownedStatusHud", FLAGS);
            FieldInfo selected = typeof(LocalPlayerHudBinder).GetField("_statusView", FLAGS);
            resolve.Invoke(binder, new object[] { new HudPlayerStatusView[0] });
            var fallback = (GameObject)owned.GetValue(binder);
            Assert.IsNotNull(fallback);
            HudPlayerStatusView retiring = fallback.GetComponentInChildren<HudPlayerStatusView>(true);
            _root = new GameObject("SceneStatusHud", typeof(RectTransform));
            HudPlayerStatusView sceneStatus = _root.AddComponent<HudPlayerStatusView>();
            var candidates = new[] { retiring, sceneStatus };
            resolve.Invoke(binder, new object[] { candidates });
            Assert.IsTrue(fallback != null, "Destroy is deferred until the end of this frame");
            Assert.IsFalse(fallback.activeSelf);
            Assert.IsFalse(retiring.enabled);
            Assert.IsNull(owned.GetValue(binder));
            resolve.Invoke(binder, new object[] { candidates });
            Assert.AreSame(sceneStatus, selected.GetValue(binder));
            Assert.IsNull(owned.GetValue(binder));
            typeof(LocalPlayerHudBinder).GetMethod("Unbind", FLAGS).Invoke(binder, null);
            Assert.IsTrue(sceneStatus != null);
            yield return null;
            Assert.IsTrue(fallback == null);
            Assert.IsTrue(sceneStatus != null);
        }
    }
}
