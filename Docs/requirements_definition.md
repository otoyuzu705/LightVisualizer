# 仕様 

## 1. プロジェクト概要

* **プロジェクト名:** Unity Light Visualizer
* **目的:**
  1. ライブ演出における照明の事前シミュレーションおよび動作確認。
  2. UnityのSceneビュー上で直感的に灯体を配置・パッチできる環境の構築。

* **ターゲット動作環境:** ゲーミングノートPC (Windows想定、GPU: RTX 3060 / 4060 クラス以上を推奨)

## 2. システム・アーキテクチャ

本システムは、安全性とパフォーマンスを両立するため「エディタ・オーサリング」と「ランタイム・シミュレーション」の2フェーズで稼働する。

### 2.1 エディタフェーズ (非Playモード時)

* GDTFファイルのインポートと解析。
* 3Dモデル（glTF）の生成とSceneへの配置。
* Inspectorおよびカスタムウィンドウを通じたDMXユニバース/アドレスの割り当て（パッチ作業）。

### 2.2 シミュレーションフェーズ (Playモード時)

* Art-Net (UDP) の受信スレッド起動。
* DMXデータのバッファリングと、HDRPライト/Transformへのリアルタイム反映。

## 3. 機能要件

### 3.1 GDTF 解析・構築モジュール (エディタ拡張)

* **対応フォーマット:** GDTF `.gdtf` ファイル (ZIP形式)。
* **展開とパース:**
* `System.IO.Compression` を使用し、指定したGDTFファイルを展開。
* `description.xml` を読み込み、灯体のDMXチャンネルマップ、物理限界値（パン・チルト等）、ホイール情報（カラー、GOBO）を抽出する。


* **動的3Dモデル生成:**
  * `glTFast` を使用し、GDTF内の `.gltf` / `.bin` モデルデータをUnityのPrefabまたはGameObjectとしてScene上に生成する。
  * XMLのジョイント構造（Base -> Yoke -> Head）に従い、可動部となる `Transform` 階層を自動構築する。


* **DMX Mode切り替え:**
* Inspector上から `DMX Mode` (Standard, Extendedなど) を選択し、アサインされるチャンネル構造を切り替える機能を実装する。



### 3.2 ネットワーク通信 (Art-Net Receiver)

* **対応プロトコル:** Art-Net 4 (UDP / Port: 6454)
* **処理能力:** 最大32ユニバース（16,384 DMXチャンネル）のデータ受信。
* **実装手法:** Playモード開始時に `System.Net.Sockets.UdpClient` を用いた非同期受信ループを別スレッドで構築し、メインスレッド（描画）のフレームレートを低下させない設計とする。

### 3.3 描画・照明シミュレーション (HDRP Rendering)

* **Render Pipeline:** HDRP (High Definition Render Pipeline)

* **対応パラメータ:**
1. **基本制御:** パン、チルト、ディマー(明るさ)、RGB/CMYカラー。
2. **光学系機能:**
    * **色温度 (Color Temperature):** ケルビン値による色調変化。
    * **GOBO (クッキー):** テクスチャの投影、およびインデックス/回転(Rotation)の再現。
    * **フォーカス / ズーム:** ビーム角(Beam Angle)の動的変更。

3. **エフェクト系機能:**
    * **プリズム (Prism) / フロスト (Frost):** シェーダーによる疑似的な光線分割・多重描画、およびソフトフォーカス処理。
    * **マクロ/プリセット:** 灯体固有の内蔵エフェクト（パン・チルトマクロなど）のDMXトリガー対応。





## 4. 非機能要件 (パフォーマンスと最適化)

* **Compute Shaderの活用:** 毎フレームのDMX配列解析から各灯体のTransform回転・Light設定値算出までの処理をGPUにオフロードし、CPUボトルネックを回避する。
* **カリング (Culling):** カメラの視錐台（Frustum）外にある灯体のボリュメトリック計算を間引き、GPU負荷を軽減する。
* **GPU Instancing:** 大量の灯体外装モデルの描画負荷を下げるため、Instancingを有効化する。

## 5. UI/UX 要件 (エディタ拡張機能)

* **Fixture Manager Window:** プロジェクト内のGDTFファイルを一覧表示し、Sceneにドラッグ＆ドロップで配置できる専用のカスタムエディタウィンドウ。
* **Patching Inspector:** 選択した灯体のInspector上で、UniverseとStart Addressを簡単に設定できるGUI。
* **DMX Monitor (Playモード用):** 受信しているArt-Netの値を一覧確認できるデバッグ用のマトリクスUI。