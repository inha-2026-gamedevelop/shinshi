// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 미니맵 뷰가 지도 그림을 밀고 표시를 창 안으로 모으는지, 코드로 만든 최소 계층으로 확인한다 </summary>
    public sealed class MinimapViewTests
    {
        private const float TOLERANCE = 0.001f;

        private GameObject    _root;
        private MinimapView   _view;
        private RectTransform _map;
        private Image[]       _markers;
        private MinimapMapSO  _data;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MinimapViewTest", typeof(RectTransform));
            var viewport = NewRect("Viewport", _root.transform, new Vector2(250f, 250f));
            _map = NewRect("Map", viewport, new Vector2(10f, 10f));
            _markers = new Image[2];
            for (int i = 0; i < _markers.Length; ++i)
            {
                var marker = NewRect("Marker" + i, _root.transform, new Vector2(8f, 8f));
                _markers[i] = marker.gameObject.AddComponent<Image>();
            }

            _view = _root.AddComponent<MinimapView>();
            var serialized = new SerializedObject(_view);
            serialized.FindProperty("_viewport").objectReferenceValue = viewport;
            serialized.FindProperty("_map").objectReferenceValue      = _map;
            var markerArray = serialized.FindProperty("_markers");
            markerArray.arraySize = _markers.Length;
            for (int i = 0; i < _markers.Length; ++i)
            {
                markerArray.GetArrayElementAtIndex(i).objectReferenceValue = _markers[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            _data = ScriptableObject.CreateInstance<MinimapMapSO>();
            var data = new SerializedObject(_data);
            data.FindProperty("_worldCenter").vector2Value      = new Vector2(10f, 20f);
            data.FindProperty("_worldHalfExtent").floatValue    = 60f;
            data.FindProperty("_viewRadiusMeters").floatValue   = 50f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_data);
        }

        [Test]
        public void SetCenter_AtMapCenter_SizesMapAndKeepsItCentered()
        {
            _view.SetMap(_data);

            _view.SetCenter(new Vector2(10f, 20f));

            Assert.AreEqual(300f, _map.sizeDelta.x, TOLERANCE);
            Assert.AreEqual(300f, _map.sizeDelta.y, TOLERANCE);
            Assert.AreEqual(Vector2.zero, _map.anchoredPosition);
        }

        [Test]
        public void SetCenter_ViewMovesEast_MapSlidesWest()
        {
            _view.SetMap(_data);

            _view.SetCenter(new Vector2(20f, 20f));

            Assert.AreEqual(-25f, _map.anchoredPosition.x, TOLERANCE);
            Assert.AreEqual(0f, _map.anchoredPosition.y, TOLERANCE);
        }

        [Test]
        public void SetMarker_InsideWindow_PlacesRelativeToViewCenter()
        {
            _view.SetMap(_data);
            _view.SetCenter(new Vector2(20f, 20f));

            bool placed = _view.SetMarker(0, new Vector2(30f, 30f), EMinimapMarker.Party);

            Assert.IsTrue(placed);
            Assert.IsTrue(_markers[0].gameObject.activeSelf);
            Assert.AreEqual(25f, _markers[0].rectTransform.anchoredPosition.x, TOLERANCE);
            Assert.AreEqual(25f, _markers[0].rectTransform.anchoredPosition.y, TOLERANCE);
        }

        [Test]
        public void SetMarker_FarOutsideWindow_PullsToEdgeInset()
        {
            _view.SetMap(_data);
            _view.SetCenter(new Vector2(10f, 20f));

            _view.SetMarker(0, new Vector2(500f, 20f), EMinimapMarker.Boss);

            Assert.AreEqual(115f, _markers[0].rectTransform.anchoredPosition.x, TOLERANCE);
            Assert.AreEqual(0f, _markers[0].rectTransform.anchoredPosition.y, TOLERANCE);
        }

        [Test]
        public void SetMarker_IndexBeyondCapacity_ReturnsFalse()
        {
            _view.SetMap(_data);
            _view.SetCenter(new Vector2(10f, 20f));

            Assert.IsFalse(_view.SetMarker(2, Vector2.zero, EMinimapMarker.Party));
            Assert.IsFalse(_view.SetMarker(-1, Vector2.zero, EMinimapMarker.Party));
        }

        [Test]
        public void SetMarker_BeforeFirstCenter_ReturnsFalse()
        {
            _view.SetMap(_data);

            Assert.IsFalse(_view.SetMarker(0, Vector2.zero, EMinimapMarker.Party));
        }

        [Test]
        public void HideMarkersFrom_HidesOnlyLaterMarkers()
        {
            _view.SetMap(_data);
            _view.SetCenter(new Vector2(10f, 20f));
            _view.SetMarker(0, new Vector2(10f, 20f), EMinimapMarker.Party);
            _view.SetMarker(1, new Vector2(12f, 20f), EMinimapMarker.Npc);

            _view.HideMarkersFrom(1);

            Assert.IsTrue(_markers[0].gameObject.activeSelf);
            Assert.IsFalse(_markers[1].gameObject.activeSelf);
        }

        [Test]
        public void SetMap_Null_HidesMarkersAndIgnoresCenter()
        {
            _view.SetMap(_data);
            _view.SetCenter(new Vector2(10f, 20f));
            _view.SetMarker(0, new Vector2(10f, 20f), EMinimapMarker.Party);

            _view.SetMap(null);
            _view.SetCenter(new Vector2(99f, 99f));

            Assert.IsFalse(_markers[0].gameObject.activeSelf);
            Assert.AreEqual(Vector2.zero, _map.anchoredPosition);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            return rect;
        }
    }
}
