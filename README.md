# SHARDSTEP Unity

Unity 2023.2.22f1 / WebGLで再構築するSHARDSTEPのCombat Vertical Sliceです。

## 現在の実装

- 移動している間だけ世界時間が進行
- 照準中と入力停止中は完全停止
- Railの手動方向照準
- Bladeの手動方向ダッシュ斬撃
- Gunner / Hound / Sweeper
- 敵ごとの攻撃予兆
- 初期6体＋増援4体
- 敵全滅後のExtraction Gate解放
- SIMPLE Apocalypse Prefab自動検出
- Unity EditModeテスト
- GameCIによるWebGL自動ビルド

## Unityバージョン

```text
2023.2.22f1
changeset 6b19bf4f8115
```

## SIMPLE Apocalypse

商用アセットはGitリポジトリへ含めません。ローカルのUnityへ`.unitypackage`をImport後、次を実行します。

```text
SHARDSTEP > Assets > Scan SIMPLE Apocalypse
```

利用可能なPrefabがCatalog化され、未検出時はPrimitive表示へフォールバックします。

## CI

GitHub Actionsには次のRepository Secretsが必要です。

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

詳細は[`docs/SETUP_JA.md`](docs/SETUP_JA.md)を参照してください。
