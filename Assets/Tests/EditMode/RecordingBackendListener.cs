// System
using System.Collections.Generic;
using System.Threading;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> 게이트웨이 결과와 받은 스레드를 기록하는 시험용 처리기 </summary>
    public sealed class RecordingBackendListener : IBackendResponseListener
    {
        /****************************************
        *              Properties
        ****************************************/

        public List<BackendResponse> Responses { get; } = new List<BackendResponse>();
        public List<int>             ThreadIds { get; } = new List<int>();

        public BackendResponse Last => Responses[Responses.Count - 1];

        /****************************************
        *                Methods
        ****************************************/

        public void OnBackendResponse(in BackendResponse response)
        {
            Responses.Add(response);
            ThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
        }
    }
}
