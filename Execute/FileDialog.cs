using System.Runtime.InteropServices;

namespace OpenWigiDash.SystemPlugin.Execute;

/// <summary>
/// A janela "Abrir" do Windows, para o botão "Procurar…" do Execute. O seletor de arquivo do Blazor (InputFile) só dá o
/// conteúdo, não o caminho; aqui a gente precisa do caminho. Roda numa thread STA própria (a do <c>Sta</c> fica livre
/// para os ícones enquanto a janela está aberta), presa à janela que está na frente (o manager, que recebeu o clique).
/// </summary>
internal static class FileDialog
{
    public const string ProgramFilter =
        "Programas e scripts (*.exe;*.bat;*.cmd;*.ps1;*.lnk)\0*.exe;*.bat;*.cmd;*.ps1;*.lnk;*.com\0Todos os arquivos (*.*)\0*.*\0\0";

    /// <summary>Caminho escolhido, ou <c>null</c> se cancelou.</summary>
    public static Task<string?> PickFileAsync(string title, string filter, string? initialPath)
    {
        var owner = GetForegroundWindow();
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(Show(owner, title, filter, initialPath));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        { IsBackground = true, Name = "Execute file dialog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private const int MaxPath = 4096;
    private const int OFN_NOCHANGEDIR = 0x8;
    private const int OFN_EXPLORER = 0x80000;
    private const int OFN_PATHMUSTEXIST = 0x800;
    private const int OFN_FILEMUSTEXIST = 0x1000;
    private const int OFN_NODEREFERENCELINKS = 0x100000; // .lnk volta como .lnk (o atalho guarda argumentos e pasta)
    private const int OFN_DONTADDTORECENT = 0x2000000;

    private static string? Show(IntPtr owner, string title, string filter, string? initialPath)
    {
        var buffer = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            var bytes = new byte[MaxPath * 2];
            string? folder = null;
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                var path = initialPath.Trim().Trim('"');
                folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (File.Exists(path))
                {
                    var name = System.Text.Encoding.Unicode.GetBytes(Path.GetFileName(path));
                    Array.Copy(name, bytes, Math.Min(name.Length, bytes.Length - 2));
                }
            }
            Marshal.Copy(bytes, 0, buffer, bytes.Length);

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = MaxPath,
                lpstrInitialDir = folder,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_NODEREFERENCELINKS | OFN_DONTADDTORECENT,
            };
            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
