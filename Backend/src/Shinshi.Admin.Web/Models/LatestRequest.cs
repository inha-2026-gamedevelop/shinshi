// System
using System;
using System.Threading.Tasks;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 가장 나중에 시작한 요청의 응답만 적용한다. 대상이 바뀐 뒤 늦게 도착한 이전 응답은 폐기한다 </summary>
    public sealed class LatestRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        private int _generation;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중이거나 마지막으로 시작한 요청이 아직 끝나지 않았는지 </summary>
        public bool Pending { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 요청을 시작하면 이전 요청의 응답은 모두 무효가 된다 </summary>
        public int Begin()
        {
            Pending = true;
            return ++_generation;
        }

        /// <summary> 진행 중인 요청을 모두 무효화한다(입력 변경·닫기) </summary>
        public void Cancel()
        {
            Pending = false;
            ++_generation;
        }

        public bool IsCurrent(int generation)
        {
            return generation == _generation;
        }

        /// <summary> 요청을 실행하고 최신일 때만 apply를 호출한다. 적용했으면 true </summary>
        public async Task<bool> RunAsync<T>(Func<Task<T>> fetch, Action<T> apply)
        {
            int generation = Begin();
            T value = await fetch();
            if (!IsCurrent(generation))
            {
                return false;
            }
            Pending = false;
            apply(value);
            return true;
        }
    }
}
