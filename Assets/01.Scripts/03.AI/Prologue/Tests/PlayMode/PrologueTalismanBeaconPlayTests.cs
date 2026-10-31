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
    public class PrologueTalismanBeaconPlayTests
    {
        private static readonly Vector3 G_MarkerPosition = new Vector3(12f, 3f, -8f);

        [UnityTest]
        public IEnumerator Create_SceneMarker_PlacesClientVisualWithoutGameplayColliders()
        {
            var markers = new GameObject("PrologueMarkers");
            var marker = new GameObject("Talisman");
            marker.transform.SetParent(markers.transform, false);
            marker.transform.position = G_MarkerPosition;
            var parent = new GameObject("Presenter");
            PrologueTalismanBeacon beacon = null;
            try
            {
                beacon = PrologueTalismanBeacon.Create(parent.transform);
                Assert.IsNotNull(beacon);
                Assert.AreEqual(G_MarkerPosition, beacon.transform.position);
                Assert.AreEqual(1, beacon.GetComponentsInChildren<ParticleSystem>().Length);
                Assert.AreEqual(1, beacon.GetComponentsInChildren<Light>().Length);
                Assert.AreEqual(0, beacon.GetComponentsInChildren<Collider>().Length, "표현은 서버 조사 판정을 바꾸지 않는다");
                Assert.IsNotNull(beacon.GetComponentInChildren<ParticleSystemRenderer>().sharedMaterial);
                yield return null;
            }
            finally
            {
                Object.Destroy(parent);
                Object.Destroy(markers);
            }
            yield return null;
            Assert.IsTrue(beacon == null, "표현은 프롤로그 presenter와 함께 정리한다");
        }
    }
}
