# Copyright Check Bypass

VRChat SDKのBuild & Publish時に表示される著作権同意ダイアログを出さず、
SDKの同意APIを自動で呼び出すUnity Editor拡張です。

この拡張を有効にすると、ダイアログで同意内容を確認する操作が省かれます。
送信する同意内容と、同意状態の保存処理はSDKのものを使います。

## ALCOM / VCCから導入

リポジトリ追加画面に、次のURLを入力してください。

```text
https://nickel-jp.github.io/copyright-check-bypass/index.json
```

追加後、プロジェクトのパッケージ管理画面で **Copyright Check Bypass** を追加します。
パッケージIDは `com.nickel-jp.copyright-check-bypass` です。

VCCでは `Settings > Packages > Add Repository` から追加できます。
手順の詳細は[VRChat公式の案内](https://vcc.docs.vrchat.com/guides/community-repositories/)を参照してください。

手動配置した旧版がある場合は、VPM版を追加する前に旧版のスクリプトを削除してください。
旧ファイル名は `Copyright Check Bypass.cs` または `VRCCopyrightAgreementAutoAccept.cs` です。
同じ拡張が二重に読み込まれると、パッチの適用・解除が競合する場合があります。

## 手動導入

`Editor/Copyright Check Bypass.cs` をプロジェクトの `Assets/Editor/` 以下へコピーします。
VPM版を導入している場合は、手動配置する必要はありません。

## 互換性と動作

コンパイルに必要なのはUnityEditorとUnityEngineだけです。
パッケージのSDK依存関係は空で、SDK/Harmonyへのアセンブリ参照もありません。
SDKやHarmonyが入っていないプロジェクトでもコンパイルできます。

自動同意が動作するには、対応するVRChat SDKとHarmonyが実行時に読み込まれている必要があります。
SDKやHarmonyが見つからない場合は警告を出し、処理を有効にしません。
SDKのAPIが想定した型やシグネチャと異なる場合も、パッチを適用せずSDK標準の動作を維持します。

同意済みのキャッシュはユーザーIDとコンテンツIDの組で管理します。
処理中にアカウントが切り替わった場合は、新しいユーザーで同意APIを再送します。
同じコンテンツへの未完了の要求は共有し、通信の重複を避けます。
SDKが同意処理の失敗を返した場合は、ビルドをエラーで止めます。

パッチを再適用するメニューは次の場所にあります。

```text
Tools > VRChat SDK > Copyright Agreement Auto Accept > Reinstall Patch
```

## 検証

対象環境はUnity 2022.3.22f1、VRChat SDK Base / Avatars / Worlds 3.10.5です。

Unity 2022.3.22f1の参照DLLと標準ライブラリだけでコンパイルし、SDK/Harmonyへの
参照が生成されないことを確認しています。SDK 3.10.5の実DLLに対する型検査と、
模擬SDK・実際のHarmony 2.0.5を使ったWindows .NET Framework上の
9シナリオ・52アサーションも確認しています。

今回のVPM配布版をUnity Editor内で使ったBuild & Publishと、実際のSDK通信は未検証です。

## 配布ファイルの生成

Python 3で次を実行します。追加パッケージは不要です。

```sh
python scripts/build_vpm.py --write-index
python scripts/build_vpm.py --check
```

`dist/` にVPM用ZIPとSHA-256一覧を作り、`docs/index.json` を更新します。
ZIPにはパッケージ本体だけを含めます。

## ライセンス

[MIT License](LICENSE)
