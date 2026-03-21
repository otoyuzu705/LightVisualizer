using UnityEngine;
using Core.GDTF;

namespace Core.Fixture
{
    /// <summary>
    /// Scene上に配置された灯体1台分を表すコンポーネント。
    /// Base → Yoke → Head の Transform 階層と、
    /// パース済みの GdtfData・選択中の DMX Mode インデックスを保持する。
    /// </summary>
    public class FixtureInstance : MonoBehaviour
    {
        // ---- 灯体メタ情報 ------------------------------------------------

        /// <summary>元の GDTF ファイルパス（Domain Reload 後の再パース用）</summary>
        [SerializeField] private string _gdtfFilePath = "";
        public string GdtfFilePath
        {
            get => _gdtfFilePath;
            set => _gdtfFilePath = value;
        }

        /// <summary>
        /// パース済み GDTF データ。
        /// GdtfData は UnityEngine.Object でないためシリアライズ不可。
        /// Domain Reload 後は OnEnable() で _gdtfFilePath から自動復元する。
        /// </summary>
        public GdtfData GdtfData { get; set; }

        // ---- DMX パッチ情報 -----------------------------------------------

        /// <summary>アサインされた Art-Net ユニバース（1始まり・業界標準）</summary>
        [field: SerializeField]
        public int Universe { get; set; } = 1;

        /// <summary>DMX スタートアドレス（1始まり、GDTF仕様に合わせる）</summary>
        [field: SerializeField]
        public int StartAddress { get; set; } = 1;

        /// <summary>使用する DmxMode のインデックス（GdtfData.DmxModes への添字）</summary>
        [field: SerializeField]
        public int DmxModeIndex { get; set; } = 0;

        // ---- Transform 階層参照 ------------------------------------------
        // Transform は UnityEngine.Object のサブクラスなので SerializeField で永続化可能。
        // Domain Reload や Play→Edit 往復後も Unity がシリアライズデータから復元する。

        /// <summary>灯体本体のルート（床面固定部）</summary>
        [SerializeField] private Transform _baseTransform;
        public Transform BaseTransform
        {
            get => _baseTransform;
            set => _baseTransform = value;
        }

        /// <summary>パン軸（水平回転）</summary>
        [SerializeField] private Transform _yokeTransform;
        public Transform YokeTransform
        {
            get => _yokeTransform;
            set => _yokeTransform = value;
        }

        /// <summary>チルト軸（垂直回転）、ビームはここから出る</summary>
        [SerializeField] private Transform _headTransform;
        public Transform HeadTransform
        {
            get => _headTransform;
            set => _headTransform = value;
        }

        // ---- HDRP ライト参照 ---------------------------------------------

        /// <summary>Head に付属する HDRP スポットライト</summary>
        [SerializeField] private Light _beamLight;
        public Light BeamLight
        {
            get => _beamLight;
            set => _beamLight = value;
        }

        // ---- 物理限界・ビームパラメータ ------------------------------------
        // GDTF の Axis PhysicalFrom/To・Beam LuminousFlux から取得。
        // SerializeField で Inspector 確認・手動オーバーライドを可能にする。

        /// <summary>Pan（水平回転）の最小角度（degrees）</summary>
        [field: SerializeField] public float PanMin  { get; set; } = -270f;

        /// <summary>Pan（水平回転）の最大角度（degrees）</summary>
        [field: SerializeField] public float PanMax  { get; set; } =  270f;

        /// <summary>Tilt（垂直回転）の最小角度（degrees）</summary>
        [field: SerializeField] public float TiltMin { get; set; } = -135f;

        /// <summary>Tilt（垂直回転）の最大角度（degrees）</summary>
        [field: SerializeField] public float TiltMax { get; set; } =  135f;

        /// <summary>ビームの最大光束（lm）。GDTF Beam LuminousFlux から取得</summary>
        [field: SerializeField] public float MaxIntensityLm { get; set; } = 10000f;

        // ---- ユーティリティ -----------------------------------------------

        /// <summary>現在選択中の DmxMode。GdtfData が null の場合は null を返す</summary>
        public DmxMode ActiveDmxMode
        {
            get
            {
                if (GdtfData == null || GdtfData.DmxModes.Count == 0) return null;
                int idx = Mathf.Clamp(DmxModeIndex, 0, GdtfData.DmxModes.Count - 1);
                return GdtfData.DmxModes[idx];
            }
        }

        /// <summary>
        /// このユニバース内の DMX バイト配列（512 要素）から
        /// 指定チャンネルオフセット（1始まり絶対アドレス）の値を取得する。
        /// </summary>
        public byte GetDmxValue(byte[] universeDmx, int offsetOneBased)
        {
            // 実効インデックス = (StartAddress - 1) + (Offset - 1)
            int index = (StartAddress - 1) + (offsetOneBased - 1);
            if (universeDmx == null || index < 0 || index >= universeDmx.Length) return 0;
            return universeDmx[index];
        }

        // ---- Domain Reload 復元 ------------------------------------------

        private void OnEnable()
        {
            // Domain Reload 後に GdtfData が消えている場合、ファイルパスから再パースして復元する
            if (GdtfData == null && !string.IsNullOrEmpty(_gdtfFilePath))
            {
                GdtfData = GdtfParser.ParseGdtf(_gdtfFilePath);
                if (GdtfData == null)
                    Debug.LogWarning(
                        $"[FixtureInstance] Failed to restore GdtfData from: {_gdtfFilePath}");
            }
        }
    }
}