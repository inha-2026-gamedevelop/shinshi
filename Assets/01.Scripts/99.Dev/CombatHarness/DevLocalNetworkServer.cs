// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Dev
{
    /// <summary> 전송 계층 없이 Mirror 서버를 "켜진 상태"로만 만들어 씬 네트워크 개체를 생성하는 시험용 부트스트랩 </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class DevLocalNetworkServer : MonoBehaviour
    {
        // 서버 활성 표식이 켜져야 [Server] 메서드와 NetworkTime(서버 시각)이 동작한다. 연결은 만들지 않는다.
        // 씬의 NetworkIdentity 개체는 Mirror가 처음에 꺼 두므로 SpawnObjects로 켜고 생성한다. 이 컴포넌트가 가장 먼저 깨어나야
        // 같은 개체의 다른 컴포넌트가 서버가 켜진 상태에서 Awake를 맞는다

        /****************************************
        *                Fields
        ****************************************/

        private bool _ownsFlag = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 에디터 창이 포커스를 잃어도 전투 시간이 흐르게 한다
            Application.runInBackground = true;

            if (!NetworkServer.active)
            {
                PropertyInfo property = typeof(NetworkServer).GetProperty(nameof(NetworkServer.active), BindingFlags.Public | BindingFlags.Static);
                property.GetSetMethod(true).Invoke(null, new object[] { true });
                _ownsFlag = true;

                // 서버를 시작하지 않아 프로파일용 측정기가 비어 있다. 네트워크 루프가 매 프레임 읽는다
                FillTimeSample(nameof(NetworkServer.earlyUpdateDuration));
                FillTimeSample(nameof(NetworkServer.lateUpdateDuration));
                FillTimeSample(nameof(NetworkServer.fullUpdateDuration));
            }

        }

        // 꺼져 있던 씬 개체는 모든 Awake가 끝난 뒤에 켜야 네트워크 개체 초기화가 끝난 상태로 생성된다
        private void Start()
        {
            NetworkServer.SpawnObjects();
        }

        private void OnDestroy()
        {
            if (!_ownsFlag)
            {
                return;
            }

            PropertyInfo property = typeof(NetworkServer).GetProperty(nameof(NetworkServer.active), BindingFlags.Public | BindingFlags.Static);
            property.GetSetMethod(true).Invoke(null, new object[] { false });
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void FillTimeSample(string fieldName)
        {
            FieldInfo field = typeof(NetworkServer).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, new TimeSample(NetworkServer.sendRate));
            }
        }
    }
}
