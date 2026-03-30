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
        private float _panMin         = -270f;
        private float _panMax         =  270f;
        private float _tiltMin        = -135f;
        private float _tiltMax        =  135f;
        private float _maxIntensityLm = 10000f;

        private FixtureInstance       _fixture;
        private ArtNetReceiver        _receiver;
        private HDAdditionalLightData _hdLight;

        // ---- 警告フラグ（条件ごとに独立させ、条件が回復したらリセットする） --------
        // 単一の _warnedOnce で全条件をまとめると、一度でも警告が出た後に
        // 条件が回復しても Update() の return が解除されないバグの原因になる。
        private bool _warnedNoReceiver = false;
        private bool _warnedNoMode     = false;
        private bool _warnedUniverse   = false;

        // ---- アロケートキャッシュ ------------------------------------------------
        // Update() 毎に new byte[] しないためにフィールドで保持する（GC 圧軽減）
        private byte[] _universeDmxCache;

        // ---- 前フレームの Pan/Tilt/Zoom 値キャッシュ ----------------------------
        // ApplyDmx 内でチャンネルが見つからなかった場合に初期値へ戻らないようにする。
        private float _lastPan  = 0f;
        private float _lastTilt = 0f;
        private float _lastZoom = -1f; // -1f = 未初期化（初回は zoomMin を使うセンチネル値）

        private void Awake()
        {
            _fixture          = GetComponent<FixtureInstance>();
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

            // GdtfData の状態をログ出力（null なら ActiveDmxMode も null になり Update がスキップされる）
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

            // glTF インポート由来の Animator が Transform を毎フレーム上書きしないよう無効化
            DisableImportedAnimators();
        }

        private void Update()
        {
            // ---- ArtNetReceiver チェック ----------------------------------------
            if (_receiver == null)
            {
                if (!_warnedNoReceiver)
                {
                    _warnedNoReceiver = true;
                    Debug.LogWarning(
                        $"[FixtureController] Skipping: ArtNetReceiver is null on '{gameObject.name}'");
                }
                return;
            }
            _warnedNoReceiver = false;

            // ---- ActiveDmxMode チェック -----------------------------------------
            if (_fixture.ActiveDmxMode == null)
            {
                if (!_warnedNoMode)
                {
                    _warnedNoMode = true;
                    Debug.LogWarning(
                        $"[FixtureController] Skipping: ActiveDmxMode is null on '{gameObject.name}' " +
                        $"(GdtfData={(_fixture.GdtfData == null ? "NULL" : "OK")})");
                }
                return;
            }
            // 条件が回復したらフラグをリセット（次に壊れたとき再び警告を出せるようにする）
            _warnedNoMode = false;

            // ---- DMX データ取得（ノーアロケート） --------------------------------
            // Universe は Inspector 上 1始まり → DmxBuffer 内部の 0始まりに変換
            int universeIndex = _fixture.Universe - 1;
            if (!_receiver.DmxBuffer.CopyUniverseTo(universeIndex, _universeDmxCache))
            {
                if (!_warnedUniverse)
                {
                    _warnedUniverse = true;
                    Debug.LogWarning(
                        $"[FixtureController] Universe {_fixture.Universe} is out of range " +
                        $"(valid: 1-{DmxBuffer.Universes}) on '{gameObject.name}'");
                }
                return;
            }
            _warnedUniverse = false;

            ApplyDmx(_universeDmxCache);
        }

        // ----------------------------------------------------------------
        // 初期化
        // ----------------------------------------------------------------

        private void ApplyPhysicalLimitsFromGdtf()
        {
            _panMin         = _fixture.PanMin;
            _panMax         = _fixture.PanMax;
            _tiltMin        = _fixture.TiltMin;
            _tiltMax        = _fixture.TiltMax;
            _maxIntensityLm = _fixture.MaxIntensityLm;

            Debug.Log($"[FixtureController] Physical limits applied: " +
                      $"Pan=[{_panMin}°, {_panMax}°]  " +
                      $"Tilt=[{_tiltMin}°, {_tiltMax}°]  " +
                      $"MaxLm={_maxIntensityLm}");
        }

        /// <summary>
        /// glTFast がインポートした GameObject に付いている Animator を無効化する。
        /// Animator が有効なままだと localRotation が毎フレーム上書きされ、
        /// DMX による Pan/Tilt 制御が無効化される。
        /// </summary>
        private void DisableImportedAnimators()
        {
            if (_fixture.YokeTransform != null)
                foreach (var anim in _fixture.YokeTransform.GetComponentsInChildren<Animator>())
                {
                    anim.enabled = false;
                    Debug.Log($"[FixtureController] Animator disabled on Yoke: {anim.gameObject.name}");
                }

            if (_fixture.HeadTransform != null)
                foreach (var anim in _fixture.HeadTransform.GetComponentsInChildren<Animator>())
                {
                    anim.enabled = false;
                    Debug.Log($"[FixtureController] Animator disabled on Head: {anim.gameObject.name}");
                }
        }

        // ----------------------------------------------------------------
        // DMX 適用
        // ----------------------------------------------------------------

        private void ApplyDmx(byte[] universeDmx)
        {
            // 前フレームの値を初期値として使う（チャンネルが見つからない場合に 0° へ戻らないようにする）
            float pan    = _lastPan;
            float tilt   = _lastTilt;
            float dimmer = 0f;
            float r = 1f, g = 1f, b = 1f;
            float cyan = 0f, magenta = 0f, yellow = 0f;
            float zoom   = _lastZoom < 0f ? zoomMin : _lastZoom;
            bool  hasCmy  = false;
            bool  hasPan  = false;
            bool  hasTilt = false;
            bool  hasZoom = false;

            foreach (var channel in _fixture.ActiveDmxMode.Channels)
            {
                if (channel.Offsets.Length == 0) continue;

                byte coarse = _fixture.GetDmxValue(universeDmx, channel.Offsets[0]);
                byte fine   = channel.Is16Bit
                    ? _fixture.GetDmxValue(universeDmx, channel.Offsets[1])
                    : (byte)0;

                float norm = channel.Is16Bit
                    ? ((coarse << 8 | fine) / 65535f)
                    : (coarse / 255f);

                string attr = channel.AttributeName.ToLowerInvariant();

                // --- Pan / Tilt ---
                if (attr == "pan")  { pan  = Mathf.Lerp(_panMin,  _panMax,  norm); hasPan  = true; }
                else if (attr == "tilt") { tilt = Mathf.Lerp(_tiltMin, _tiltMax, norm); hasTilt = true; }

                // --- Dimmer ---
                else if (attr == "dimmer") dimmer = norm;

                // --- RGB 加算混色（ColorAdd_R/G/B）---
                else if (attr is "coloradd_r" or "colorrgb_red"   or "red"   or "r") r = norm;
                else if (attr is "coloradd_g" or "colorrgb_green" or "green" or "g") g = norm;
                else if (attr is "coloradd_b" or "colorrgb_blue"  or "blue"  or "b") b = norm;

                // --- CMY 減算混色（ColorSub_C/M/Y）---
                else if (attr is "colorsub_c" or "cyan")    { cyan    = norm; hasCmy = true; }
                else if (attr is "colorsub_m" or "magenta") { magenta = norm; hasCmy = true; }
                else if (attr is "colorsub_y" or "yellow")  { yellow  = norm; hasCmy = true; }

                // --- Zoom ---
                else if (attr == "zoom")
                    { zoom = Mathf.Lerp(zoomMin, zoomMax, norm); hasZoom = true; }
            }

            // 今フレームでチャンネルが見つかった場合のみキャッシュを更新
            if (hasPan)  _lastPan  = pan;
            if (hasTilt) _lastTilt = tilt;
            if (hasZoom) _lastZoom = zoom;

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