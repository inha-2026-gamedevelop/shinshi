// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 75%, 50%, 25%, 0% 기믹마다 실제 성공 사건으로 보호막을 끝내고, 시험은 ApplyAt을 부르지 않는다
    // 페이드는 WolbaekShieldFade.LateUpdate만으로 진행되어 제거 직전에 투명해져야 한다
    // 소멸 도중 Reset은 그 자리에서 지우고, 다시 시작한 보호막은 처음 밝기로 시작해야 한다
    public class WolbaekShieldFadePlayTests
    {
        private const string PREFAB_PATH            = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string SHIELD_NAME            = "SB_Shield_Blue(Clone)";
        private const float  FADE_SECONDS           = 2.5f;
        private const float  WARM_SECONDS           = 0.5f;
        private const float  RESET_AT_SECONDS       = 1.2f;
        private const float  REMOVE_GRACE_SECONDS   = 0.5f;
        private const int    LAST_FRAME_ALPHA_LIMIT = 2;

        private enum EGimmick
        {
            Talisman,
            ColorWard,
            FoxfireSeal,
            FinalArea,
        }

        private GameObject _boss;
        private BossView _bossView;
        private WolbaekTalismanView _talisman;
        private WolbaekColorWardView _ward;
        private WolbaekFoxfireSealView _seal;
        private WolbaekFinalAreaView _final;
        private double _clock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            _boss = Object.Instantiate(prefab);
            _bossView = _boss.GetComponent<BossView>();
            _talisman = _boss.GetComponent<WolbaekTalismanView>();
            _ward = _boss.GetComponent<WolbaekColorWardView>();
            _seal = _boss.GetComponent<WolbaekFoxfireSealView>();
            _final = _boss.GetComponent<WolbaekFinalAreaView>();
            Assert.IsNotNull(_bossView);
            Assert.IsNotNull(_talisman);
            Assert.IsNotNull(_ward);
            Assert.IsNotNull(_seal);
            Assert.IsNotNull(_final);
            _clock = 100d;
            _talisman.SetServerClock(() => _clock);
            _ward.SetServerClock(() => _clock);
            _seal.SetServerClock(() => _clock);
            _final.SetServerClock(() => _clock);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bossView != null)
            {
                _bossView.ResetView();
            }
            if (_boss != null)
            {
                Object.Destroy(_boss);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Talisman75_SuccessFadesByLateUpdate_ResetClearsMidFade_ReuseStartsFull()
        {
            yield return Run(EGimmick.Talisman);
        }

        [UnityTest]
        public IEnumerator ColorWard50_SuccessFadesByLateUpdate_ResetClearsMidFade_ReuseStartsFull()
        {
            yield return Run(EGimmick.ColorWard);
        }

        [UnityTest]
        public IEnumerator FoxfireSeal25_SuccessFadesByLateUpdate_ResetClearsMidFade_ReuseStartsFull()
        {
            yield return Run(EGimmick.FoxfireSeal);
        }

        [UnityTest]
        public IEnumerator FinalArea0_SuccessFadesByLateUpdate_ResetClearsMidFade_ReuseStartsFull()
        {
            yield return Run(EGimmick.FinalArea);
        }

        private IEnumerator Run(EGimmick gimmick)
        {
            // 1. 정상 성공: 매 프레임 최대 알파가 줄기만 하고, 제거 직전 프레임에는 보이지 않으며, 2.5초 뒤 지워진다
            yield return BeginAndWarm(gimmick, 1);
            GameObject first = FindShield(false);
            Assert.IsNotNull(first, "기믹 시작 때 보호막이 생겨야 한다");
            Assert.IsNull(first.GetComponent<WolbaekShieldFade>(), "성공 전에는 페이드가 붙지 않는다");
            int full = MaxAlpha(first);
            Assert.That(full, Is.GreaterThan(0));

            Succeed(gimmick, 1);
            WolbaekShieldFade fade = first.GetComponent<WolbaekShieldFade>();
            Assert.IsNotNull(fade, "실제 성공 경로가 페이드를 시작해야 한다");
            Assert.IsTrue(fade.IsFading);
            AssertNotEmitting(first);

            float started = Time.time;
            int previous = full;
            int lastVisible = full;
            bool sawMiddle = false;
            while ((first != null) && ((Time.time - started) < (FADE_SECONDS + REMOVE_GRACE_SECONDS)))
            {
                yield return null;
                if (first == null)
                {
                    break;
                }

                float elapsed = Time.time - started;
                int max = MaxAlpha(first);
                Assert.That(max, Is.LessThanOrEqualTo(previous), $"{elapsed:F3}초에 다시 밝아지면 안 된다");
                previous = max;
                lastVisible = max;
                if ((elapsed >= 1f) && (elapsed <= 1.6f))
                {
                    Assert.That(max, Is.InRange(1, full - 1), $"{elapsed:F3}초에는 절반쯤 낮아져 있어야 한다");
                    sawMiddle = true;
                }
            }
            Assert.IsTrue(first == null, "2.5초 제거 시각을 넘기면 보호막이 지워져야 한다");
            Assert.IsTrue(sawMiddle, "1초에서 1.6초 사이 프레임을 관찰해야 한다");
            Assert.That(lastVisible, Is.LessThanOrEqualTo(LAST_FRAME_ALPHA_LIMIT), "제거 직전 마지막 프레임은 보이지 않아야 한다");

            // 2. 재사용 뒤 소멸 도중 Reset: 처음 밝기로 시작하고, Reset 호출 즉시 비활성이 된다
            yield return BeginAndWarm(gimmick, 2);
            GameObject second = FindShield(false);
            Assert.IsNotNull(second);
            Assert.That(MaxAlpha(second), Is.EqualTo(full), "다시 만든 보호막은 이전 소멸의 영향을 받지 않는다");
            Succeed(gimmick, 2);
            Assert.IsNotNull(second.GetComponent<WolbaekShieldFade>());
            float secondStarted = Time.time;
            while ((Time.time - secondStarted) < RESET_AT_SECONDS)
            {
                yield return null;
            }
            Assert.IsNotNull(second);
            Assert.That(MaxAlpha(second), Is.InRange(1, full - 1));
            _bossView.ResetView();
            Assert.IsTrue((second == null) || (!second.activeInHierarchy), "Reset은 소멸 중인 보호막을 그 자리에서 지운다");
            yield return null;
            Assert.AreEqual(0, CountActiveShields());

            // 3. Reset 뒤 재사용: 새 보호막이 처음 밝기로 시작하고 같은 방식으로 지워진다
            yield return BeginAndWarm(gimmick, 3);
            GameObject third = FindShield(false);
            Assert.IsNotNull(third);
            Assert.IsNull(third.GetComponent<WolbaekShieldFade>());
            Assert.That(MaxAlpha(third), Is.EqualTo(full));
            Succeed(gimmick, 3);
            float thirdStarted = Time.time;
            while ((third != null) && ((Time.time - thirdStarted) < (FADE_SECONDS + REMOVE_GRACE_SECONDS)))
            {
                yield return null;
            }
            Assert.IsTrue(third == null);
            Assert.AreEqual(0, CountActiveShields());
        }

        private IEnumerator BeginAndWarm(EGimmick gimmick, int castId)
        {
            _clock = 100d + (castId * 100d);
            double now = _clock;
            bool started = false;
            switch (gimmick)
            {
                case EGimmick.Talisman:
                    {
                        WolbaekTalismanStart start = TalismanStart(castId, now);
                        started = _talisman.StartFormation(in start, now);
                        _talisman.HoldStarted(castId, now + 3d, now + 3d);
                        break;
                    }
                case EGimmick.ColorWard:
                    {
                        WolbaekWardStart start = WardStart(castId, now);
                        started = _ward.StartWard(in start, now);
                        break;
                    }
                case EGimmick.FoxfireSeal:
                    {
                        var start = new WolbaekSealStart(1, castId, now, now + 29d, 3, 3f, 4f, 2f, 1f, 2.5f, 3f, Vector3.zero);
                        started = _seal.StartSeal(in start, now);
                        break;
                    }
                case EGimmick.FinalArea:
                    {
                        var start = new WolbaekFinalStart(1, castId, now, now + 4d, 8f, 2, 10f, 4f, 2f, 2f, new Vector3(10f, 0f, -6f));
                        started = _final.StartFinal(in start, now);
                        break;
                    }
            }
            Assert.IsTrue(started, $"{gimmick} {castId}번 시전이 시작되어야 한다");
            float warmed = Time.time;
            while ((Time.time - warmed) < WARM_SECONDS)
            {
                yield return null;
            }
        }

        private void Succeed(EGimmick gimmick, int castId)
        {
            _clock = 100d + (castId * 100d) + 10d;
            double now = _clock;
            switch (gimmick)
            {
                case EGimmick.Talisman:
                    _talisman.Succeeded(castId, now, now + 6d, now);
                    break;
                case EGimmick.ColorWard:
                    _ward.Succeeded(castId);
                    break;
                case EGimmick.FoxfireSeal:
                    _seal.Succeeded(castId, now, now + 6d, now);
                    break;
                case EGimmick.FinalArea:
                    _final.Succeeded(castId, now, now);
                    break;
            }
        }

        private static WolbaekTalismanStart TalismanStart(int castId, double now)
        {
            var placements = new WolbaekTalismanStructure[]
            {
                new WolbaekTalismanStructure(0, EWolbaekTalismanColor.Red, 0, new Vector3(11f, 0f, 1f), 2.2f, 21),
                new WolbaekTalismanStructure(1, EWolbaekTalismanColor.Blue, 1, new Vector3(1f, 0f, 13f), 2.5f, 22),
                new WolbaekTalismanStructure(2, EWolbaekTalismanColor.Yellow, 2, new Vector3(-12f, 0f, 1f), 2.8f, 23),
                new WolbaekTalismanStructure(3, EWolbaekTalismanColor.Green, 3, new Vector3(1f, 0f, -14f), 3.1f, 24)
            };
            return new WolbaekTalismanStart(1, castId, now, now + 3d, 5f, Vector3.zero, placements);
        }

        private static WolbaekWardStart WardStart(int castId, double now)
        {
            var members = new[]
            {
                new WolbaekWardMember(11, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(12, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(13, EWolbaekWardColor.Blue, 1),
                new WolbaekWardMember(14, EWolbaekWardColor.Blue, 1)
            };
            return new WolbaekWardStart(1, castId, now, new Vector3(8f, 0f, -4f), 5f, 8f, 3f, 3f, 4f, members);
        }

        private static GameObject FindShield(bool fading)
        {
            GameObject found = null;
            foreach (Transform item in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if ((item.name == SHIELD_NAME) && (item.gameObject.activeInHierarchy)
                    && ((item.GetComponent<WolbaekShieldFade>() != null) == fading))
                {
                    Assert.IsNull(found, "같은 상태의 보호막은 하나만 있어야 한다");
                    found = item.gameObject;
                }
            }
            return found;
        }

        private static int CountActiveShields()
        {
            int count = 0;
            foreach (Transform item in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if ((item.name == SHIELD_NAME) && (item.gameObject.activeInHierarchy))
                {
                    ++count;
                }
            }
            return count;
        }

        private static void AssertNotEmitting(GameObject shield)
        {
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.IsFalse(system.isEmitting, system.name);
            }
        }

        private static int MaxAlpha(GameObject shield)
        {
            int max = 0;
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                var buffer = new ParticleSystem.Particle[system.particleCount];
                int count = system.GetParticles(buffer);
                for (int i = 0; i < count; ++i)
                {
                    max = Mathf.Max(max, buffer[i].startColor.a);
                }
            }
            return max;
        }
    }
}
