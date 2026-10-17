// Unity
using UnityEngine;
using UnityEngine.Timeline;

namespace Shinshi.AI
{
    // 시안 트랙(PreviewParticleFadeTrack)과 같은 방식이다. 정식 컷신이 시안 어셈블리를 참조하지 않도록 정식 어셈블리에 따로 둔다
    // 시안은 확정 연출 기록이라 그대로 두고, 고칠 때는 두 곳의 곡선과 덮어쓰기 순서를 같이 맞춘다
    // 같은 Transform 아래 ControlTrack이 만든 VFX 인스턴스를 찾아 정점 알파를 바꾼다
    // ControlTrack의 파티클 시뮬레이션은 PrepareFrame에서 끝나고 이 트랙의 처리는 ProcessFrame에서 하므로, 그 프레임의 파티클에 덮어쓴다
    /// <summary> 컷신 파티클 투명도 트랙. 바인딩은 페이드할 VFX만 담은 부모 Transform이다 </summary>
    [TrackColor(0.45f, 0.75f, 1f)]
    [TrackClipType(typeof(CutsceneParticleFadeClip))]
    [TrackBindingType(typeof(Transform))]
    public sealed class CutsceneParticleFadeTrack : TrackAsset
    {
    }
}
