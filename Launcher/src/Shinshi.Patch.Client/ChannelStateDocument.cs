// System
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Patch.Client
{
    /// <summary> state/channels.json, 제품과 채널별로 마지막으로 받아들인 채널 포인터 sequence </summary>
    [DataContract]
    internal sealed class ChannelStateDocument
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "formatVersion")] private int                      _formatVersion;
        [DataMember(Name = "channels")]      private List<ChannelStateEntry>? _channels;

        /****************************************
        *              Properties
        ****************************************/

        public int FormatVersion => _formatVersion;

        /****************************************
        *                Methods
        ****************************************/

        public ChannelStateDocument()
        {
            _formatVersion = PatchFormat.FORMAT_VERSION;
            _channels      = new List<ChannelStateEntry>();
        }

        public long GetSequence(string product, string channel)
        {
            ChannelStateEntry? entry = Find(product, channel);
            return (entry == null) ? 0 : entry.Sequence;
        }

        // 되돌리기 방지를 위해 저장값은 줄지 않는다
        public void RaiseSequence(string product, string channel, long sequence)
        {
            ChannelStateEntry? entry = Find(product, channel);
            if (entry == null)
            {
                _channels ??= new List<ChannelStateEntry>();
                _channels.Add(new ChannelStateEntry(product, channel, sequence));
                return;
            }

            entry.Sequence = Math.Max(entry.Sequence, sequence);
        }

        private ChannelStateEntry? Find(string product, string channel)
        {
            if (_channels == null)
            {
                return null;
            }

            foreach (ChannelStateEntry? entry in _channels)
            {
                if ((entry != null) && (entry.Product == product) && (entry.Channel == channel))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
