using System;
using UnityEditor;
using UnityEngine;
using Core.Network;

namespace Editor.Windows
{ 
    public class DmxMonitorWindow : EditorWindow
    {
        private int _selectedUniverse = 0;
        private Vector2 _scrollPosition;

        [MenuItem("Window/DMX Monitor")]
        public static void ShowWindow()
        {
            var window = EditorWindow.GetWindow(typeof(DmxMonitorWindow));
            window.minSize = new Vector2(800, 600);
            window.Show();
        }

        private void Update()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Art-Net DMX Monitor", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // ユニバース選択
            _selectedUniverse = EditorGUILayout.IntSlider("Universe", _selectedUniverse, 0, DmxBuffer.Universes - 1);
            EditorGUILayout.Space();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("DMX Monitor is only available in Play Mode.", MessageType.Info);
                return;
            }
            
            // ArtNetReceiverをシーンから探す
            var receiver = FindObjectOfType<ArtNetReceiver>();
            if (receiver == null || receiver.DmxBuffer == null)
            {
                EditorGUILayout.HelpBox("No ArtNetReceiver found in the scene.", MessageType.Warning);
                return;
            }
            
            // DMXデータを取得
            byte[] dmxData = receiver.DmxBuffer.GetDmxDataSnapshot();
            
            // 選択されたユニバースの開始インデックス
            int startIndex = _selectedUniverse * DmxBuffer.ChannelsPerUniverse;
            
            
        }

        /// <summary>
        /// DMX512チャンネルを16列グリッドで描画する
        /// </summary>
        private void DrawDmxGrid(byte[] dmxData, int startIndex)
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            int columns = 16;
            int rows = DmxBuffer.ChannelsPerUniverse / columns;
            
            // ヘッダー行の描画
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < columns; col++)
            {
                GUILayout.Label($"Column {col + 1}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(30));
            }
            EditorGUILayout.EndHorizontal();
            
            // DMXチャンネルの描画
            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();
                
                // 行の先頭チャンネル番号
                int startChannel = row * columns + 1;
                GUILayout.Label($"{startChannel:000}", EditorStyles.boldLabel, GUILayout.Width(40));
                
                for (int col = 0; col < columns; col++)
                {
                    int index = startIndex + row * columns + col;
                    byte value = index < dmxData.Length ? dmxData[index] : (byte)0;

                    GUIStyle cellStyle = new GUIStyle(GUI.skin.box)
                    {
                        fixedWidth = 30,
                        fixedHeight = 20,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    cellStyle.normal.textColor = Color.white;
                    
                    // 背景色をDMXの値に応じて変化させる
                    if (value > 0)
                    {
                        GUI.backgroundColor = Color.Lerp(new Color(0.2f, 0.2f, 0.2f), Color.darkGreen, value / 255f);
                    }
                    else
                    {
                        GUI.backgroundColor = Color.black;
                    }
                    
                    // 値の0埋め
                    GUILayout.Box($"{value:000}", cellStyle);
                    
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }    
}

