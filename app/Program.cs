using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace A3MihirakiTool
{
    static class Program
    {
        internal const string Title = "A3見開き作成ツール";
        // 作業用のフォルダは各自のパソコンに作る（共有フォルダに書き込まないように）
        internal static readonly string LocalDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A3MihirakiTool");
        internal static string LibDir;

        [STAThread]
        static void Main()
        {
            try { LibDir = ExtractLibraries(); }
            catch (Exception ex)
            {
                MessageBox.Show("起動の準備ができませんでした。\n\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // EXE の横に WebView2 の DLL がないときは、取り出した DLL を使う
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string path = Path.Combine(LibDir, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Run();
        }

        // WebView2 の型を使うのは、DLL の読み込み先を決めたあと（このメソッドの中）から
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run()
        {
            CoreWebView2Environment.SetLoaderDllFolderPath(LibDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        // EXE の中に入れてある WebView2 の DLL を、作業用のフォルダに取り出す（版ごとのフォルダ）
        static string ExtractLibraries()
        {
            string dir = Path.Combine(LocalDir, "lib", Assembly.GetExecutingAssembly().GetName().Version + "-x86");
            Directory.CreateDirectory(dir);
            ExtractResource("lib.Microsoft.Web.WebView2.Core.dll", Path.Combine(dir, "Microsoft.Web.WebView2.Core.dll"));
            ExtractResource("lib.Microsoft.Web.WebView2.WinForms.dll", Path.Combine(dir, "Microsoft.Web.WebView2.WinForms.dll"));
            ExtractResource("native.x86.WebView2Loader.dll", Path.Combine(dir, "WebView2Loader.dll"));
            return dir;
        }

        internal static void ExtractResource(string name, string path)
        {
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (src == null) throw new FileNotFoundException("アプリの中に " + name + " が見つかりません。");
                if (File.Exists(path) && new FileInfo(path).Length == src.Length) return; // 取り出し済み
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (Stream dst = File.Create(tmp)) src.CopyTo(dst);
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    File.Delete(tmp); // ほかに起動中のアプリが使っているときは、そのファイルをそのまま使う
                }
            }
        }
    }

    // A3見開き作成ツール（HTML）を、Edge の部品（WebView2）で表示するアプリ。
    // ブラウザ版との違いは、入れた Word・DocuWorks のファイルを、パソコンの Word・DocuWorks で PDF にしてから渡せること。
    sealed class MainForm : Form
    {
        const string Host = "a3-mihiraki-tool.local"; // アプリの中だけで使うアドレス
        const string ToolName = "A3見開き作成ツール.html";
        const string RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill };
        string convDir;

        public MainForm()
        {
            Text = Program.Title;
            Size = new Size(1400, 900);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* アイコンがなくても動く */ }
            Controls.Add(web);
            Load += async (s, e) => await StartAsync();
        }

        async Task StartAsync()
        {
            try
            {
                string appDir = Path.Combine(Program.LocalDir, "app");
                Directory.CreateDirectory(appDir);
                // ツール本体は EXE の中に入っている。毎回取り出し直して、EXE と同じ版を使う
                string target = Path.Combine(appDir, ToolName);
                if (File.Exists(target)) File.Delete(target);
                Program.ExtractResource("tool.html", target);
                // Word・DocuWorks から作った PDF の置き場所（前回の分は消す）
                convDir = Path.Combine(appDir, "conv");
                try { if (Directory.Exists(convDir)) Directory.Delete(convDir, true); } catch { /* 消せなくても続ける */ }
                Directory.CreateDirectory(convDir);

                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.LocalDir, "WebView2"));
                await web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(Host, appDir, CoreWebView2HostResourceAccessKind.Allow);

                // 「PDF で保存」で選んだ場所への書き込みは、確認を出さずに許可する（保存先は使う人が画面で選ぶ）
                core.PermissionRequested += (s, e) =>
                {
                    if (e.PermissionKind == CoreWebView2PermissionKind.FileReadWrite && IsTool(e.Uri))
                        e.State = CoreWebView2PermissionState.Allow;
                };
                core.NewWindowRequested += (s, e) => { e.Handled = true; OpenInBrowser(e.Uri); };
                core.NavigationStarting += (s, e) =>
                {
                    if (!IsTool(e.Uri)) { e.Cancel = true; OpenInBrowser(e.Uri); }
                };
                core.DocumentTitleChanged += (s, e) => Text = string.IsNullOrEmpty(core.DocumentTitle) ? Program.Title : core.DocumentTitle;
                core.WebMessageReceived += (s, e) => OnWebMessage(core, e);

                core.Navigate("https://" + Host + "/" + Uri.EscapeDataString(ToolName));
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show(this,
                        "このパソコンには、アプリが使う「Microsoft Edge WebView2 ランタイム」が入っていません。\n\n" +
                        "ダウンロードのページを開きますか？（入れるには、パソコン管理担当の許可が必要なことがあります）",
                        Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    OpenInBrowser(RuntimeUrl);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "起動できませんでした。\n\n" + ex.Message, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        // ツールからの頼みごと：入れたファイル（Word・DocuWorks）を PDF にする。
        // ファイルは postMessageWithAdditionalObjects で届くので、パソコンの上の場所（Path）がわかる
        void OnWebMessage(CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!IsTool(e.Source)) return;
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch (ArgumentException) { return; }
            var m = Regex.Match(msg ?? "", "\"type\"\\s*:\\s*\"convert\".*\"id\"\\s*:\\s*\"([A-Za-z0-9]+)\"");
            if (!m.Success) return;
            string id = m.Groups[1].Value;
            string src = null;
            var objs = e.AdditionalObjects;
            if (objs != null && objs.Count > 0 && objs[0] is CoreWebView2File f) src = f.Path;
            if (string.IsNullOrEmpty(src) || !File.Exists(src))
            {
                Reply(core, id, null, "ファイルの場所がわかりませんでした。エクスプローラーからドラッグするか、「PDF を選ぶ」で選んでください。");
                return;
            }
            string pdf = Path.Combine(convDir, id + ".pdf");
            RunSta(() => Converter.ToPdf(src, pdf)).ContinueWith(t =>
            {
                string err = t.Exception?.GetBaseException().Message;
                BeginInvoke((Action)(() => Reply(core, id, err == null ? "https://" + Host + "/conv/" + id + ".pdf" : null, err)));
            });
        }

        static void Reply(CoreWebView2 core, string id, string url, string error)
        {
            string json = "{\"type\":\"converted\",\"id\":\"" + id + "\"" +
                (url != null ? ",\"url\":" + Json(url) : "") + (error != null ? ",\"error\":" + Json(error) : "") + "}";
            try { core.PostWebMessageAsJson(json); } catch { /* 画面を閉じたあとなど */ }
        }

        // Word は STA のスレッドから動かす（画面が固まらないように、別のスレッドで）
        static Task RunSta(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            var th = new Thread(() =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            return tcs.Task;
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static bool IsTool(string uri) =>
            uri != null && uri.StartsWith("https://" + Host + "/", StringComparison.OrdinalIgnoreCase);

        static void OpenInBrowser(string uri)
        {
            if (uri == null || !(uri.StartsWith("https://") || uri.StartsWith("http://"))) return;
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { /* 開けなくても続ける */ }
        }
    }

    // Word・DocuWorks のファイルを PDF にする
    static class Converter
    {
        static readonly object Lock = new object(); // 1 つずつ順に

        public static void ToPdf(string src, string pdf)
        {
            string ext = Path.GetExtension(src).ToLowerInvariant();
            lock (Lock)
            {
                if (ext == ".xdw" || ext == ".xbd") DocuWorksToPdf(src, pdf);
                else WordToPdf(src, pdf);
            }
            if (!File.Exists(pdf)) throw new Exception("PDF ができませんでした。");
        }

        /* ---------- Word：Word を裏で動かして「PDF で保存」する ---------- */
        static void WordToPdf(string src, string pdf)
        {
            Type t = Type.GetTypeFromProgID("Word.Application");
            if (t == null) throw new Exception("このパソコンに Word が入っていないようです。");
            dynamic word = null, doc = null;
            try
            {
                word = Activator.CreateInstance(t);
                word.DisplayAlerts = 0; // 確認の画面を出さない
                doc = word.Documents.Open(FileName: src, ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
                // 17 = PDF、OptimizeFor 0 = 印刷用、Item 0 = 本文（コメントなどは入れない）
                doc.ExportAsFixedFormat(OutputFileName: pdf, ExportFormat: 17, OpenAfterExport: false,
                    OptimizeFor: 0, Item: 0, IncludeDocProps: false, CreateBookmarks: 0, BitmapMissingFonts: true);
            }
            catch (COMException ex)
            {
                throw new Exception("Word で PDF にできませんでした（" + ex.Message + "）。Word でそのファイルが開けるか確かめてください。");
            }
            finally
            {
                if (doc != null)
                {
                    try { doc.Close(SaveChanges: 0); } catch { /* 閉じられなくても続ける */ }
                    Marshal.FinalReleaseComObject((object)doc);
                }
                if (word != null)
                {
                    // 使う人が開いている Word と同じものだったときは、終わらせない
                    try { if ((int)word.Documents.Count == 0) word.Quit(SaveChanges: 0); } catch { /* 続ける */ }
                    Marshal.FinalReleaseComObject((object)word);
                }
            }
        }

        /* ---------- DocuWorks：DocuWorks の部品（xdwapi.dll）で全ページを PDF に書き出す ---------- */
        // ページは画像の PDF になる（DocuWorks の部品でできる書き出し方がこれだけのため）。注釈も入る
        const int XDW_OPEN_READONLY = 0, XDW_AUTH_NODIALOGUE = 1;
        const int XDW_IMAGE_COLOR = 1, XDW_IMAGE_PDF = 3;
        const int XDW_COMPRESS_HIGHQUALITY = 2, XDW_COMPRESS_MRC_HIGHQUALITY = 9, XDW_CONVERT_MRC_OS = 1;
        const int Dpi = 400;

        [StructLayout(LayoutKind.Sequential)]
        struct XDW_OPEN_MODE_EX { public int nSize, nOption, nAuthMode; }

        [StructLayout(LayoutKind.Sequential)]
        struct XDW_DOCUMENT_INFO
        {
            public int nSize, nPages, nVersion, nOriginalData, nDocType, nPermission,
                nShowAnnotations, nDocuments, nBinderColor, nBinderSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XDW_IMAGE_OPTION_EX { public int nSize, nDpi, nColor, nImageType; public IntPtr pDetailOption; }

        [StructLayout(LayoutKind.Sequential)]
        struct XDW_IMAGE_OPTION_PDF { public int nSize, nCompress, nConvertMethod, nEndOfMultiPages; }

        [DllImport("xdwapi.dll", CharSet = CharSet.Unicode)]
        static extern int XDW_OpenDocumentHandleW(string path, out IntPtr handle, ref XDW_OPEN_MODE_EX mode);
        [DllImport("xdwapi.dll", CharSet = CharSet.Ansi)]
        static extern int XDW_OpenDocumentHandle(string path, out IntPtr handle, ref XDW_OPEN_MODE_EX mode);
        [DllImport("xdwapi.dll")]
        static extern int XDW_CloseDocumentHandle(IntPtr handle, IntPtr reserved);
        [DllImport("xdwapi.dll")]
        static extern int XDW_GetDocumentInformation(IntPtr handle, ref XDW_DOCUMENT_INFO info);
        [DllImport("xdwapi.dll", CharSet = CharSet.Unicode)]
        static extern int XDW_ConvertPageToImageFileW(IntPtr handle, int page, string path, ref XDW_IMAGE_OPTION_EX option);
        [DllImport("xdwapi.dll", CharSet = CharSet.Ansi)]
        static extern int XDW_ConvertPageToImageFile(IntPtr handle, int page, string path, ref XDW_IMAGE_OPTION_EX option);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetDllDirectory(string path);

        static bool dllReady;

        // xdwapi.dll がいつもの場所で見つからないときは、DocuWorks を入れたフォルダから探す
        static void PrepareDll()
        {
            if (dllReady) return;
            dllReady = true;
            foreach (string root in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                if (File.Exists(Path.Combine(root, "xdwapi.dll"))) return;
                foreach (string maker in new[] { "FUJIFILM", "Fuji Xerox" })
                    foreach (string sub in new[] { @"DocuWorks\bin", "DocuWorks" })
                    {
                        string dir = Path.Combine(root, maker, sub);
                        if (File.Exists(Path.Combine(dir, "xdwapi.dll"))) { SetDllDirectory(dir); return; }
                    }
            }
        }

        static void DocuWorksToPdf(string src, string pdf)
        {
            PrepareDll();
            IntPtr h = IntPtr.Zero;
            try
            {
                var mode = new XDW_OPEN_MODE_EX { nOption = XDW_OPEN_READONLY, nAuthMode = XDW_AUTH_NODIALOGUE };
                mode.nSize = Marshal.SizeOf(mode);
                bool wide = true;
                int r;
                try { r = XDW_OpenDocumentHandleW(src, out h, ref mode); }
                catch (EntryPointNotFoundException) { wide = false; r = XDW_OpenDocumentHandle(src, out h, ref mode); } // DocuWorks 7 まで
                Check(r, "開けませんでした");

                var info = new XDW_DOCUMENT_INFO();
                info.nSize = Marshal.SizeOf(info);
                Check(XDW_GetDocumentInformation(h, ref info), "ページ数がわかりませんでした");
                if (info.nPages < 1) throw new Exception("DocuWorks の文書にページがありません。");

                // まず画質優先の圧縮（MRC）で。だめなら、ふつうの圧縮で
                r = Export(h, info.nPages, pdf, XDW_COMPRESS_MRC_HIGHQUALITY, wide);
                if (r < 0) { TryDelete(pdf); r = Export(h, info.nPages, pdf, XDW_COMPRESS_HIGHQUALITY, wide); }
                Check(r, "PDF に書き出せませんでした");
            }
            catch (DllNotFoundException)
            {
                throw new Exception("このパソコンに DocuWorks が入っていないか、DocuWorks の部品（xdwapi.dll）が見つかりませんでした。");
            }
            catch (BadImageFormatException)
            {
                throw new Exception("DocuWorks の部品（xdwapi.dll）を読み込めませんでした（32 ビット／64 ビットの違い）。");
            }
            finally
            {
                if (h != IntPtr.Zero) XDW_CloseDocumentHandle(h, IntPtr.Zero);
            }
        }

        // 1 ページ目から最後のページまでを、1 つの PDF に書き出す
        static int Export(IntPtr h, int pages, string pdf, int compress, bool wide)
        {
            var popt = new XDW_IMAGE_OPTION_PDF { nCompress = compress, nConvertMethod = XDW_CONVERT_MRC_OS, nEndOfMultiPages = pages };
            popt.nSize = Marshal.SizeOf(popt);
            IntPtr p = Marshal.AllocHGlobal(popt.nSize);
            try
            {
                Marshal.StructureToPtr(popt, p, false);
                var opt = new XDW_IMAGE_OPTION_EX { nDpi = Dpi, nColor = XDW_IMAGE_COLOR, nImageType = XDW_IMAGE_PDF, pDetailOption = p };
                opt.nSize = Marshal.SizeOf(opt);
                return wide ? XDW_ConvertPageToImageFileW(h, 1, pdf, ref opt) : XDW_ConvertPageToImageFile(h, 1, pdf, ref opt);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        static void Check(int r, string what)
        {
            if (r >= 0) return;
            string why;
            switch ((uint)r)
            {
                case 0x80040001: why = "DocuWorks が入っていません"; break;
                case 0x80070002: why = "ファイルが見つかりません"; break;
                case 0x80070005: why = "ファイルを読む権限がありません"; break;
                case 0x80070020: why = "ほかのアプリがファイルを使っています。DocuWorks で開いていたら閉じてください"; break;
                case 0x8007000B: why = "DocuWorks の文書として読めません"; break;
                case 0x800E0004: why = "新しい版の DocuWorks で作られた文書です"; break;
                case 0x80040003: why = "パスワードや署名で守られた文書です"; break;
                default: why = "エラー 0x" + ((uint)r).ToString("X8"); break;
            }
            throw new Exception("DocuWorks の文書を" + what + "（" + why + "）。");
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 続ける */ }
        }
    }
}
