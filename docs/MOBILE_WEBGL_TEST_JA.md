# SHARDSTEP スマートフォンWebGLテスト手順

## 目的

SHARDSTEPのWebGL成果物を、PCだけでなくiPhone / Androidの実機ブラウザで確認するための手順です。

現在の基準は横画面です。縦画面ではゲーム前面に回転案内を表示します。

## 1. CIでWebGL成果物を作成

GitHubで次を実行します。

```text
Actions
→ Unity 2023.2 WebGL CI
→ Run workflow
→ Branch: main
→ Run workflow
```

完了後、Run画面下部のArtifactsから`shardstep-webgl`をダウンロードして展開します。

## 2. HTTPSで配信

スマートフォン実機テストでは、展開したフォルダ全体をHTTPSで配信します。

必要な構成は次のとおりです。

```text
index.html
Build/
TemplateData/  ※生成される場合
StreamingAssets/  ※生成される場合
```

Cloudflare Pagesなどの静的ホスティングへ、`index.html`がルートになるようにアップロードします。

ローカルPC上での確認には次を使用できますが、スマートフォンの本試験はHTTPS配信版で実施します。

```powershell
npx serve .
```

## 3. iPhone確認項目

- SafariでURLを開ける
- 横向きにすると`TAP TO START`が有効になる
- タップ後にゲームが読み込まれる
- 左側ドラッグで移動できる
- 右側ドラッグから指を離すと攻撃できる
- 2本指で移動と照準を同時に操作できる
- RAIL / BLADEボタンを押しても照準操作が誤発火しない
- ノッチやDynamic IslandにUIが重ならない
- ホーム画面へ移動して復帰しても移動や照準が固定されない
- 端末を回転してもブラウザページがスクロールしない

## 4. Android確認項目

- ChromeでURLを開ける
- 横向きと全画面要求が正常に処理される
- 左右同時タッチが安定している
- 戻る操作やアプリ切替後に入力が固定されない
- 10分以上プレイして著しい発熱、フレーム低下、メモリ警告がない

## 5. 記録する情報

不具合報告には次を含めます。

```text
端末名:
OSバージョン:
ブラウザとバージョン:
画面向き:
再現手順:
期待結果:
実際の結果:
スクリーンショットまたは動画:
```

## 現在の完了状況

- PC WebGL起動: 確認済み
- PC移動操作: 確認済み
- モバイル向けWebGLテンプレート: 実装済み、CI確認前
- モバイル向け入力リセット: 実装済み、実機確認前
- iPhone実機: 未確認
- Android実機: 未確認
