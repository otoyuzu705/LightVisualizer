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
        [SerializeField] private int port = 6454; // Art-Netのデフォルトポート
        
        public DmxBuffer DmxBuffer { get; private set; }
        
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private bool _isRunning = false;
        
        // Art-Netヘッダ
        private readonly byte[] _artNetHeader = new byte[] { 0x41, 0x72, 0x74, 0x2D, 0x4E, 0x65, 0x74, 0x00 };

        private void Awake()
        {
            this.DmxBuffer = new DmxBuffer();
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
                Debug.Log($"[Art-Net receiver] started listening on port {port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start Art-Net receiver: {e.Message}");
            }
        }
        
        private void StopReceive()
        {
            _isRunning = false;
            
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join();
            }
            Debug.Log("[Art-Net receiver] stopped");
        }

        private void ReceiveLoop()
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning)
            {
                try
                {
                    // データ受信
                    byte[] data = _udpClient.Receive(ref endPoint);
                    ProcessPacket(data);
                }
                catch (SocketException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ArtNetReceiver] Error receiving data: {e.Message}");
                }
            }
        }

        private void ProcessPacket(byte[] data)
        {
            if (data == null) return;
            
            // ヘッダーの検証
            for (int i = 0; i < 8; i++)
            {
                if (data[i] != _artNetHeader[i]) return; 
            }
            
            // OpCodeの検証
            ushort opCode = BitConverter.ToUInt16(data, 8);
            if (opCode != 0x5000) return; // ArtDMXパケットのみ処理
            
            // ユニバースの計算
            int universe = data[14] | (data[15] << 8);
            
            int length = (data[16] << 8) | data[17];
            if (length > 512) return; 
            
            // DMXデータの更新
            byte[] dmxData = new byte[length];
            Buffer.BlockCopy(data, 18, dmxData, 0, length);
            
            DmxBuffer.UpdateUniverse(universe, dmxData, length);
            
        }
    }    
}

