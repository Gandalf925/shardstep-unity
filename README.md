# SHARDSTEP Unity

Unity 2023.2.22f1 / WebGLで再構築する、`Enter the Chronosphere` itch版の時間同期型戦闘を基準にしたSHARDSTEPのCombat Vertical Sliceです。

## システム基準

- 入力を考えている間は、敵・弾丸・攻撃進行を含む世界時間が完全停止
- プレイヤーが1行動を確定したときだけ、全オブジェクトが共通の約0.18秒を進行
- MOVEはタップ地点への移動ではなく方向指定。1回につき2.6ワールド単位の固定ショートステップ
- 移動は残像付き高速ダッシュで表現し、経路中の衝突判定を維持
- プレイヤー弾・敵弾は実体Projectile。停止中も空間に残り、次の1行動で進む距離を読める
- AIM / FIRE、BLADE、WAIT、RELOAD、SWAPはすべて1行動を消費
- BLADEは敵弾を破壊可能
- 増援は実時間ではなく行動数と撃破数で発生

## 現在の実装

- Chronosphere共通アクションクロック
- 固定距離MOVEと障害物・足場判定
- 残像付きショートステップ
- Railの手動方向照準、4発マガジン、リロード
- Bladeの方向指定ダッシュ斬撃と敵弾破壊
- プレイヤー・Gunner双方の物理Projectile
- WAIT / SWAP / RELOADの時間コスト
- Gunner / Hound / Sweeperと攻撃予兆
- 初期6体＋行動数連動の増援4体
- Ammo / Health Pickup
- 敵全滅後のExtraction Gate解放
- 勝敗後の完全停止
- SIMPLE Apocalypse Prefab自動検出
- Unity EditModeテスト
- GameCIによるWebGL検証ビルド
- PC / タッチ端末共通入力、縦画面Safe Area対応

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

- Unity / CI設定: [`docs/SETUP_JA.md`](docs/SETUP_JA.md)
- スマートフォン配信・実機試験: [`docs/MOBILE_WEBGL_TEST_JA.md`](docs/MOBILE_WEBGL_TEST_JA.md)

## 実機確認の合格条件

- 放置中は敵と弾丸が1pxも進まない
- 遠くをタップしても固定距離の1ステップだけ進む
- 移動中だけ敵と弾丸が同時進行し、終了時に即停止する
- 停止中の弾丸配置から、安全な隙間を判断できる
- MOVE / AIM / WAIT / RELOAD / SWAPの意味がタッチ時に一意に分かる
