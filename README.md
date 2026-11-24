# A3見開き作成ツール

Word から保存した PDF を入れると、報告書の資料を **A3 横の見開き**にまとめる、ブラウザで動くツールです。
DocuWorks でしていた次の手作業を、まとめて自動で行います。

- 白紙の差し込み（A4 と A3 の切れ目では右側を空けます）
- A3 横の 2 アップ（A4 を 2 ページずつ左右に並べます。横向きのページは、上が左になるように回して入れます）
- A3 ページの差し替え（A3 のページは原寸で 1 枚に。ページごとに「縮小して 2 アップ」にもできます）
- ページ番号（例：`2-#` → 2-1、2-2…。ファイル名が「資料2…」なら自動でこの書式になります）
- 右上の資料名（枠付き。ファイル名がそのまま入ります。入れるページは「1,3,5-7」「全部」のように選べます。ふだんは 1 枚目だけ）

## 使い方

次の 2 つの形で使えます。どちらもインストールは要りません。

- **アプリ版（EXE）**：`A3見開き作成ツール.exe` 1 つで動きます。**Word（.docx・.doc）と DocuWorks（.xdw）のファイルをそのまま入れられます**（パソコンの Word・DocuWorks を裏で動かして PDF にしてから読み込みます）。
  [リリース](https://github.com/yamamotowt08/a3-mihiraki-tool/releases/latest) の `A3MihirakiTool.zip` を展開して使います。
- **HTML 版**：Google Chrome か Microsoft Edge で **https://yamamotowt08.github.io/a3-mihiraki-tool/** を開きます。こちらは PDF だけを入れられます（Word は「名前を付けて保存 → PDF」で PDF にしてから）。

1. ファイルをツールにドラッグします（複数入れると、ファイル名の順につなげます）
2. 仕上がりを確認して「PDF で保存」を押し、保存先を選びます
3. 保存した PDF を DocuWorks Desk にドラッグすると、DocuWorks 文書になります

ファイルはパソコンの中だけで処理し、どこにも送りません。

### アプリ版で Word・DocuWorks を入れるとき

- Word：パソコンの Word で開いて PDF にします。ページの区切りは Word と同じになります。
- DocuWorks：DocuWorks の部品（xdwapi.dll）でページを PDF に書き出します。**ページは画像（400dpi）になります**（DocuWorks の部品でできる書き出し方がこれだけのため）。注釈も入ります。
- アプリ版を動かすには、Microsoft Edge WebView2 ランタイム（Windows 10 / 11 にはふつう入っています）と .NET Framework 4.8 が要ります。

## 直すとき

元は `A3見開き作成ツール.html`（部品は cdnjs から読み込む）です。直したら次のコマンドで、アプリ版（`dist/`）と公開ページ用の `index.html` を作り直します（.NET SDK が必要）。

```
python3 build_app.py
```

アプリ版の中のツールは、`vendor/` の部品（pdf-lib 1.17.1・MIT、PDF.js 3.11.174・Apache-2.0）を HTML の中に入れたものです。
