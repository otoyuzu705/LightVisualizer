using System;
using UnityEngine;

namespace Core.Network
{
    public class DmxBuffer
    {
        public const int Universes = 32;
        public const int ChannelsPerUniverse = 512;
        
        //全ユニバースを1次元配列で保持
        private byte[] _dmxData = new byte[Universes * ChannelsPerUniverse];
        private readonly object _lockObj = new object();

        /// <summary>
        /// 指定したユニバースのデータを更新
        /// </summary>
        public void UpdateUniverse(int universe, byte[] data, int length)
        {
            if (universe < 0 || universe >= Universes) return;
            
            int copyLength = Mathf.Min(length, ChannelsPerUniverse);
            int startIndex = universe * ChannelsPerUniverse;

            lock (_lockObj)
            {
                Buffer.BlockCopy(data, 0, _dmxData, startIndex, copyLength);
            }
        }

        /// <summary>
        /// 現在のDMXデータのスナップショットを取得
        /// </summary>
        public byte[] GetDmxDataSnapshot()
        {
            byte[] snapshot = new byte[_dmxData.Length];
            lock (_lockObj)
            {
                Buffer.BlockCopy(_dmxData, 0, snapshot, 0, _dmxData.Length);
            }
            return snapshot;
        }
    }    
}

