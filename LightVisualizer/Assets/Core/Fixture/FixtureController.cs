using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Core.GDTF;
using Core.Network;

namespace Core.Fixture
{
    /// <summary>
    /// Playモード中に ArtNetReceiver から DMX データを受け取り、
    /// Pan / Tilt / Dimmer / Color / Zoom 等を FixtureInstance の
    /// Transform および HDRP ライトにリアルタイムで反映する。
    /// </summary>
    [RequireComponent(typeof(FixtureInstance))]
    public class FixtureController : MonoBehaviour
    {
        [Header("Beam")]
        [SerializeField] private float zoomMin = 5f;
        [SerializeField] private float zoomMax = 50f;

        // Pan/Tilt 物理限界は GdtfGeometryInfo から Start() 時に設定する（GDTF PhysicalFrom/To）
        // Inspector でオーバーライドしたい場合は SerializeField を付けて使う
        private float _panMin  = -270f;
        private float _panMax  =  270f;
        private float _tiltMin = -135f;
        private float _tiltMax =  135f;
        private float _maxIntensityLm = 10000f;

        private FixtureInstance       _fixture;
        private ArtNetReceiver        _receiver;
        private HDAdditionalLightData _hdLight;
        private bool                  _warnedOnce = false;

        // ---- アロケートキャッシュ ----------------------------------------
        // Update() 毎に new byte[] しないためにフィールドで保持する（④ GC 圧軽減）
        private byte[] _universeDmxCache;

        private void Awake()
        {
            _fixture = GetComponent<FixtureInstance>();
            _universeDmxCache = new byte[DmxBuffer.ChannelsPerUniverse];
        }

        private void Start()
        {
            _receiver = FindAnyObjectByType<ArtNetReceiver>();
            if (_receiver == null)
                Debug.LogWarning("[FixtureController] ArtNetReceiver not found in scene.");
            else
                Debug.Log("[FixtureController] ArtNetReceiver found.");

            if (_fixture.BeamLight != null)
                _hdLight = _fixture.BeamLight.GetComponent<HDAdditionalLightData>();

            // GdtfData の状態をログ出力（null なら ActiveDmxMode も null になりUpdateが止まる）
            if (_fixture.GdtfData == null)
                Debug.LogError(
                    $"[FixtureController] GdtfData is NULL on '{gameObject.name}'. " +
                    "Update() will be skipped. Check GdtfFilePath in FixtureInstance.");
            else
                Debug.Log(
                    $"[FixtureController] GdtfData OK: {_fixture.GdtfData.FixtureName} " +
                    $"/ modes={_fixture.GdtfData.DmxModes.Count} " +
                    $"/ activeMode='{_fixture.ActiveDmxMode?.Name}'");

            // GDTF の物理限界をロード済みデータから取得
            ApplyPhysicalLimitsFromGdtf();
        }

        private void Update()
        {
            if (_receiver == null || _fixture.ActiveDmxMode == null)
            {
                // 原因を1回だけログ出力
                if (!_warnedOnce)
                {
                    _warnedOnce = true;
                    if (_receiver == null)
                        Debug.LogWarning(
                            $"[FixtureController] Skipping: ArtNetReceiver is null on '{gameObject.name}'");
                    else
                        Debug.LogWarning(
                            $"[FixtureController] Skipping: ActiveDmxMode is null on '{gameObject.name}' " +
                            $"(GdtfData={((_fixture.GdtfData == null) ? "NULL" : "OK")})");
                }
                return;
            }

            // スナップショット全体を取得してからキャッシュ配列へコピー（new しない）
            byte[] snapshot = _receiver.DmxBuffer.GetDmxDataSnapshot();
            // Universe は Inspector 上1始まり（業界標準に合わせる）
            // DmxBuffer は0始まりなので -1 して変換する
            int universeStart = (_fixture.Universe - 1) * DmxBuffer.ChannelsPerUniverse;
            System.Buffer.BlockCopy(
                snapshot, universeStart,
                _universeDmxCache, 0,
                DmxBuffer.ChannelsPerUniverse);

            ApplyDmx(_universeDmxCache);
        }

        // ----------------------------------------------------------------
        // 初期化
        // ----------------------------------------------------------------

        private void ApplyPhysicalLimitsFromGdtf()
        {
            // FixtureInstance に書き込まれた GDTF 由来の物理限界・光束を読み取る
            _panMin        = _fixture.PanMin;
            _panMax        = _fixture.PanMax;
            _tiltMin       = _fixture.TiltMin;
            _tiltMax       = _fixture.TiltMax;
            _maxIntensityLm = _fixture.MaxIntensityLm;

            Debug.Log($"[FixtureController] Physical limits applied: " +
                      $"Pan=[{_panMin}°, {_panMax}°]  " +
                      $"Tilt=[{_tiltMin}°, {_tiltMax}°]  " +
                      $"MaxLm={_maxIntensityLm}");
        }

        // ----------------------------------------------------------------
        // DMX 適用
        // ----------------------------------------------------------------

        private void ApplyDmx(byte[] universeDmx)
        {
            float pan    = 0f;
            float tilt   = 0f;
            float dimmer = 0f;
            float r = 1f, g = 1f, b = 1f;
            float cyan = 0f, magenta = 0f, yellow = 0f;
            float zoom = zoomMin;
            bool  hasCmy = false;

            foreach (var channel in _fixture.ActiveDmxMode.Channels)
            {
                if (channel.Offsets.Length == 0) continue;

                byte  coarse = _fixture.GetDmxValue(universeDmx, channel.Offsets[0]);
                byte  fine   = channel.Is16Bit
                    ? _fixture.GetDmxValue(universeDmx, channel.Offsets[1])
                    : (byte)0;

                float norm = channel.Is16Bit
                    ? ((coarse << 8 | fine) / 65535f)
                    : (coarse / 255f);

                string attr = channel.AttributeName.ToLowerInvariant();

                // --- Pan / Tilt ---
                if      (attr == "pan")  pan  = Mathf.Lerp(_panMin,  _panMax,  norm);
                else if (attr == "tilt") tilt = Mathf.Lerp(_tiltMin, _tiltMax, norm);

                // --- Dimmer ---
                else if (attr == "dimmer") dimmer = norm;

                // --- RGB 加算混色（ColorAdd_R/G/B）---
                // GDTF では ColorAdd_R が「R LED の強さ」
                else if (attr is "coloradd_r" or "colorrgb_red"   or "red"   or "r") r = norm;
                else if (attr is "coloradd_g" or "colorrgb_green" or "green" or "g") g = norm;
                else if (attr is "coloradd_b" or "colorrgb_blue"  or "blue"  or "b") b = norm;

                // --- CMY 減算混色（ColorSub_C/M/Y）---
                // フィルタ濃度: 0 = フィルタなし（白）、1 = 完全吸収
                else if (attr is "colorsub_c" or "cyan")    { cyan    = norm; hasCmy = true; }
                else if (attr is "colorsub_m" or "magenta") { magenta = norm; hasCmy = true; }
                else if (attr is "colorsub_y" or "yellow")  { yellow  = norm; hasCmy = true; }

                // --- Zoom ---
                else if (attr == "zoom")
                    zoom = Mathf.Lerp(zoomMin, zoomMax, norm);
            }

            // CMY → RGB 変換（ColorAdd と CMY が混在する灯体は CMY を優先）
            if (hasCmy)
            {
                r = 1f - cyan;
                g = 1f - magenta;
                b = 1f - yellow;
            }

            ApplyPanTilt(pan, tilt);
            ApplyLight(dimmer, r, g, b, zoom);
        }

        // ---- Pan / Tilt -----------------------------------------------

        private void ApplyPanTilt(float panDeg, float tiltDeg)
        {
            if (_fixture.YokeTransform != null)
                _fixture.YokeTransform.localRotation = Quaternion.Euler(0f, panDeg, 0f);

            if (_fixture.HeadTransform != null)
                _fixture.HeadTransform.localRotation = Quaternion.Euler(tiltDeg, 0f, 0f);
        }

        // ---- HDRP ライト ----------------------------------------------

        private void ApplyLight(float dimmer, float r, float g, float b, float zoomDeg)
        {
            if (_fixture.BeamLight == null) return;

            _fixture.BeamLight.color = new Color(r, g, b);

            if (_hdLight != null)
            {
                _hdLight.intensity = dimmer * _maxIntensityLm;
                _hdLight.SetSpotAngle(zoomDeg);
            }
            else
            {
                _fixture.BeamLight.intensity = dimmer;
                _fixture.BeamLight.spotAngle = zoomDeg;
            }
        }
    }
}