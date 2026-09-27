# GitHub公開・ダウンロード配布

## 公開用セット

このワークスペース全体を公開せず、`scripts/Export-PublicSource.ps1` で作る **dist/public-source** の中身だけをGitHubリポジトリへ登録します。
src、tests、docs、scripts、.github、README、SECURITY、LICENSE、SAMPLE-ASSETS、global.json、.gitignoreと、許可したref/person01〜07・place01〜08を含みます。

APIキー、案件資料、原稿、動画、履歴、old、作業用ファイルは含みません。
サンプル画像は作者が生成AIで作成し、同梱を許可した素材です。

## ビルド

Windows x64 / .NET 8 SDK / PowerShell 7を使用します。アプリを閉じてから実行します。

```powershell
pwsh -File scripts/Build-Release.ps1
pwsh -File scripts/Export-PublicSource.ps1
```

- 実行版：dist/FrameStudio/HiggsfieldStudio.exe
- GitHub Release添付用：dist/releases/FrameStudio-win-x64.zip
- 整合性確認用：dist/releases/FrameStudio-win-x64.zip.sha256
- 公開用ソース：dist/public-source

.NETランタイムとサンプル画像をZIPに同梱します。利用者はスクリプトを実行する必要はありません。
ファイル名とフォルダ名は固定で更新し、変更対象の旧ファイルだけold/日時/元の相対パスに退避します。
一時ビルドは.local/release-buildを使用します。distにstagingやバージョン別実行フォルダを増やしません。
ZIPは許可リストから作るため、アプリを使った後でもキー・履歴・生成動画を取り込みません。

## 公開手順

1. dist/public-sourceの中身を、新しいGitHubリポジトリのルートとしてアップロードします。ソースの履歴はこの公開用フォルダから開始します。
2. GitHubのActionsでビルド結果を確認します。
3. ReleasesでDraftを作り、ZIPとSHA256の2ファイルをAssetsに添付します。Source code ZIPだけでは実行アプリを配布できません。
4. ZIPを別の書き込み可能な場所へ展開し、サンプル表示・キー入力画面・起動を確認します。有料生成テストをする場合は自分のAPIキーを使います。
5. READMEのYouTube欄は動画公開後にリンクへ差し替えます。
6. Releaseを公開後、リポジトリのReleasesページのURLを動画概要欄へ掲載します。架空のURLは用意しません。

リリース説明文の例：

> 人物と背景を選び、行動・セリフ・秒数を指定して動画を生成できるWindows x64向けアプリです。
> FrameStudio-win-x64.zipをすべて展開してHiggsfieldStudio.exeを起動してください。
> .NETの別途インストールは不要。人物7枚・背景8枚を同梱しています。
> 利用者自身のHiggsfield APIキーが必要で、動画生成にはAPI利用料が発生します。
> 試用版・署名なし。詳細はREADMEをご覧ください。

## 検証

- オフラインのHTTPモックテストを実行。
- 配布ZIPを展開し、--smokeで起動と素材読み込みを確認。
- runtimeconfigがself-containedであることとランタイムDLLの同梱を確認。
- ZIPと公開ソースのファイル一覧にキー・履歴・生成動画・案件資料がないことを確認。
- 未使用のクリーンなWindows実機全般での動作までは保証しません。Windowsのメディア機能や端末ポリシーに依存する部分があります。

CIはWindowsでビルドとテストを行います。workflow_dispatchからの手動実行ではZIPをActions成果物にします。
タグやpushだけで一般公開する機能は入れていません。

