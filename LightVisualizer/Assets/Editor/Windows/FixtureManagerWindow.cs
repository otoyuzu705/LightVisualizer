#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Core.GDTF;
using Core.Fixture;

namespace Editor.Windows
{
    /// <summary>
    /// Fixture Manager ウィンドウ。
    /// プロジェクト内の .gdtf ファイルを一覧表示し、
    /// ダブルクリックまたは "Place in Scene" ボタンで Scene に灯体を配置する。
    /// Menu: Window > Fixture Manager
    /// </summary>
    public class FixtureManagerWindow : EditorWindow
    {
        private List<string>   _gdtfPaths     = new();
        private List<GdtfData> _gdtfDataCache = new();
        private int            _selectedIndex = -1;
        private Vector2        _scrollPos;
        private bool           _isLoading;

        // 非同期処理中に発生した例外を OnGUI で表示するために保持する
        private string _lastError;

        [MenuItem("Window/Fixture Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<FixtureManagerWindow>("Fixture Manager");
            window.minSize = new Vector2(360, 480);
            window.Show();
        }

        private void OnEnable() => RefreshGdtfList();

        // ----------------------------------------------------------------
        // GUI
        // ----------------------------------------------------------------

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawFileList();
            EditorGUILayout.Space(4);
            DrawPlaceButton();
            DrawErrorBox();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("GDTF Fixtures", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                RefreshGdtfList();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFileList()
        {
            if (_gdtfPaths.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No .gdtf files found in the project.\n" +
                    "Place .gdtf files anywhere under Assets/ and click Refresh.",
                    MessageType.Info);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < _gdtfPaths.Count; i++)
            {
                bool isSelected = (i == _selectedIndex);
                var rowStyle = isSelected
                    ? new GUIStyle(EditorStyles.selectionRect)
                    : new GUIStyle(GUIStyle.none);

                EditorGUILayout.BeginHorizontal(rowStyle);

                string label = Path.GetFileNameWithoutExtension(_gdtfPaths[i]);
                if (_gdtfDataCache.Count > i && _gdtfDataCache[i] != null)
                {
                    var d = _gdtfDataCache[i];
                    label = $"{d.Manufacturer}  {d.FixtureName}";
                }

                if (GUILayout.Button(label, EditorStyles.label))
                {
                    _selectedIndex = i;
                    _lastError     = null;
                }

                EditorGUILayout.EndHorizontal();

                if (Event.current.type == EventType.MouseDown
                    && Event.current.clickCount == 2
                    && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    _lastError     = null;
                    _ = PlaceSelectedFixtureAsync();
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPlaceButton()
        {
            using (new EditorGUI.DisabledScope(_selectedIndex < 0 || _isLoading))
            {
                string btnLabel = _isLoading ? "Placing..." : "Place in Scene";
                if (GUILayout.Button(btnLabel, GUILayout.Height(32)))
                {
                    _lastError = null;
                    _ = PlaceSelectedFixtureAsync();
                }
            }

            if (_selectedIndex >= 0
                && _gdtfDataCache.Count > _selectedIndex
                && _gdtfDataCache[_selectedIndex] != null)
            {
                var d = _gdtfDataCache[_selectedIndex];
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Manufacturer", d.Manufacturer);
                EditorGUILayout.LabelField("Fixture",      d.FixtureName);
                EditorGUILayout.LabelField("Type ID",      d.FixtureTypeId);
                EditorGUILayout.LabelField("DMX Modes",    d.DmxModes.Count.ToString());
            }
        }

        private void DrawErrorBox()
        {
            if (!string.IsNullOrEmpty(_lastError))
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
        }

        // ----------------------------------------------------------------
        // GDTF スキャン
        // ----------------------------------------------------------------

        private void RefreshGdtfList()
        {
            _gdtfPaths.Clear();
            _gdtfDataCache.Clear();
            _selectedIndex = -1;
            _lastError     = null;

            string[] guids = AssetDatabase.FindAssets("t:DefaultAsset");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".gdtf", StringComparison.OrdinalIgnoreCase)) continue;

                string absPath = Path.GetFullPath(path);
                _gdtfPaths.Add(absPath);
                _gdtfDataCache.Add(GdtfParser.ParseGdtf(absPath));
            }

            Repaint();
        }

        // ----------------------------------------------------------------
        // Scene 配置
        // async void の代わりに Task を返す。呼び出し側は _ = ... で破棄し、
        // 例外は try-catch で捕捉してウィンドウに表示する。
        // ----------------------------------------------------------------

        private async Task PlaceSelectedFixtureAsync()
        {
            // ダブルクリック連打による2タスク並走を防ぐ
            // (_isLoading = true への代入より前に2回目が入るケースを防ぐ)
            if (_isLoading) return;
            if (_selectedIndex < 0 || _selectedIndex >= _gdtfPaths.Count) return;

            string   gdtfPath = _gdtfPaths[_selectedIndex];
            GdtfData gdtfData = _gdtfDataCache.Count > _selectedIndex
                ? _gdtfDataCache[_selectedIndex]
                : null;

            if (gdtfData == null)
            {
                gdtfData = GdtfParser.ParseGdtf(gdtfPath);
                if (gdtfData == null)
                {
                    _lastError = $"Failed to parse GDTF:\n{gdtfPath}";
                    Repaint();
                    return;
                }
            }

            Vector3 spawnPos = GetSceneViewCenter();
            _isLoading = true;
            Repaint();

            try
            {
                GameObject fixtureGo =
                    await GdtfModelBuilder.BuildAsync(gdtfPath, gdtfData, spawnPos);

                if (fixtureGo != null)
                {
                    fixtureGo.AddComponent<FixtureController>();
                    Undo.RegisterCreatedObjectUndo(fixtureGo, $"Place {gdtfData.FixtureName}");
                    Selection.activeGameObject = fixtureGo;
                    Debug.Log(
                        $"[FixtureManager] Placed '{gdtfData.FixtureName}' at {spawnPos}");
                }
                else
                {
                    _lastError = $"Failed to build fixture: {gdtfData.FixtureName}";
                }
            }
            catch (Exception e)
            {
                // Task を返すことで catch できる（async void では不可能）
                _lastError = $"Exception: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                _isLoading = false;
                Repaint();
            }
        }

        private static Vector3 GetSceneViewCenter()
        {
            if (SceneView.lastActiveSceneView != null)
                return SceneView.lastActiveSceneView.pivot;
            return Vector3.zero;
        }
    }
}
#endif