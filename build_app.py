#!/usr/bin/env python3
"""Windows 用のアプリ（EXE 1 つ）を作る。

    python3 build_app.py

1. A3見開き作成ツール.html（編集用。pdf-lib と pdf.js を cdnjs から読み込む）の部品の読み込みを、
   vendor/ に置いたファイルの中身に置き換えて、app/tool.html（外部の部品がいらない 1 つの HTML）を作る
2. app/ の起動アプリ（.NET Framework 4.8 + WebView2）をビルドする。ツール本体と WebView2 の DLL は EXE の中に入る
3. dist/ に A3見開き作成ツール.exe と、それを入れた A3MihirakiTool.zip を作る。
   GitHub のリリースには日本語のファイル名を置けないので、配るのは ZIP（名前は英数字）にする
4. 公開ページ（GitHub Pages）用に、編集用の HTML を index.html としてコピーする
"""
import shutil
import subprocess
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / 'A3見開き作成ツール.html'
TOOL = HERE / 'app' / 'tool.html'
EXE = HERE / 'app' / 'bin' / 'Release' / 'net48' / 'A3MihirakiTool.exe'
DIST = HERE / 'dist'
APP_NAME = 'A3見開き作成ツール.exe'
ZIP_NAME = 'A3MihirakiTool.zip'
CDN = 'https://cdnjs.cloudflare.com/ajax/libs'


class ShiftJisName(zipfile.ZipInfo):
    """ZIP の中の日本語のファイル名を Shift_JIS で書く（日本語の Windows で展開しても文字化けしないように）。"""
    def _encodeFilenameFlags(self):
        return self.filename.encode('cp932'), self.flag_bits & ~0x800


def vendor(name):
    text = (HERE / 'vendor' / name).read_text(encoding='utf-8')
    if '</script' in text.lower() or '<!--' in text:
        raise SystemExit(f'{name} に </script> などが含まれていて、そのままでは HTML に入れられません')
    return text


def replace_once(html, old, new):
    if html.count(old) != 1:
        raise SystemExit(f'置き換える場所が見つかりません: {old}')
    return html.replace(old, new)


# 1. 外部の部品がいらない HTML
html = SRC.read_text(encoding='utf-8')
html = replace_once(html, f'<script src="{CDN}/pdf-lib/1.17.1/pdf-lib.min.js"></script>',
                    '<script>\n/* pdf-lib 1.17.1 https://pdf-lib.js.org  MIT License (c) 2019 Andrew Dillon */\n'
                    + vendor('pdf-lib.min.js') + '\n</script>')
html = replace_once(html, f'<script src="{CDN}/pdf.js/3.11.174/pdf.min.js"></script>',
                    '<script>\n' + vendor('pdf.min.js') + '\n</script>')
html = replace_once(html, f'<script id="pdfWorkerSrc" type="text/plain" data-src="{CDN}/pdf.js/3.11.174/pdf.worker.min.js"></script>',
                    '<script id="pdfWorkerSrc" type="text/plain">\n' + vendor('pdf.worker.min.js') + '\n</script>')
if 'cdnjs.cloudflare.com' in html or 'cdn.jsdelivr.net' in html:
    raise SystemExit('まだ外部の部品を読み込む場所が残っています')
TOOL.write_text(html, encoding='utf-8')
print(f'app/tool.html を作りました（{TOOL.stat().st_size // 1024} KB）')

# 2. アプリ
print('> dotnet build -c Release')
subprocess.run(['dotnet', 'build', '-c', 'Release', '--nologo'], cwd=HERE / 'app', check=True)

# 3. 配るもの
if DIST.exists():
    shutil.rmtree(DIST)
DIST.mkdir()
shutil.copy2(EXE, DIST / APP_NAME)
with zipfile.ZipFile(DIST / ZIP_NAME, 'w') as z:
    info = ShiftJisName.from_file(EXE, APP_NAME)
    info.compress_type = zipfile.ZIP_DEFLATED
    z.writestr(info, EXE.read_bytes())

# 4. 公開ページ
shutil.copy2(SRC, HERE / 'index.html')
print('index.html（公開ページ）を作りました')
for f in sorted(DIST.iterdir()):
    print(f'dist/{f.name} を作りました（{f.stat().st_size // 1024} KB）')
