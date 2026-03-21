using System;
using UnityEngine;

namespace Core.Network
{
    public class DmxBuffer
    {
        public const int Universes = 32;
        public const int ChannelsPerUniverse = 512;

        // 全ユニバースを1次元配列で保持
        private byte[] _dmxData = new byte[Universes * ChannelsPerUniverse];
        private readonly object _lockObj = new object();

        /// <summary>
        /// 指定したユニバースのデータを更新する（受信スレッドから呼ばれる）
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
        /// 現在の全 DMX データのスナップショットを新規配列で返す。
        /// 毎フレーム呼ぶと GC 負荷になるため、可能な限り CopyUniverseTo() を使うこと。
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

        /// <summary>
        /// 指定ユニバースの DMX データを呼び出し元が用意した配列にコピーする（ノーアロケート）。
        /// dest は ChannelsPerUniverse (512) バイト以上のサイズが必要。
        /// universe は 0始まり（DmxBuffer 内部インデックス）。
        /// </summary>
        public bool CopyUniverseTo(int universe, byte[] dest)
        {
            if (universe < 0 || universe >= Universes) return false;
            if (dest == null || dest.Length < ChannelsPerUniverse) return false;

            int startIndex = universe * ChannelsPerUniverse;
            lock (_lockObj)
            {
                Buffer.BlockCopy(_dmxData, startIndex, dest, 0, ChannelsPerUniverse);
            }
            return true;
        }
    }
}