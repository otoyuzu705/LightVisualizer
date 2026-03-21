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
                _isRunning = false;
                Debug.LogError($"[ArtNetReceiver] Failed to start: {e.Message}");
            }
        }

        private void StopReceive()
        {
            _isRunning = false;

            // ReceiveLoop 内で使用中の UdpClient をローカル変数に退避してから null にする。
            // こうすることで ReceiveLoop 側は Close() 後に NullReferenceException ではなく
            // SocketException / ObjectDisposedException を受け取り、安全にループを抜けられる。
            UdpClient clientToClose = _udpClient;
            _udpClient = null;
            clientToClose?.Close();

            if (_receiveThread != null)
            {
                bool joined = _receiveThread.Join(ThreadJoinTimeout);
                if (!joined && _receiveThread.IsAlive)
                {
                    // スレッドがまだ生きている場合は参照を保持したまま警告だけ出す。
                    // null にすると後から Join できなくなり、複数スレッドが起動するリスクが生まれる。
                    Debug.LogWarning("[ArtNetReceiver] Receive thread did not stop within timeout");
                }
                else
                {
                    _receiveThread = null;
                }
            }

            Debug.Log("[ArtNetReceiver] Stopped");
        }

        private void ReceiveLoop()
        {
            // ループ開始時点の UdpClient をローカルにキャプチャする。
            // StopReceive() がフィールドを null にした後も、このローカル参照経由で
            // Receive() が SocketException を返すまで安全にループを継続できる。
            UdpClient localClient = _udpClient;
            var endPoint = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                while (_isRunning)
                {
                    try
                    {
                        byte[] data = localClient.Receive(ref endPoint);
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
            finally
            {
                // 異常終了（過渡的なソケットエラー等）でループを抜けた場合も
                // _isRunning を false にして StartReceive() が再起動できる状態にする。
                _isRunning = false;
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

            // Length: ビッグエンディアン、仕様上は必ず偶数、最大512
            int length = (data[16] << 8) | data[17];
            if (length <= 0 || length > 512 || (length & 1) != 0) return;

            if (data.Length < 18 + length) return;

            var dmxData = new byte[length];
            Buffer.BlockCopy(data, 18, dmxData, 0, length);

            DmxBuffer.UpdateUniverse(universe, dmxData, length);
        }
    }
}