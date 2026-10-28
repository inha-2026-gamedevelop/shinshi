// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 직업별 버프 발동에 공통으로 쓰는 클립 목록 </summary>
    internal static class BuffSoundClips
    {
        private static AudioClip[] _clips;

        public static AudioClip Pick(System.Random random)
        {
            if (_clips == null)
            {
                _clips = new[]
                {
                    Resources.Load<AudioClip>("Holy_Spell_v2"),
                    Resources.Load<AudioClip>("magic_shine_light_spell_01"),
                    Resources.Load<AudioClip>("magic_shine_light_spell_02"),
                    Resources.Load<AudioClip>("magic_shine_light_spell_03"),
                    Resources.Load<AudioClip>("magic_shine_light_spell_04")
                };
            }

            return _clips[random.Next(_clips.Length)];
        }
    }
}
