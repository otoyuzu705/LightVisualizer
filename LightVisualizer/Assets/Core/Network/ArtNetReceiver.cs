using System;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Core.Network
{
    public class ArtNetReceiver : MonoBehaviour
    {
        [Header("Network Settings")]
        [SerializeField] private int port = 6454;

        public DmxBuffer DmxBuffer { get; private set; }

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private volatile bool _isRunning = false;

        // Art-Net ヘッダー: "Art-Net\0"
        private static readonly byte[] ArtNetHeader =
            { 0x41, 0x72, 0x74, 0x2D, 0x4E, 0x65, 0x74, 0x00 };

        // スレッド終了を待つ最大時間
        private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(2);

        private void Awake()
        {
            DmxBuffer = new DmxBuffer();
        }

        private void OnEnable()
        {
            StartReceive();
        }

        private void OnDisable()
        {
            StopReceive();
        }

        private void StartReceive()
        {
            if (_isRunning) return;

            try
            {
                _udpClient = new UdpClient(port);
                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "ArtNetReceiverThread"
                };
                _receiveThread.Start();
                Debug.Log($"[ArtNetReceiver] Started listening on port {port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtNetReceiver] Failed to start: {e.Message}");
            }
        }

        private void StopReceive()
        {
            _isRunning = false;

            // Close() で Receive() のブロックを解除し SocketException を発生させる
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                // タイムアウト付き Join でエディタの無限待機を防ぐ
                if (!_receiveThread.Join(ThreadJoinTimeout))
                {
                    Debug.LogWarning("[ArtNetReceiver] Receive thread did not stop within timeout");
                }
                _receiveThread = null;
            }

            Debug.Log("[ArtNetReceiver] Stopped");
        }

        private void ReceiveLoop()
        {
            var endPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref endPoint);
                    ProcessPacket(data);
                }
                catch (SocketException)
                {
                    // Close() による正常終了、またはネットワークエラー
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // UdpClient が Dispose 済みの場合（Close 直後のレース）
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ArtNetReceiver] Unexpected error: {e.Message}");
                }
            }
        }

        private void ProcessPacket(byte[] data)
        {
            if (data == null) return;

            // 最低限のパケット長チェック（Art-Net ヘッダー 8 + OpCode 2 + ProtVer 2 + Sequence 1
            //  + Physical 1 + SubUni 1 + Net 1 + Length 2 = 18 バイト + DMXデータ）
            if (data.Length < 18) return;

            // Art-Net ヘッダー検証
            for (int i = 0; i < ArtNetHeader.Length; i++)
            {
                if (data[i] != ArtNetHeader[i]) return;
            }

            // OpCode: ArtDMX = 0x5000 (リトルエンディアン)
            ushort opCode = (ushort)(data[8] | (data[9] << 8));
            if (opCode != 0x5000) return;

            // Universe: SubUni(data[14]) + Net(data[15]) → Art-Net 4 の15bitユニバース
            int universe = data[14] | (data[15] << 8);

            // Length: ビッグエンディアン、必ず偶数、最大512
            int length = (data[16] << 8) | data[17];
            if (length <= 0 || length > 512) return;

            if (data.Length < 18 + length) return;

            var dmxData = new byte[length];
            Buffer.BlockCopy(data, 18, dmxData, 0, length);

            DmxBuffer.UpdateUniverse(universe, dmxData, length);
        }
    }
}