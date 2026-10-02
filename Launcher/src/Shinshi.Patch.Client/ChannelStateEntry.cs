// System
using System.Runtime.Serialization;

namespace Shinshi.Patch.Client
{
    /// <summary> channels.json의 항목 하나 </summary>
    [DataContract]
    internal sealed class ChannelStateEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "product")]  private string? _product;
        [DataMember(Name = "channel")]  private string? _channel;
        [DataMember(Name = "sequence")] private long    _sequence;

        /****************************************
        *              Properties
        ****************************************/

        public string Product  => _product ?? string.Empty;
        public string Channel  => _channel ?? string.Empty;
        public long   Sequence { get => _sequence; set => _sequence = value; }

        /****************************************
        *                Methods
        ****************************************/

        public ChannelStateEntry(string product, string channel, long sequence)
        {
            _product  = product;
            _channel  = channel;
            _sequence = sequence;
        }

        private ChannelStateEntry() { }
    }
}
