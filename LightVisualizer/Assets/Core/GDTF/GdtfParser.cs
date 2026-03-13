using System;
using System.Net;
using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Core.GDTF
{
    public static class GdtfParser
    {
        /// <summary>
        /// .gdtfファイルを読み込み、description.xmlからGdtfDataを生成する
        /// </summary>
        public static GdtfData ParseGdtf(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"File {filePath} does not exist");
                return null;
            }

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(filePath))
                {
                    ZipArchiveEntry descriptionEntry = archive.GetEntry("description.xml");
                    if (descriptionEntry == null)
                    {
                        Debug.LogError("[GdtfParser] description.xml not found in GDTF file");
                        return null;
                    }

                    using (Stream descriptorStream = descriptionEntry.Open())
                    {
                        // XMLをパースしてGdtfDataを生成
                        GdtfData gdtfData = GdtfXmlParser(XDocument.Load(descriptorStream));
                        return gdtfData;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GdtfPaeser] Failed to parse GDTF file: {e.Message}");
                return null;
            }
        }

        private static GdtfData GdtfXmlParser(XDocument doc)
        {
            var data = new GdtfData();
            
            // XMLのルート要素を取得
            XElement root = doc.Element("GDTF");
            if (root == null)
            {
                Debug.LogError("[GdtfParser] Invalid GDTF XML: Root element 'GDTF' not found");
                return data;
            }
            // TODO: GDTF 1.2準拠のため、rootのDataVersion属性（例: "1.2"）を検証する。
             
            // FixtureType要素を取得
            XElement fixtureType = root.Element("FixtureType");
            if (fixtureType != null)
            {
                data.Manufacturer = fixtureType.Attribute("Manufacturer")?.Value ?? "Unknown";
                data.FixtureName = fixtureType.Attribute("Name")?.Value ?? "Unknown";
                // TODO: GDTF 1.2の属性名はFixtureTypeID。現在のTypeId参照を仕様に合わせて見直す。
                data.FixtureTypeId = fixtureType.Attribute("TypeId")?.Value ?? "Unknown";
            }
            // TODO: GDTF 1.2必須子要素（例: AttributeDefinitions / Geometries / DMXModes）の存在検証を追加する。
            
            // DmxMode要素を取得
            var dmxModeElements = root.Descendants("DmxMode");
            if (dmxModeElements != null)
            {
                foreach (var modeElement in dmxModeElements)
                {
                    var mode = new DmxMode
                    {
                        Name = modeElement.Attribute("Name")?.Value ?? "Unknown"
                    };
                    
                    // DmxChannel要素を取得
                    var channelElements = modeElement.Descendants("DmxChannel");
                    if (channelElements != null)
                    {
                        foreach (var channelElement in channelElements)
                        {
                            var channel = new DmxChannel
                            {
                                LogicalChannelId = channelElement.Attribute("LogicalChannelId")?.Value ?? "",
                                DefaultValue = channelElement.Attribute("DefaultValue")?.Value ?? "0/1"
                            };
                            
                            // Offsets属性をカンマ区切りでパース
                            string offsetsStr = channelElement.Attribute("Offsets")?.Value ?? "";
                            string[] offsetParts = offsetsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            // TODO: int.Parse直呼び出しをTryParseベースに変更し、不正Offsets値での例外終了を防ぐ。
                            int[] offsets = Array.ConvertAll(offsetParts, int.Parse);
                            channel.Offets = offsets;
                            
                            mode.Channels.Add(channel);
                        }
                    }
                    
                    data.DmxModes.Add(mode);
                }
            }

            return data;
        }
    }
}
