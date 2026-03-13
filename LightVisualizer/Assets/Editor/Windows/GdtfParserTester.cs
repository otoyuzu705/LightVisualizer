using System;
using Core.GDTF;
using UnityEditor;
using UnityEngine;

namespace Editor.Windows
{
    public class GdtfParserTester
    {
        [MenuItem("Window/GDTF Parser Tester")]
        public static void TestGdtfParser()
        {
            // GDTFファイルを選択するダイアログを開く
            string path = EditorUtility.OpenFilePanel("Select GDTF File", "", "gdtf");
            
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[GDTF Tester] No GDTF file selected.");
                return;
            }

            try
            {
                // GDTFファイルをパースしてデータ構造に変換
                GdtfData parsedData = GdtfParser.ParseGdtf(path);
                
                // パース結果をコンソールに出力
                if (parsedData != null)
                {
                    Debug.Log($"[GDTF Tester] Manufacturer: {parsedData.Manufacturer}");
                    Debug.Log($"[GDTF Tester] Fixture Name: {parsedData.FixtureName}");
                    Debug.Log($"[GDTF Tester] Fixture Type ID: {parsedData.FixtureTypeId}");
                    Debug.Log($"[GDTF Tester] DMX Modes Count: {parsedData.DmxModes.Count}");

                    foreach (var mode in parsedData.DmxModes)
                    {
                        Debug.Log($"  DMX Mode: {mode.Name}, Channels: {mode.Channels.Count}");
                        
                        // 最初の数チャンネルだけサンプルとして出力するようにする
                        for (int i = 0; i < Math.Min(mode.Channels.Count, 5); i++)
                        {
                            var channel = mode.Channels[i];
                            string offsetsText = channel.Offsets.Length == 0 ? "(none)" : string.Join(",", channel.Offsets);
                            Debug.Log($"    - Ch: {channel.LogicalChannelId} | Offsets: {offsetsText} | Default: {channel.DefaultValue}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[GDTF Parser Tester] Failed to parse selected GDTF file.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[GDTF Parser Tester] Error parsing GDTF file: " + e.Message);
                throw;
            }
        }
        
    }
}
