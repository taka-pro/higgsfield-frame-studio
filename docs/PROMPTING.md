# モデル別のプロンプト設計

確認日：2026-09-27。日本語発話の利用実績はユーザー確認済みです。音声スイッチがないモデルを、音声非対応とは扱いません。

## I2Vと、今回の2枚参照の違い

I2Vは画像1枚を開始フレームとして動かす処理です。元画像と矛盾する配置変更を大量に指示するより、その後の動作とカメラを記述します。

本アプリは人物と背景を別々に渡すReference-to-Videoです。1枚目は容姿・服装だけ、2枚目は場所・光・空間の参照と明記します。人物画像の背景や全身構図を引き継ぐ必要はありません。発話では胸上、移動では腰上から胸上へ近づく構図を使います。

## Wan 3.0

公式は画像のアップロード順とImage Nの番号を一致させるよう説明しています。人物・場所・動作・カメラ・音を分け、短いカットでは単一ショットを明示します。発話は話者と引用したセリフを結び付け、口の同期も指定します。発話不要なら明示し、BGM不要も別に書きます。

本アプリでは、顔が見える構図、主動作1つ、音声、終了時の表情という順にまとめます。音声パラメーターはtrueです。

出典：[Alibaba Cloud Wan 3.0公式ガイド](https://help.aliyun.com/en/model-studio/wan3-video-generation-prompt-guide)

## Seedance 2.5

ByteDanceの公式作例は@Image Nによる役割の割り当て、構図、時間の流れ、カメラ、音を組み合わせています。今回の参照2枚は人物と場所を明示的に分担させます。動画編集・延長の指示と混ぜず、新しいワンシーンの生成として書きます。

音声はtrue、MCPではomni_referenceを指定します。APIのモデル別エンドポイントとはパラメーター名が異なります。

出典：[ByteDance公式発表と実例](https://seed.bytedance.com/en/blog/one-take-creation-flexible-referencing-introducing-seedance-2-5)

[BytePlus専用ガイド](https://docs.byteplus.com/en/docs/ModelArk/2607689)はページ名と更新日を確認できましたが、本文は閲覧ツールで取得できませんでした。非公式サイトの特殊記号ルールを公式仕様として採用していません。

## MiniMax H3

公式のI2VAガイドは、開始画像の位置づけと、映像・現場音・音楽の区別を推奨しています。人物の発話には話者IDと、日本語をそのまま入れたdialogueタグを使う形式です。

Full-Reference向けには別ガイドがあり、subject_definitions、summary、retention_analysis、detailed_description、overall_soundscape、non_diegetic_musicの6節を提示しています。本アプリでは参照人物と参照背景を別Subjectに割り当て、発話を映像の時系列内に置きます。これらはprompt文字列内の書式であり、RESTのJSONフィールドを追加するものではありません。

HiggsfieldのH3にはgenerate_audio項目がないため追加せず、プロンプトに発話と音環境を記述します。

出典：[MiniMax公式I2VAガイド](https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/docs/VIDEO_PROMPT_WRITING_GUIDE_base_en.md)、[公式Full-Referenceガイド](https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/docs/VIDEO_PROMPT_WRITING_GUIDE_ref_en.md)

## アプリの設計判断

- 空欄なら振り返り・前かがみ・誘う動作は既定のセリフを使用。近づく動作は発話なし、ダンスはインストゥルメンタル音楽を指定する。
- セリフ入力時は原文を維持。短い尺に長い文章を詰めない。
- 発話は胸上の寄り、目線の高さ、顔と口が見える位置。参照の全身構図と区別する。
- 1カットに複数の激しい動作や相反するカメラワークを詰めない。
- 音声生成成功、セリフ一致、口の同期、人物維持、背景維持は別々に評価する。
- MCPでの試作成功だけでは、RESTアプリの実接続を検証したことにならない。

以上は公式資料を踏まえた実装方針です。同じプロンプトでも生成結果は変動します。今回の具体的な実生成結果は非公開の作業記録に保存し、公開資料へ個人の素材や生成URLを自動転記しません。
