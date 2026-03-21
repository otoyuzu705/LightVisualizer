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
                Debug.LogError($"[GdtfParser] File not found: {filePath}");
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
                        GdtfData gdtfData = ParseXml(XDocument.Load(descriptorStream));
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
                if (string.Equals(Path.GetFileName(entry.FullName), "description.xml",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private static GdtfData ParseXml(XDocument doc)
        {
            var data = new GdtfData();

            XElement root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "GDTF", StringComparison.Ordinal))
            {
                Debug.LogError("[GdtfParser] Invalid GDTF XML: root element 'GDTF' not found");
                return data;
            }

            // FixtureType
            XElement fixtureType = root.Descendants()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, "FixtureType", StringComparison.Ordinal));
            if (fixtureType != null)
            {
                data.Manufacturer  = fixtureType.Attribute("Manufacturer")?.Value   ?? "Unknown";
                data.FixtureName   = fixtureType.Attribute("Name")?.Value            ?? "Unknown";
                data.FixtureTypeId = fixtureType.Attribute("FixtureTypeID")?.Value
                                  ?? fixtureType.Attribute("TypeId")?.Value
                                  ?? "Unknown";
            }
            else
            {
                Debug.LogWarning("[GdtfParser] FixtureType element not found");
            }

            // DmxModes
            var dmxModeElements = root.Descendants()
                .Where(x => string.Equals(x.Name.LocalName, "DmxMode", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var modeElement in dmxModeElements)
            {
                var mode = new DmxMode
                {
                    Name = modeElement.Attribute("Name")?.Value ?? "Unknown"
                };

                // DmxChannels — DmxMode の直下の DMXChannels コレクションのみ対象にする
                // （ChannelFunction 配下の孫要素を誤収集しないよう Elements() で1階層に絞る）
                var dmxChannelsContainer = modeElement.Elements()
                    .FirstOrDefault(x => string.Equals(x.Name.LocalName, "DMXChannels",
                        StringComparison.OrdinalIgnoreCase));

                var channelElements = dmxChannelsContainer != null
                    ? dmxChannelsContainer.Elements()
                        .Where(x => string.Equals(x.Name.LocalName, "DmxChannel",
                            StringComparison.OrdinalIgnoreCase))
                    : modeElement.Elements()
                        .Where(x => string.Equals(x.Name.LocalName, "DmxChannel",
                            StringComparison.OrdinalIgnoreCase));

                foreach (var channelElement in channelElements)
                {
                    var channel = ParseDmxChannel(channelElement);
                    if (channel != null)
                        mode.Channels.Add(channel);
                }

                data.DmxModes.Add(mode);
            }

            if (data.DmxModes.Count == 0)
            {
                var modeTagNames = root.Descendants()
                    .Select(x => x.Name.LocalName)
                    .Where(n => n.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n);

                Debug.LogWarning(
                    $"[GdtfParser] No DmxMode elements found. Mode-like tags: " +
                    $"{string.Join(", ", modeTagNames.DefaultIfEmpty("(none)"))}");
            }

            return data;
        }

        private static DmxChannel ParseDmxChannel(XElement channelElement)
        {
            // LogicalChannel — GDTF仕様では DmxChannel 直下に1つ存在する
            var logicalChannel = channelElement.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, "LogicalChannel",
                    StringComparison.OrdinalIgnoreCase));

            // ChannelFunction — LogicalChannel 直下の最初のもの（DefaultValue の取得に使用）
            var firstChannelFunction = logicalChannel?.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, "ChannelFunction",
                    StringComparison.OrdinalIgnoreCase));

            // --- AttributeName ---
            // GDTF仕様: LogicalChannel の Attribute 属性が正式（"Pan", "Tilt", "Dimmer" 等）
            // InitialFunction は "ModeName.ChannelName.ChannelFunction名" 形式なのでフォールバックのみ
            string attributeName = logicalChannel?.Attribute("Attribute")?.Value
                                ?? channelElement.Attribute("InitialFunction")?.Value
                                ?? "";

            // --- Offset ---
            // GDTF仕様の正式属性名は "Offset"（単数）
            // 旧仕様や一部ツール出力が "Offsets" を使うこともあるためフォールバック
            string offsetsStr = channelElement.Attribute("Offset")?.Value
                             ?? channelElement.Attribute("Offsets")?.Value
                             ?? "";

            // --- DefaultValue ---
            // GDTF仕様: "分子/分母" 形式（例: "0/1", "128/255"）
            // DmxChannel.DefaultValue はバイト値(0-255)で保持する
            string rawDefault = channelElement.Attribute("DefaultValue")?.Value
                             ?? firstChannelFunction?.Attribute("Default")?.Value
                             ?? "0/1";

            // --- ChannelFunctions ---
            // Attribute名ごとの DMX レンジ情報をすべて収集する
            var channelFunctions = (logicalChannel ?? channelElement).Descendants()
                .Where(x => string.Equals(x.Name.LocalName, "ChannelFunction",
                    StringComparison.OrdinalIgnoreCase))
                .Select(cf => new ChannelFunctionEntry
                {
                    Attribute  = cf.Attribute("Attribute")?.Value  ?? attributeName,
                    Name       = cf.Attribute("Name")?.Value        ?? "",
                    DmxFrom    = ParseDmxFraction(cf.Attribute("DMXFrom")?.Value   ?? "0/1"),
                    DmxDefault = ParseDmxFraction(cf.Attribute("Default")?.Value   ?? rawDefault),
                })
                .ToList();

            return new DmxChannel
            {
                AttributeName     = attributeName,
                Offsets           = ParseOffsets(offsetsStr),
                DefaultValue      = ParseDmxFraction(rawDefault),
                ChannelFunctions  = channelFunctions,
            };
        }

        // ---- ユーティリティ ------------------------------------------------

        /// <summary>
        /// GDTF の "分子/分母" 形式を 0-255 の byte 値に変換する。
        /// 分母が 65535 の場合は 16bit チャンネルとみなし 255 にクランプする。
        /// </summary>
        internal static byte ParseDmxFraction(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            var parts = raw.Split('/');
            if (parts.Length == 2
                && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out float numerator)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out float denominator)
                && denominator > 0f)
            {
                return (byte)Mathf.Clamp(Mathf.RoundToInt(numerator / denominator * 255f), 0, 255);
            }

            // 分数形式でない場合は整数として直接解釈
            if (int.TryParse(raw.Trim(), out int direct))
                return (byte)Mathf.Clamp(direct, 0, 255);

            Debug.LogWarning($"[GdtfParser] Could not parse DMX value: '{raw}', defaulting to 0");
            return 0;
        }

        /// <summary>
        /// カンマ区切りのオフセット文字列を int[] に変換する（1始まりのまま保持）。
        /// </summary>
        private static int[] ParseOffsets(string offsetsValue)
        {
            if (string.IsNullOrWhiteSpace(offsetsValue))
                return Array.Empty<int>();

            var parts = offsetsValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new System.Collections.Generic.List<int>(parts.Length);

            foreach (var rawPart in parts)
            {
                string part = rawPart.Trim();
                if (int.TryParse(part, out int offset))
                    result.Add(offset);
                else
                    Debug.LogWarning($"[GdtfParser] Invalid offset value: '{part}'");
            }

            return result.ToArray();
        }
    }
}