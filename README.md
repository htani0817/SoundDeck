---
title: SoundDeck
date: 2026-10-13
updated: 2026-10-15
tags:
  - コーディング/csharp
  - コーディング/windows
  - プロジェクト/開発中
status: active
related:
  - "[[2026-10-13-技術選定と全体アーキテクチャ]]"
---

# SoundDeck

Windows 11 向けの音量ミキサーです。通知領域（タスクバー右下）に常駐し、アイコンをクリックすると Windows 11 のクイック設定と同じ見た目のパネルが開きます。アプリごとの音量と出力先に加えて、プロファイル、アプリ音量の記憶、マイクのミュート用ホットキー、デバイスの抜き差しに合わせた自動切替、通話中に他の音を下げるダッキングを使えます。

[English summary](#english)

## ダウンロードと起動

1. [Releases](https://github.com/htani0817/SoundDeck/releases) から `SoundDeck-v<バージョン>.exe`（例: `SoundDeck-v1.0.0.exe`）をダウンロードします
2. 好きな場所に置きます（例: `C:\Users\<ユーザー名>\AppData\Local\Programs\SoundDeck`）
3. その `SoundDeck-v<バージョン>.exe` を起動します

インストーラーはありません。.NET や Windows App SDK を別に入れる必要もありません（exe に同梱しています）。初めて起動したときは、同梱している部品を `%TEMP%\.net\SoundDeck-v<バージョン>` に展開するため、少し時間がかかります。

> [!WARNING]
> 実行ファイルにはコード署名をしていないため、初回起動時に「Windows によって PC が保護されました」と表示されることがあります。「詳細情報」→「実行」を押すと起動できます。

> [!TIP]
> 起動すると通知領域にスピーカーのアイコンが出ます。見当たらないときはタスクバー右端の「^」の中を確認してください。常に表示したい場合は、アイコンをタスクバーへドラッグするか、Windows の「設定」→「個人用設定」→「タスクバー」→「その他のシステム トレイ アイコン」で SoundDeck をオンにします。

## 使い方

| 操作 | 動作 |
| --- | --- |
| アイコンを左クリック | 音量パネル（フライアウト）を開く・閉じる |
| アイコンを右クリック | メニュー（出力・入力デバイスの切り替え、プロファイル、ミュート、設定、Windows のサウンド画面（再生・録音デバイス）、終了） |
| アイコンを中クリック | 既定の出力デバイスをミュート・解除 |
| アイコンの上でホイール | 既定の出力デバイスの音量を上げ下げ |
| `Ctrl + Alt + M` | マイクのミュート・解除（画面下に表示が出る） |
| `Ctrl + Alt + V` | 音量パネルを開く・閉じる |

ホットキーは設定画面で変更・追加できます。設定画面は、右クリックメニューの「SoundDeck の設定」か、音量パネル右上の歯車から開きます。

### 音量パネル

- 「出力」「入力」タブで、スピーカー類とマイク類を切り替えます
- デバイスごとに音量・ミュート・ピークメーターがあり、その下にそのデバイスで鳴っているアプリが並びます
- 既定以外のデバイスはアプリ一覧を折りたたんでいます。右上のアプリ数のボタンで開閉します
- アプリ横の「…」から、そのアプリの出力先の変更、音量を覚えるかどうか、ダッキングのきっかけにするかを選べます
- スライダーの上でホイールを回しても音量を変えられます

## 機能

| 機能 | 内容 |
| --- | --- |
| アプリ別の音量 | デバイスごとに、鳴っているアプリの音量とミュートを操作 |
| アプリ別の出力先 | アプリごとに出力先（入力元）のデバイスを指定 |
| 既定デバイスの切り替え | ワンクリック・右クリックメニュー・ホットキーで切り替え。通話アプリ用の「通信デバイス」も一緒に切り替えるか選べる |
| マイク管理 | 入力デバイスの音量・ミュート・既定の切り替え。マイクを使っているアプリも表示 |
| プロファイル | 既定デバイス、デバイスの音量、アプリごとの音量と出力先をまとめて保存し、ワンクリックかホットキーで呼び出す |
| アプリ音量の記憶 | アプリごとの音量・ミュート・出力先を覚え、次にそのアプリが音を出したときに戻す（アプリ単位で除外可） |
| 自動切替ルール | 「ヘッドホンをつないだら既定にする」「このアプリが鳴り始めたらプロファイルを適用する」など |
| ダッキング | 指定したアプリ（通話アプリなど）が鳴っている間だけ、他のアプリの音量を下げる |
| デバイスの整理 | 表示名の変更、使わないデバイスを一覧から隠す |
| 画面下の表示（OSD） | ホットキーでの変更を画面下に表示。ゲーム中でもフォーカスを奪わない |
| テーマ | 設定の「一般」で「システムに合わせる」（最初の設定）・「ダーク」（黒い背景）・「ライト」（白い背景）を選べる |

## Windows の起動時に自動で起動する

設定画面の「一般」→「Windows の起動時に SoundDeck を開始する」をオンにします。最初はオフです。

## アンインストール

1. 設定画面の「一般」で「Windows の起動時に SoundDeck を開始する」をオフにします（オンにしていた場合）
2. 右クリックメニューの「終了」で SoundDeck を終了します
3. `SoundDeck-v<バージョン>.exe` と、展開先の `%TEMP%\.net\SoundDeck-v<バージョン>` フォルダーを削除します
4. 設定も消す場合は `%LOCALAPPDATA%\SoundDeck` フォルダーを削除します

## プライバシー

SoundDeck はインターネットに接続しません。設定とエラーログはこの PC の `%LOCALAPPDATA%\SoundDeck` にだけ保存します。

| 場所 | 中身 |
| --- | --- |
| `%LOCALAPPDATA%\SoundDeck\settings.json` | 設定一式。壊れていたら `settings.json.broken-日時` に退避して既定値で起動する |
| `%LOCALAPPDATA%\SoundDeck\logs\sounddeck.log` | エラーログ。1 MB を超えたら `.old` に回す |
| レジストリ `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` の `SoundDeck` | 自動起動をオンにしたときだけ書く |

## 動作環境と制限

- Windows 11（21H2 以降）の x64。ARM64 向けのビルド設定も入れていますが、実機では確認していません
- 画面の表示は日本語だけです
- 既定デバイスの切り替えとアプリ別の出力先は、Windows が公開していない API を使っています（Windows のサウンド設定が使っているものと同じ）。Windows の更新で使えなくなる可能性があり、その場合はその操作だけが失敗して、他の機能は動き続けます

## 不具合の報告

[Issues](https://github.com/htani0817/SoundDeck/issues) にお願いします。Windows のバージョン（`winver` で表示されるもの）と、`%LOCALAPPDATA%\SoundDeck\logs\sounddeck.log` の該当部分があると調べやすくなります。

## 開発者向け

### ビルドと起動

.NET 10 SDK（10.0.400 以降）が必要です。

```powershell
dotnet build SoundDeck.slnx -c Debug -p:Platform=x64
./src/SoundDeck.App/bin/x64/Debug/net10.0-windows10.0.22621.0/SoundDeck.exe
```

| 起動オプション | 動作 |
| --- | --- |
| `--show` | 起動してすぐ音量パネルを開く |
| `--settings` | 起動してすぐ設定を開く |
| `--preview-flyout=x,y[,input]` | 見た目の確認用。座標 x,y のあるモニターに、フォーカスを奪わずに音量パネルを出す |
| `--preview-settings=x,y,ページ名` | 同じく設定画面を出す（`general` `hotkeys` `devices` `memory` `profiles` `rules` `ducking` `about`） |
| `--preview-osd=x,y` | 同じく OSD を出す |
| `--preview-theme=system\|light\|dark` | 確認用の表示のテーマを変える（`--preview-*` と組み合わせる） |

二重に起動した場合は、先に動いている方の音量パネルが開きます。確認用（`--preview-*`）の起動だけは、動いている SoundDeck とは別に立ち上がり、トレイ・ホットキー・設定の保存・音量の自動処理を使いません（確認が済んだらタスクマネージャーなどで終了します）。

### テスト

```powershell
dotnet test tests/SoundDeck.Core.Tests
```

テストの対象は OS に依存しない判断部分（プロファイルの差分計算、ルールの一致と連続発火の抑止、ダッキングの下げ戻し、音量の記憶、設定の保存と壊れたファイルの退避、ホットキーの表示）です。GitHub Actions でも push と PR のたびに実行します。

### 配布用に発行する

```powershell
./scripts/publish-win-x64.ps1                    # x64
./scripts/publish-win-x64.ps1 -Runtime win-arm64 # ARM64
```

`artifacts/publish/SoundDeck-v<バージョン>.exe`（ARM64 は `SoundDeck-v<バージョン>-arm64.exe`）に配布用の exe が 1 つできます。バージョンは `src/SoundDeck.App/SoundDeck.App.csproj` の `Version` から取ります。Windows App SDK ごと 1 つにまとめた単一ファイル形式で、初回起動時に中身を `%TEMP%\.net` に展開して動きます。画面のリソースは exe 名に依存しない `resources.pri` にしてあるので、exe の名前を変えても起動できます。

### 構成

```text
sound-deck/
├─ src/
│  ├─ SoundDeck.Core/    OS に依存しない設定・プロファイル・ルール・ダッキング・音量の記憶
│  ├─ SoundDeck.Audio/   Core Audio の COM interop（デバイス、セッション、既定の切り替え、アプリ別の出力先）
│  └─ SoundDeck.App/     WinUI 3 本体（トレイ、音量パネル、設定、OSD、ホットキー）
├─ tests/SoundDeck.Core.Tests/
├─ scripts/publish-win-x64.ps1
└─ docs/
```

設計の理由と実装中の落とし穴は [技術選定と全体アーキテクチャ](docs/2026-10-13-技術選定と全体アーキテクチャ.md) にまとめています。

## ライセンス

[MIT License](LICENSE) です。配布物に同梱している Windows App SDK などのライセンスは [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) にあります。

## English

SoundDeck is a volume mixer for Windows 11 that lives in the notification area. It offers per-app volume and output device selection, default device switching, a microphone tab with a global mute hotkey, saved profiles, per-app volume memory, automation rules (e.g. switch to headphones when they are plugged in) and ducking (lower other apps while a chat app is speaking). The UI is Japanese only for now.

Download `SoundDeck-v<version>.exe` (e.g. `SoundDeck-v1.0.0.exe`) from [Releases](https://github.com/htani0817/SoundDeck/releases), put it anywhere and run it. No installer or runtime is needed; on first launch it unpacks its bundled components to `%TEMP%\.net`. The executable is not code-signed, so SmartScreen may warn you on first launch ("More info" → "Run anyway"). Licensed under the [MIT License](LICENSE).
