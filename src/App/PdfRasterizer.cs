using System.Runtime.InteropServices;
namespace HousePlanning;
// Only rasterization APIs are used. No JavaScript, forms or external resources are executed.
public sealed class PdfRasterizer : IDisposable
{
    const string Dll = "pdfium";
    [DllImport(Dll)] static extern void FPDF_InitLibrary();
    [DllImport(Dll)] static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr length, IntPtr password);
    [DllImport(Dll)] static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Dll)] static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Dll)] static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport(Dll)] static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport(Dll)] static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll)] static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll)] static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport(Dll)] static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll)] static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll)] static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll)] static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll)] static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Dll)] static extern uint FPDF_GetLastError();
    static bool initialized;
    IntPtr doc, memory;
    public int Count => FPDF_GetPageCount(doc);
    public PdfRasterizer(string path)
    {
        if (!initialized)
        {
            string dll = System.IO.Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
            if (!File.Exists(dll))
                dll = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Godot.OS.GetExecutablePath())!, "pdfium.dll");
            if (!File.Exists(dll))
                dll = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("res://"), "native", "pdfium.dll");
            NativeLibrary.SetDllImportResolver(typeof(PdfRasterizer).Assembly, (name, _, _) => name == Dll ? NativeLibrary.Load(dll) : IntPtr.Zero);
            FPDF_InitLibrary();
            initialized = true;
        }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("PDFは128MB以下にしてください。");
        memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        doc = FPDF_LoadMemDocument64(memory, (UIntPtr)bytes.Length, IntPtr.Zero);
        if (doc == IntPtr.Zero)
        {
            var error = FPDF_GetLastError();
            Dispose();
            throw new InvalidDataException(error == 4 ? "暗号化PDFには対応していません。解除したPDFを使用してください。" : "PDFを開けません。ファイルが破損していないか確認してください。");
        }
    }
    public Godot.Image Render(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var page = FPDF_LoadPage(doc, index);
        if (page == IntPtr.Zero)
            throw new InvalidDataException("ページを読み込めません。");
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            double w = FPDF_GetPageWidth(page), h = FPDF_GetPageHeight(page);
            double scale = Math.Min(3, 4096 / Math.Max(w, h));
            int width = Math.Max(1, (int)(w * scale)), height = Math.Max(1, (int)(h * scale));
            bitmap = FPDFBitmap_Create(width, height, 1);
            if (bitmap == IntPtr.Zero)
                throw new InvalidDataException("PDF画像のメモリを確保できません。");
            FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff);
            FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1);
            int stride = FPDFBitmap_GetStride(bitmap);
            var raw = new byte[stride * height];
            Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), raw, 0, raw.Length);
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int s = y * stride + x * 4, t = (y * width + x) * 4;
                    rgba[t] = raw[s + 2];
                    rgba[t + 1] = raw[s + 1];
                    rgba[t + 2] = raw[s];
                    rgba[t + 3] = raw[s + 3];
                }
            return Godot.Image.CreateFromData(width, height, false, Godot.Image.Format.Rgba8, rgba);
        }
        finally { if (bitmap != IntPtr.Zero) FPDFBitmap_Destroy(bitmap); FPDF_ClosePage(page); }
    }
    public void Dispose()
    {
        if (doc != IntPtr.Zero)
        {
            FPDF_CloseDocument(doc);
            doc = IntPtr.Zero;
        }
        if (memory != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(memory);
            memory = IntPtr.Zero;
        }
    }
}
