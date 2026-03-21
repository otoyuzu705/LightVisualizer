using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using GLTFast;
using GLTFast.Logging;
using Core.GDTF;

namespace Core.Fixture
{
    /// <summary>
    /// GDTF ファイルから glTF モデルをロードし、
    /// Base → Yoke → Head の Transform 階層を持つ GameObject を Scene 上に生成する。
    /// </summary>
    public static class GdtfModelBuilder
    {
        private const string KeywordBase = "base";
        private const string KeywordYoke = "yoke";
        private const string KeywordHead = "head";

        private static readonly string TempExtractRoot =
            Path.Combine(Application.temporaryCachePath, "GdtfModelTemp");

        // ----------------------------------------------------------------
        // 公開 API
        // ----------------------------------------------------------------

        /// <summary>
        /// GDTF ファイルを読み込み、Scene に灯体 GameObject を生成して返す。
        /// 失敗時は null を返す（中途生成した GameObject は必ずクリーンアップする）。
        /// </summary>
        public static async Task<GameObject> BuildAsync(
            string gdtfFilePath,
            GdtfData gdtfData,
            Vector3 worldPosition = default)
        {
            if (!File.Exists(gdtfFilePath))
            {
                Debug.LogError($"[GdtfModelBuilder] File not found: {gdtfFilePath}");
                return null;
            }

            // --- 1. glTF ファイルを一時フォルダに展開 ---
            string extractDir = ExtractGltfFiles(gdtfFilePath);
            if (extractDir == null) return null;

            // --- 2. description.xml を1回だけ読んで全情報を取得 ---
            GdtfGeometryInfo geoInfo = ParseGeometryInfo(gdtfFilePath);

            // --- 3. ルート GameObject を生成 ---
            string fixtureName = $"{gdtfData.Manufacturer}_{gdtfData.FixtureName}";
            var root = new GameObject(fixtureName);
            root.transform.position = worldPosition;

            try
            {
                // --- 4. FixtureInstance をアタッチ ---
                var fixture = root.AddComponent<FixtureInstance>();
                fixture.GdtfFilePath = gdtfFilePath;
                fixture.GdtfData     = gdtfData;

                // GDTF から取得した物理限界・ビーム情報を FixtureInstance に書き込む
                // FixtureController.Start() で読み取って Pan/Tilt 制御・光量に使用する
                fixture.PanMin        = geoInfo.PanMin;
                fixture.PanMax        = geoInfo.PanMax;
                fixture.TiltMin       = geoInfo.TiltMin;
                fixture.TiltMax       = geoInfo.TiltMax;
                fixture.MaxIntensityLm = geoInfo.LuminousFlux;

                // --- 5. パーツごとに glb をロードし Base→Yoke→Head 階層を構築 ---
                await BuildHierarchyAsync(root, extractDir, geoInfo, fixture);

                // --- 6. HDRP スポットライトを Head に追加 ---
                AttachBeamLight(fixture, geoInfo);

                Debug.Log($"[GdtfModelBuilder] Built fixture '{fixtureName}' at {worldPosition}");
                return root;
            }
            catch (Exception e)
            {
                // 中途生成した GameObject をクリーンアップしてから例外を伝播させない
                Debug.LogError(
                    $"[GdtfModelBuilder] Failed to build '{fixtureName}': {e.Message}\n{e.StackTrace}");
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
        }

        // ----------------------------------------------------------------
        // description.xml の一括パース
        // ----------------------------------------------------------------

        /// <summary>
        /// Model / Geometry（Axis 含む）/ Beam 要素から
        /// モデルファイル名・パーツ位置・物理限界値・ビーム情報をまとめて取得する。
        /// description.xml を1回だけ開くことで ZIP アクセスを最小化する。
        /// </summary>
        private static GdtfGeometryInfo ParseGeometryInfo(string gdtfFilePath)
        {
            var info = new GdtfGeometryInfo();
            try
            {
                using var archive = ZipFile.OpenRead(gdtfFilePath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e.FullName), "description.xml",
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null) return info;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);

                // --- Model 要素：ファイル名とサイズ ---
                foreach (var model in doc.Descendants()
                    .Where(x => string.Equals(x.Name.LocalName, "Model",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    string name  = model.Attribute("Name")?.Value ?? "";
                    string file  = model.Attribute("File")?.Value ?? "";
                    string lower = name.ToLowerInvariant();
                    if (string.IsNullOrEmpty(file)) continue;

                    if (lower.Contains(KeywordBase)) info.BaseFile = file;
                    if (lower.Contains(KeywordYoke)) info.YokeFile = file;
                    if (lower.Contains(KeywordHead)) info.HeadFile = file;
                }

                // --- Geometry / Axis 要素：位置行列と物理限界 ---
                var geoAndAxis = doc.Descendants().Where(x =>
                {
                    string tag = x.Name.LocalName;
                    return string.Equals(tag, "Geometry", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, "Axis",     StringComparison.OrdinalIgnoreCase);
                });

                foreach (var geo in geoAndAxis)
                {
                    string name  = geo.Attribute("Name")?.Value ?? "";
                    string lower = name.ToLowerInvariant();
                    Vector3 pos  = ParsePositionMatrix(geo.Attribute("Position")?.Value);

                    if (lower.Contains(KeywordBase)) info.BaseLocalPos = pos;
                    if (lower.Contains(KeywordYoke))
                    {
                        info.YokeLocalPos = pos;
                        // Axis の PhysicalFrom / PhysicalTo が Pan 物理限界
                        if (float.TryParse(geo.Attribute("PhysicalFrom")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float pf))
                            info.PanMin = pf;
                        if (float.TryParse(geo.Attribute("PhysicalTo")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float pt))
                            info.PanMax = pt;
                    }
                    if (lower.Contains(KeywordHead))
                    {
                        info.HeadLocalPos = pos;
                        if (float.TryParse(geo.Attribute("PhysicalFrom")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float tf))
                            info.TiltMin = tf;
                        if (float.TryParse(geo.Attribute("PhysicalTo")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float tt))
                            info.TiltMax = tt;
                    }
                }

                // --- Beam 要素：ビーム角・光束 ---
                var beam = doc.Descendants()
                    .FirstOrDefault(x => string.Equals(x.Name.LocalName, "Beam",
                        StringComparison.OrdinalIgnoreCase));
                if (beam != null)
                {
                    if (float.TryParse(beam.Attribute("BeamAngle")?.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out float ba))
                        info.BeamAngle = ba;
                    if (float.TryParse(beam.Attribute("LuminousFlux")?.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out float lf))
                        info.LuminousFlux = lf;
                    if (float.TryParse(beam.Attribute("ColorTemperature")?.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out float ct))
                        info.ColorTemperature = ct;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GdtfModelBuilder] Failed to parse geometry info: {e.Message}");
            }
            return info;
        }

        /// <summary>
        /// GDTF の Position 行列文字列（行優先4×4）から Unity の localPosition を返す。
        /// GDTF 行列形式: {r0c0..r0c3}{r1c0..r1c3}{r2c0..r2c3}{r3c0..r3c3}
        /// 位置成分: tx=row0[3], ty=row1[3], tz=row2[3]
        ///
        /// 座標系変換（GDTF 右手系 → Unity 左手系）:
        ///   GDTF: Y=上, Z=前(ビューア方向), ビームは -Z 方向（下）に出射
        ///   Unity: Y=上, Z=前
        ///   Robin Forte 等の天吊り灯体では GDTF の -Z が実際の「下」方向 = Unity の -Y に対応
        ///   → Unity.x = GDTF.tx, Unity.y = GDTF.tz, Unity.z = GDTF.ty
        /// </summary>
        private static Vector3 ParsePositionMatrix(string posStr)
        {
            if (string.IsNullOrEmpty(posStr)) return Vector3.zero;
            try
            {
                var rows = new List<float[]>();
                int start = 0;
                while (start < posStr.Length)
                {
                    int open  = posStr.IndexOf('{', start);
                    int close = posStr.IndexOf('}', open + 1);
                    if (open < 0 || close < 0) break;
                    string row = posStr.Substring(open + 1, close - open - 1);
                    var vals   = row.Split(',');
                    var floats = new float[vals.Length];
                    for (int i = 0; i < vals.Length; i++)
                        float.TryParse(vals[i].Trim(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out floats[i]);
                    rows.Add(floats);
                    start = close + 1;
                }
                if (rows.Count < 3) return Vector3.zero;

                float tx = rows[0].Length > 3 ? rows[0][3] : 0f;
                float ty = rows[1].Length > 3 ? rows[1][3] : 0f;
                float tz = rows[2].Length > 3 ? rows[2][3] : 0f;

                // GDTF Z(-下) → Unity Y, GDTF Y → Unity Z
                return new Vector3(tx, tz, ty);
            }
            catch
            {
                return Vector3.zero;
            }
        }

        // ----------------------------------------------------------------
        // glTF 展開
        // ----------------------------------------------------------------

        private static string ExtractGltfFiles(string gdtfFilePath)
        {
            try
            {
                string subDir = Path.Combine(
                    TempExtractRoot,
                    Path.GetFileNameWithoutExtension(gdtfFilePath));

                if (Directory.Exists(subDir))
                    Directory.Delete(subDir, recursive: true);
                Directory.CreateDirectory(subDir);

                using var archive = ZipFile.OpenRead(gdtfFilePath);

                // 低解像度（models/gltf/）を先に展開し、
                // 高解像度（models/gltf_high/ 等）は同名ファイルが既に存在する場合はスキップする。
                // GDTF の低解像度 glb は軽量でエディタ配置に適しており、
                // 高解像度版が同名で上書きすると不必要に重くなる。
                var lowResEntries  = new List<ZipArchiveEntry>();
                var highResEntries = new List<ZipArchiveEntry>();

                foreach (var entry in archive.Entries)
                {
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is not (".gltf" or ".glb" or ".bin" or ".png" or ".jpg" or ".jpeg"))
                        continue;

                    string fullLower = entry.FullName.ToLowerInvariant();
                    // "gltf_high" や "_high" を含むパスを高解像度とみなす
                    if (fullLower.Contains("_high") || fullLower.Contains("high/"))
                        highResEntries.Add(entry);
                    else
                        lowResEntries.Add(entry);
                }

                // 低解像度を先に展開（overwrite: true）
                foreach (var entry in lowResEntries)
                {
                    string destPath = Path.Combine(subDir, entry.Name);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                // 高解像度は同名ファイルが存在しない場合のみ展開
                foreach (var entry in highResEntries)
                {
                    string destPath = Path.Combine(subDir, entry.Name);
                    if (!File.Exists(destPath))
                        entry.ExtractToFile(destPath, overwrite: false);
                }

                return subDir;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GdtfModelBuilder] Failed to extract glTF files: {e.Message}");
                return null;
            }
        }

        // ----------------------------------------------------------------
        // Transform 階層構築
        // ----------------------------------------------------------------

        private static async Task BuildHierarchyAsync(
            GameObject root,
            string extractDir,
            GdtfGeometryInfo geoInfo,
            FixtureInstance fixture)
        {
            // Base
            Transform baseTransform = await LoadPartAsync(
                geoInfo.BaseFile, extractDir, root.transform, "Base");
            if (baseTransform == null)
                baseTransform = CreatePlaceholder("Base", root.transform, Vector3.zero);
            // Base は常に root 原点
            baseTransform.localPosition = Vector3.zero;

            // Yoke（Base の子）
            // GDTF Position を Unity 座標系に変換した localPosition を設定
            Transform yokeTransform = await LoadPartAsync(
                geoInfo.YokeFile, extractDir, baseTransform, "Yoke");
            if (yokeTransform == null)
                yokeTransform = CreatePlaceholder("Yoke", baseTransform, geoInfo.YokeLocalPos);
            else
                yokeTransform.localPosition = geoInfo.YokeLocalPos;

            // Head（Yoke の子）
            Transform headTransform = await LoadPartAsync(
                geoInfo.HeadFile, extractDir, yokeTransform, "Head");
            if (headTransform == null)
                headTransform = CreatePlaceholder("Head", yokeTransform, geoInfo.HeadLocalPos);
            else
                headTransform.localPosition = geoInfo.HeadLocalPos;

            fixture.BaseTransform = baseTransform;
            fixture.YokeTransform = yokeTransform;
            fixture.HeadTransform = headTransform;
        }

        private static async Task<Transform> LoadPartAsync(
            string fileBase, string extractDir, Transform parent, string partName)
        {
            if (string.IsNullOrEmpty(fileBase)) return null;

            string glbPath  = Path.Combine(extractDir, fileBase + ".glb");
            string gltfPath = Path.Combine(extractDir, fileBase + ".gltf");
            string target   = File.Exists(glbPath) ? glbPath
                            : File.Exists(gltfPath) ? gltfPath
                            : null;

            if (target == null)
            {
                Debug.LogWarning(
                    $"[GdtfModelBuilder] Model file not found for '{partName}': {fileBase}");
                return null;
            }

            var partGo = await LoadGltfAsync(target, parent);
            if (partGo == null) return null;

            partGo.name = partName;
            return partGo.transform;
        }

        private static async Task<GameObject> LoadGltfAsync(string gltfPath, Transform parent)
        {
            // UninterruptedDeferAgent を渡すことで DontDestroyOnLoad を回避し
            // エディタモードでも動作させる
            var deferAgent = new UninterruptedDeferAgent();
            var logger     = new ConsoleLogger();

            // ※ GltfImport は Dispose() するとメッシュ・マテリアル等の
            //   Unity アセットが一緒に破棄され Missing (Mesh) になる。
            //   インスタンスはメッシュが不要になるまで生存させる必要があるため、
            //   using は使わない。Unity のシーン破棄時に GC に任せる。
            var gltfImport = new GltfImport(
                downloadProvider:  null,
                deferAgent:        deferAgent,
                materialGenerator: null,
                logger:            logger
            );

            bool success = await gltfImport.Load(new Uri(gltfPath));
            if (!success)
            {
                Debug.LogError($"[GdtfModelBuilder] glTFast failed to load: {gltfPath}");
                gltfImport.Dispose();
                return null;
            }

            var modelRoot = new GameObject("Model");
            modelRoot.transform.SetParent(parent, worldPositionStays: false);

            bool instantiated = await gltfImport.InstantiateMainSceneAsync(modelRoot.transform);
            if (!instantiated)
            {
                Debug.LogError("[GdtfModelBuilder] glTFast failed to instantiate scene");
                UnityEngine.Object.DestroyImmediate(modelRoot);
                gltfImport.Dispose();
                return null;
            }

            // GltfImport をアセットのライフタイム管理用コンポーネントとして
            // modelRoot に保持させる（GameObject が Destroy されたとき一緒に解放）
            modelRoot.AddComponent<GltfImportHolder>().Import = gltfImport;

            return modelRoot;
        }

        // ----------------------------------------------------------------
        // HDRP ライト
        // ----------------------------------------------------------------

        private static void AttachBeamLight(FixtureInstance fixture, GdtfGeometryInfo geoInfo)
        {
            if (fixture.HeadTransform == null) return;

            var lightGo = new GameObject("BeamLight");
            lightGo.transform.SetParent(fixture.HeadTransform, worldPositionStays: false);
            // GDTF のビームは -Z 方向（前方）が基準 → Unity では前方が +Z なので不要な回転はゼロ
            // ただし灯体が天井吊りを想定して下向きに 90° 回す
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var unityLight = lightGo.AddComponent<Light>();
            unityLight.type      = LightType.Spot;
            unityLight.intensity = 0f;
            unityLight.range     = 20f;
            unityLight.spotAngle = geoInfo.BeamAngle > 0f ? geoInfo.BeamAngle : 20f;
            unityLight.color     = Color.white;

            var hdLight = lightGo.AddComponent<HDAdditionalLightData>();
            hdLight.intensity = 0f;
            hdLight.SetSpotAngle(unityLight.spotAngle);
            hdLight.EnableColorTemperature(true);
            hdLight.SetColor(Color.white,
                geoInfo.ColorTemperature > 0f ? geoInfo.ColorTemperature : 6500f);

            fixture.BeamLight = unityLight;
        }

        // ----------------------------------------------------------------
        // ユーティリティ
        // ----------------------------------------------------------------

        private static Transform CreatePlaceholder(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            return go.transform;
        }
    }

    // ----------------------------------------------------------------
    // 内部データクラス
    // ----------------------------------------------------------------

    /// <summary>description.xml から一括取得した Geometry 情報</summary>
    internal sealed class GdtfGeometryInfo
    {
        // Model ファイル名（拡張子なし）
        public string BaseFile { get; set; } = "";
        public string YokeFile { get; set; } = "";
        public string HeadFile { get; set; } = "";

        // パーツの localPosition（GDTF Position 行列から変換）
        public Vector3 BaseLocalPos { get; set; } = Vector3.zero;
        public Vector3 YokeLocalPos { get; set; } = Vector3.zero;
        public Vector3 HeadLocalPos { get; set; } = Vector3.zero;

        // Pan/Tilt 物理限界（degrees）。GDTF Axis の PhysicalFrom/To から取得
        public float PanMin  { get; set; } = -270f;
        public float PanMax  { get; set; } =  270f;
        public float TiltMin { get; set; } = -135f;
        public float TiltMax { get; set; } =  135f;

        // Beam パラメータ
        public float BeamAngle       { get; set; } = 20f;
        public float LuminousFlux    { get; set; } = 10000f;
        public float ColorTemperature{ get; set; } = 6500f;
    }

    /// <summary>
    /// GltfImport を GameObject のライフタイムに紐付けて保持するコンポーネント。
    /// GltfImport は Dispose() すると生成したメッシュ・マテリアルが破棄されるため、
    /// GameObject が Destroy されるまで解放してはいけない。
    /// OnDestroy() で Dispose() することで適切なタイミングで解放する。
    /// </summary>
    internal sealed class GltfImportHolder : MonoBehaviour
    {
        public GltfImport Import { get; set; }

        private void OnDestroy()
        {
            Import?.Dispose();
            Import = null;
        }
    }
}