// Unity
using UnityEngine;
using UnityEngine.Timeline;

namespace Shinshi.AI.Preview
{
    // 같은 Transform 아래 ControlTrack이 만든 VFX 인스턴스를 찾아 정점 알파를 바꾼다
    // ControlTrack의 파티클 시뮬레이션은 PrepareFrame에서 끝나고 이 트랙의 처리는 ProcessFrame에서 하므로, 그 프레임의 파티클에 덮어쓴다
    /// <summary> 월백 전환 시안 전용 파티클 투명도 트랙. 바인딩은 페이드할 VFX만 담은 부모 Transform이다 </summary>
    [TrackColor(0.45f, 0.75f, 1f)]
    [TrackClipType(typeof(PreviewParticleFadeClip))]
    [TrackBindingType(typeof(Transform))]
    public sealed class PreviewParticleFadeTrack : TrackAsset
    {
    }
}
