using System;
using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Linq;

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
                    ZipArchiveEntry descriptionEntry = FindDescriptionEntry(archive);
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
                Debug.LogError($"[GdtfParser] Failed to parse GDTF file: {e.Message}");
                return null;
            }
        }

        private static ZipArchiveEntry FindDescriptionEntry(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (string.Equals(Path.GetFileName(entry.FullName), "description.xml", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static GdtfData GdtfXmlParser(XDocument doc)
        {
            var data = new GdtfData();
            
            // XMLのルート要素を取得
            XElement root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "GDTF", StringComparison.Ordinal))
            {
                Debug.LogError("[GdtfParser] Invalid GDTF XML: Root element 'GDTF' not found");
                return data;
            }
              
            // FixtureType要素を取得
            XElement fixtureType = root.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, "FixtureType", StringComparison.Ordinal));
            if (fixtureType != null)
            {
                data.Manufacturer = fixtureType.Attribute("Manufacturer")?.Value ?? "Unknown";
                data.FixtureName = fixtureType.Attribute("Name")?.Value ?? "Unknown";
                data.FixtureTypeId = fixtureType.Attribute("FixtureTypeID")?.Value
                                     ?? fixtureType.Attribute("TypeId")?.Value
                                     ?? "Unknown";
            }
            else
            {
                Debug.LogWarning("[GdtfParser] FixtureType element not found");
            }
            
            
            
            // DmxMode要素を取得
            var dmxModeElements = root.Descendants().Where(x => string.Equals(x.Name.LocalName, "DmxMode", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dmxModeElements.Count > 0)
            {
                foreach (var modeElement in dmxModeElements)
                {
                    var mode = new DmxMode
                    {
                        Name = modeElement.Attribute("Name")?.Value ?? "Unknown"
                    };
                    
                    // DmxChannel要素を取得
                    var channelElements = modeElement.Descendants().Where(x => string.Equals(x.Name.LocalName, "DmxChannel", StringComparison.OrdinalIgnoreCase));
                    foreach (var channelElement in channelElements)
                    {
                        var firstChannelFunction = channelElement
                            .Descendants()
                            .FirstOrDefault(x => string.Equals(x.Name.LocalName, "ChannelFunction", StringComparison.OrdinalIgnoreCase));

                        var firstLogicalChannel = channelElement
                            .Descendants()
                            .FirstOrDefault(x => string.Equals(x.Name.LocalName, "LogicalChannel", StringComparison.OrdinalIgnoreCase));

                        string offsetsStr = channelElement.Attribute("Offsets")?.Value;
                        if (string.IsNullOrWhiteSpace(offsetsStr))
                        {
                            offsetsStr = channelElement.Attribute("Offset")?.Value ?? "";
                        }

                        string defaultValue = channelElement.Attribute("DefaultValue")?.Value
                                              ?? firstChannelFunction?.Attribute("Default")?.Value
                                              ?? "0/1";

                        var channel = new DmxChannel
                        {
                            LogicalChannelId = channelElement.Attribute("LogicalChannelId")?.Value
                                               ?? firstLogicalChannel?.Attribute("Attribute")?.Value
                                               ?? channelElement.Attribute("InitialFunction")?.Value
                                               ?? "",
                            DefaultValue = defaultValue,
                            Offsets = ParseOffsets(offsetsStr)
                        };

                        mode.Channels.Add(channel);
                    }
                     
                    data.DmxModes.Add(mode);
                }
            }

            if (data.DmxModes.Count == 0)
            {
                var modeLikeNames = root
                    .Descendants()
                    .Select(x => x.Name.LocalName)
                    .Where(n => n.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToArray();

                string modeCandidates = modeLikeNames.Length == 0 ? "(none)" : string.Join(", ", modeLikeNames);
                Debug.LogWarning($"[GdtfParser] No DmxMode elements found. Mode-like tags: {modeCandidates}");
            }

            return data;
        }

        private static int[] ParseOffsets(string offsetsValue)
        {
            if (string.IsNullOrWhiteSpace(offsetsValue))
            {
                return Array.Empty<int>();
            }

            var parts = offsetsValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = new int[parts.Length];
            int parsedCount = 0;

            foreach (var rawPart in parts)
            {
                string part = rawPart.Trim();
                if (!int.TryParse(part, out int offset))
                {
                    Debug.LogWarning($"[GdtfParser] Invalid offset value: '{part}'");
                    continue;
                }

                parsed[parsedCount++] = offset;
            }

            if (parsedCount == parsed.Length)
            {
                return parsed;
            }

            var result = new int[parsedCount];
            Array.Copy(parsed, result, parsedCount);
            return result;
        }
    }
}
