using UnityEngine;
using GLTFast;

namespace Core.Fixture
{
    /// <summary>
    /// GltfImport を GameObject のライフタイムに紐付けて保持するコンポーネント。
    /// GltfImport は Dispose() すると生成したメッシュ・マテリアルが破棄されるため、
    /// GameObject が Destroy されるまで解放してはいけない。
    /// OnDestroy() で Dispose() することで適切なタイミングで解放する。
    ///
    /// ※ MonoBehaviour はファイル名とクラス名の一致・public 修飾が必要。
    ///   GdtfModelBuilder.cs 内の internal 定義から分離し、
    ///   Unity が GUID でスクリプト参照を解決できるようにした。
    /// </summary>
    public sealed class GltfImportHolder : MonoBehaviour
    {
        public GltfImport Import { get; set; }

        private void OnDestroy()
        {
            Import?.Dispose();
            Import = null;
        }
    }
}