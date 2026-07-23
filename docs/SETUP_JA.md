# SHARDSTEP Unity セットアップ

## 固定環境

- Unity Editor: `2023.2.22f1`
- Changeset: `6b19bf4f8115`
- Render package: Universal RP 16.0.x
- Build target: WebGL
- CI: GitHub Actions + GameCI v4

## ローカル起動

1. Unity HubからUnity 2023.2.22f1とWebGL Build Supportを導入します。
2. このリポジトリをCloneします。
3. Unity HubでリポジトリのルートをProjectとして開きます。
4. UnityがPackagesを解決するまで待ちます。
5. Playを押すと、空SceneでもRuntime Bootstrapが戦闘Vertical Sliceを生成します。

## SIMPLE Apocalypseの導入

商用アセット本体はリポジトリへCommitしません。

1. `SIMPLE_Apocalypse_Unity_2022_3_v1_2_7.unitypackage`をUnityへImportします。
2. メニューから`SHARDSTEP > Assets > Scan SIMPLE Apocalypse`を実行します。
3. `Assets/SHARDSTEP/Generated/Resources/SimpleApocalypseCatalog.asset`が生成されます。
4. Playを再実行すると、見つかったPrefabがPrimitiveの代わりに使用されます。

生成CatalogとSIMPLE Apocalypseの元データは`.gitignore`対象です。

## 操作

### PC

- WASD / 矢印: 移動
- 右ドラッグ: 手動照準
- 右ボタンを離す: 攻撃
- Q: Rail / Blade切替

### スマートフォン

- 左側ドラッグ: 移動
- 右側長押し・ドラッグ: 手動照準
- 指を離す: 攻撃
- 上部のRAIL / BLADE: 武器切替

移動入力と攻撃実行がない時、世界時間は停止します。照準中も完全停止します。

## GitHub Actions Secrets

Repositoryの`Settings > Secrets and variables > Actions`に以下を登録します。

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

Secretが未登録の場合、GameCIはUnityの起動前またはライセンス認証で失敗します。

## WebGL Build

Unity Editor内:

`SHARDSTEP > Build > WebGL`

GitHub Actions:

`Actions > Unity 2023.2 WebGL CI > Run workflow`

成功すると`shardstep-webgl` Artifactが生成されます。
