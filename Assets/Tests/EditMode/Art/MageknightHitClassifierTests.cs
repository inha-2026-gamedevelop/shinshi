// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class MageknightHitClassifierTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  WAVE_SECONDS  = 0.7f;
        private const float  BLINK_SECONDS = 0.45f;
        private const float  MARGIN        = 0.15f;
        private const double WAVE_RESOLVE  = 10.0;
        private const double BLINK_RESOLVE = 20.0;
        private const int    WAVE_CAST     = 1;
        private const int    BLINK_CAST    = 2;

        /****************************************
        *                Methods
        ****************************************/

        private static MageknightHitClassifier CreateConfigured()
        {
            var classifier = new MageknightHitClassifier();
            classifier.Configure(WAVE_SECONDS, BLINK_SECONDS, MARGIN);
            return classifier;
        }

        [Test]
        public void Classify_WithoutCasts_IsOther()
        {
            Assert.AreEqual(MageknightHitClassifier.EKind.Other, CreateConfigured().Classify(WAVE_RESOLVE));
        }

        [Test]
        public void Classify_DuringWaveFlight_IsWave()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE);

            Assert.AreEqual(MageknightHitClassifier.EKind.Wave, classifier.Classify(WAVE_RESOLVE + 0.3));
            Assert.AreEqual(MageknightHitClassifier.EKind.Wave, classifier.Classify(WAVE_RESOLVE + WAVE_SECONDS + (MARGIN * 0.5f)));
        }

        [Test]
        public void Classify_OutsideWaveWindow_IsOther()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE);

            Assert.AreEqual(MageknightHitClassifier.EKind.Other, classifier.Classify(WAVE_RESOLVE - 1.0));
            Assert.AreEqual(MageknightHitClassifier.EKind.Other, classifier.Classify(WAVE_RESOLVE + WAVE_SECONDS + 1.0));
        }

        [Test]
        public void Classify_AtBlinkEnd_IsBlink()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(false, true, BLINK_CAST, BLINK_RESOLVE);

            Assert.AreEqual(MageknightHitClassifier.EKind.Blink, classifier.Classify(BLINK_RESOLVE + BLINK_SECONDS));
        }

        [Test]
        public void Classify_OverlappingWindows_PrefersLaterCast()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE);
            classifier.ObserveCast(false, true, BLINK_CAST, WAVE_RESOLVE + 0.3);

            Assert.AreEqual(MageknightHitClassifier.EKind.Blink, classifier.Classify(WAVE_RESOLVE + 0.5));
        }

        [Test]
        public void ObserveCast_SameCastTwice_KeepsFirstResolveTime()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE);
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE + 5.0);

            Assert.AreEqual(MageknightHitClassifier.EKind.Wave, classifier.Classify(WAVE_RESOLVE + 0.3));
            Assert.AreEqual(MageknightHitClassifier.EKind.Other, classifier.Classify(WAVE_RESOLVE + 5.3));
        }

        [Test]
        public void Reset_ForgetsCasts()
        {
            MageknightHitClassifier classifier = CreateConfigured();
            classifier.ObserveCast(true, false, WAVE_CAST, WAVE_RESOLVE);

            classifier.Reset();

            Assert.AreEqual(MageknightHitClassifier.EKind.Other, classifier.Classify(WAVE_RESOLVE + 0.3));
        }
    }
}
