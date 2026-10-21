// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 무철 방향 봉인 표식의 타수별 연출을 시각을 직접 주입해 검증한다. 서버 어셈블리를 참조하지 않고 방향과 타수만 넣는다.
    // 화면에 나온 모양은 확인하지 않고, 표식에 준 셰이더 값(진행도, 반응, 투명도)과 위치, 크기를 확인한다
    public class MucheolSealViewTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START     = 100.0;
        private const float  TOLERANCE = 0.001f;
        private const int    REQUIRED  = 5;
        private const float  SIZE      = 1.4f;

        private const string CONTAINER_NAME = "MucheolSealMarkers";

        private static readonly Vector3[] Offsets =
        {
            new Vector3(0.1f, 0.2f, 0.3f), new Vector3(-0.1f, 0.2f, 0.3f), new Vector3(0f, 0.1f, -0.2f)
        };

        private static readonly Vector3[] Angles =
        {
            new Vector3(0f, 30f, 0f), new Vector3(0f, -30f, 0f), new Vector3(0f, 180f, 0f)
        };

        /****************************************
        *                Fields
        ****************************************/

        private GameObject    _root;
        private GameObject[]  _bones;
        private Material      _material;
        private MucheolSealView _view;
        private double        _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _clock = START;
            _root  = new GameObject("MucheolSealViewTests");

            // 뼈는 배율이 1이 아니어도 표식 크기에 영향을 주면 안 된다
            _bones = new GameObject[3];
            for (int i = 0; i < _bones.Length; ++i)
            {
                _bones[i] = new GameObject("Bone" + i);
                _bones[i].transform.SetParent(_root.transform, false);
                _bones[i].transform.position   = new Vector3(i * 2f, 1f, 0f);
                _bones[i].transform.localScale = Vector3.one * 3f;
            }

            _material = new Material(Shader.Find("Sprites/Default"));
            _view     = _root.AddComponent<MucheolSealView>();
            SetField("_anchors", new[] { _bones[0].transform, _bones[1].transform, _bones[2].transform });
            SetField("_offsets", Offsets);
            SetField("_localAngles", Angles);
            SetField("_material", _material);
            SetField("_size", SIZE);

            ViewTestUtil.Awake(_view);
            _view.SetServerClock(() => _clock);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            GameObject container = FindContainer();
            if (container != null)
            {
                Object.DestroyImmediate(container);
            }

            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void SetField(string name, object value)
        {
            typeof(MucheolSealView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_view, value);
        }

        private void Advance(double seconds)
        {
            _clock += seconds;
            _view.Refresh();
        }

        private static GameObject FindContainer()
        {
            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name == CONTAINER_NAME)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static Transform FindMarker(EMucheolSealSide side)
        {
            return FindContainer().transform.Find(side + " Seal");
        }

        private static float ReadFloat(EMucheolSealSide side, string property)
        {
            var block = new MaterialPropertyBlock();
            FindMarker(side).GetComponent<Renderer>().GetPropertyBlock(block);
            return block.GetFloat(property);
        }

        /****************************************
        *               Visual
        ****************************************/

        [TestCase(0, 5, 0f)]
        [TestCase(1, 5, 0.2f)]
        [TestCase(3, 5, 0.6f)]
        [TestCase(5, 5, 1f)]
        [TestCase(5, 10, 0.5f)]
        [TestCase(10, 10, 1f)]
        public void Visual_Progress_FollowsHitsOverRequired(int hits, int required, float expected)
        {
            MucheolSealLook look = MucheolSealVisual.Evaluate(1f, hits, required, 1f, MucheolSealVisual.NOT_DISSOLVING);

            Assert.AreEqual(expected, look.Progress, TOLERANCE);
        }

        [Test]
        public void Visual_Pulse_StartsAtOneAndFadesWithinPulseSeconds()
        {
            Assert.AreEqual(1f, MucheolSealVisual.Evaluate(1f, 1, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).Pulse, TOLERANCE);
            Assert.AreEqual(0.5f, MucheolSealVisual.Evaluate(1f, 1, REQUIRED, 0.1f, MucheolSealVisual.NOT_DISSOLVING).Pulse, TOLERANCE);
            Assert.AreEqual(0f, MucheolSealVisual.Evaluate(1f, 1, REQUIRED, 0.2f, MucheolSealVisual.NOT_DISSOLVING).Pulse, TOLERANCE);
        }

        [Test]
        public void Visual_NoHitYet_HasNoPulse()
        {
            Assert.AreEqual(0f, MucheolSealVisual.Evaluate(1f, 0, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).Pulse, TOLERANCE);
        }

        [Test]
        public void Visual_Opacity_FadesInThenFadesOutWhileDissolving()
        {
            Assert.AreEqual(0f, MucheolSealVisual.Evaluate(0f, 0, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).Opacity, TOLERANCE);
            Assert.AreEqual(0.5f, MucheolSealVisual.Evaluate(0.1f, 0, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).Opacity, TOLERANCE);
            Assert.AreEqual(1f, MucheolSealVisual.Evaluate(1f, 0, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).Opacity, TOLERANCE);
            Assert.AreEqual(0.5f, MucheolSealVisual.Evaluate(5f, REQUIRED, REQUIRED, 5f, 0.2f).Opacity, TOLERANCE);
            Assert.AreEqual(0f, MucheolSealVisual.Evaluate(5f, REQUIRED, REQUIRED, 5f, 0.4f).Opacity, TOLERANCE);
        }

        [Test]
        public void Visual_Scale_GrowsOnPulseAndSpreadsWhileDissolving()
        {
            Assert.AreEqual(1f, MucheolSealVisual.Evaluate(1f, 0, REQUIRED, 1f, MucheolSealVisual.NOT_DISSOLVING).ScaleFactor, TOLERANCE);
            Assert.AreEqual(1.12f, MucheolSealVisual.Evaluate(1f, 1, REQUIRED, 0f, MucheolSealVisual.NOT_DISSOLVING).ScaleFactor, TOLERANCE);
            Assert.AreEqual(1.7f, MucheolSealVisual.Evaluate(5f, REQUIRED, REQUIRED, 5f, 0.4f).ScaleFactor, TOLERANCE);
        }

        /****************************************
        *            Reveal And Hits
        ****************************************/

        [Test]
        public void Revealed_ShowsOnlyTheRightMarkerWithNoProgress()
        {
            _view.OnSealRevealed(REQUIRED);
            _view.Refresh();

            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Rear));
            Assert.AreEqual(0f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
            Assert.AreEqual(0f, ReadFloat(EMucheolSealSide.Right, "_Opacity"), TOLERANCE);
        }

        [Test]
        public void Revealed_FadesInOverRevealSeconds()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(MucheolSealVisual.REVEAL_FADE_SECONDS);

            Assert.AreEqual(1f, ReadFloat(EMucheolSealSide.Right, "_Opacity"), TOLERANCE);
        }

        [Test]
        public void HitCounted_RaisesProgressAndPulsesThenSettles()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);

            _view.OnSealHitCounted(EMucheolSealSide.Right, 3, REQUIRED);
            _view.Refresh();
            Assert.AreEqual(0.6f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
            Assert.AreEqual(1f, ReadFloat(EMucheolSealSide.Right, "_Pulse"), TOLERANCE);
            Assert.AreEqual(SIZE * 1.12f, FindMarker(EMucheolSealSide.Right).localScale.x, TOLERANCE);

            Advance(MucheolSealVisual.PULSE_SECONDS);
            Assert.AreEqual(0.6f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
            Assert.AreEqual(0f, ReadFloat(EMucheolSealSide.Right, "_Pulse"), TOLERANCE);
            Assert.AreEqual(SIZE, FindMarker(EMucheolSealSide.Right).localScale.x, TOLERANCE);
        }

        [Test]
        public void HitCounted_EachHitPulsesAgain()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            _view.OnSealHitCounted(EMucheolSealSide.Right, 1, REQUIRED);
            Advance(0.5);
            Assert.AreEqual(0f, ReadFloat(EMucheolSealSide.Right, "_Pulse"), TOLERANCE);

            _view.OnSealHitCounted(EMucheolSealSide.Right, 2, REQUIRED);
            _view.Refresh();

            Assert.AreEqual(1f, ReadFloat(EMucheolSealSide.Right, "_Pulse"), TOLERANCE);
            Assert.AreEqual(0.4f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
        }

        [Test]
        public void HitCounted_PartyRequirementScalesProgress()
        {
            _view.OnSealRevealed(10);
            Advance(1.0);

            _view.OnSealHitCounted(EMucheolSealSide.Right, 5, 10);
            _view.Refresh();

            Assert.AreEqual(0.5f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
        }

        [Test]
        public void HitCounted_WhenMarkerNotShown_ShowsItWithCurrentProgress()
        {
            _view.OnSealHitCounted(EMucheolSealSide.Left, 2, REQUIRED);
            _view.Refresh();

            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.AreEqual(0.4f, _view.GetProgress(EMucheolSealSide.Left), TOLERANCE);
        }

        /****************************************
        *        Direction Cleared And Broken
        ****************************************/

        [Test]
        public void DirectionCleared_DissolvesThatMarkerAndRevealsTheNextOne()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            _view.OnSealHitCounted(EMucheolSealSide.Right, 4, REQUIRED);

            _view.OnSealDirectionCleared(EMucheolSealSide.Right, REQUIRED);
            Advance(MucheolSealVisual.DISSOLVE_SECONDS * 0.5f);

            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsTrue(_view.IsMarkerDissolving(EMucheolSealSide.Right));
            Assert.AreEqual(1f, ReadFloat(EMucheolSealSide.Right, "_Progress"), TOLERANCE);
            Assert.AreEqual(0.5f, ReadFloat(EMucheolSealSide.Right, "_Opacity"), TOLERANCE);
            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.IsFalse(_view.IsMarkerDissolving(EMucheolSealSide.Left));
            Assert.AreEqual(0f, _view.GetProgress(EMucheolSealSide.Left), TOLERANCE);
        }

        [Test]
        public void DirectionCleared_RemovesTheDissolvedMarkerAfterDissolveSeconds()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            _view.OnSealDirectionCleared(EMucheolSealSide.Right, REQUIRED);

            Advance(MucheolSealVisual.DISSOLVE_SECONDS);

            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Left));
        }

        [Test]
        public void FullSequence_RightLeftRearThenBroken_LeavesNoMarker()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);

            _view.OnSealDirectionCleared(EMucheolSealSide.Right, REQUIRED);
            Advance(1.0);
            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Rear));

            _view.OnSealDirectionCleared(EMucheolSealSide.Left, REQUIRED);
            Advance(1.0);
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Rear));

            _view.OnSealBroken(REQUIRED);
            Advance(MucheolSealVisual.DISSOLVE_SECONDS);
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Rear));
        }

        [Test]
        public void Broken_DoesNotRevealAnotherMarker()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            _view.OnSealDirectionCleared(EMucheolSealSide.Right, REQUIRED);
            Advance(1.0);
            _view.OnSealDirectionCleared(EMucheolSealSide.Left, REQUIRED);
            Advance(1.0);

            _view.OnSealBroken(REQUIRED);

            Assert.IsTrue(_view.IsMarkerDissolving(EMucheolSealSide.Rear));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Left));
        }

        /****************************************
        *           Stop, Restore, Follow
        ****************************************/

        [Test]
        public void Stopped_HidesEveryMarkerAtOnce()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            _view.OnSealDirectionCleared(EMucheolSealSide.Right, REQUIRED);

            _view.OnSealStopped();

            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Rear));
        }

        [Test]
        public void Restored_ShowsCurrentDirectionAtFullOpacityWithoutPulse()
        {
            _view.OnSealRestored(EMucheolSealSide.Left, 2, REQUIRED);
            _view.Refresh();

            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Left));
            Assert.AreEqual(0.4f, ReadFloat(EMucheolSealSide.Left, "_Progress"), TOLERANCE);
            Assert.AreEqual(1f, ReadFloat(EMucheolSealSide.Left, "_Opacity"), TOLERANCE);
            Assert.AreEqual(0f, ReadFloat(EMucheolSealSide.Left, "_Pulse"), TOLERANCE);
        }

        [Test]
        public void Revealed_AgainClearsPreviousMarkers()
        {
            _view.OnSealRestored(EMucheolSealSide.Rear, 3, REQUIRED);

            _view.OnSealRevealed(REQUIRED);

            Assert.IsTrue(_view.IsMarkerVisible(EMucheolSealSide.Right));
            Assert.IsFalse(_view.IsMarkerVisible(EMucheolSealSide.Rear));
        }

        [Test]
        public void Marker_FollowsItsBoneButIgnoresBoneScale()
        {
            _view.OnSealRevealed(REQUIRED);
            Advance(1.0);
            Vector3 expected = _bones[0].transform.TransformPoint(Offsets[0]);
            Assert.AreEqual(0f, Vector3.Distance(expected, FindMarker(EMucheolSealSide.Right).position), TOLERANCE);

            _bones[0].transform.position += new Vector3(2f, 0f, 1f);
            Advance(0.1);

            expected = _bones[0].transform.TransformPoint(Offsets[0]);
            Assert.AreEqual(0f, Vector3.Distance(expected, FindMarker(EMucheolSealSide.Right).position), TOLERANCE);
            Assert.AreEqual(SIZE, FindMarker(EMucheolSealSide.Right).lossyScale.x, TOLERANCE);
        }

        [Test]
        public void Marker_RotatesWithBoneAndLocalAngle()
        {
            _view.OnSealRevealed(REQUIRED);
            _bones[0].transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Advance(1.0);

            Quaternion expected = _bones[0].transform.rotation * Quaternion.Euler(Angles[0]);

            Assert.Less(Quaternion.Angle(expected, FindMarker(EMucheolSealSide.Right).rotation), 0.01f);
        }
    }
}
