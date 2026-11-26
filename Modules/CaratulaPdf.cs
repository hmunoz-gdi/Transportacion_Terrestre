using System.Globalization;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;

namespace Transportacion;

internal sealed class CaratulaPdf
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-MX");
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private readonly string assets;

    public CaratulaPdf(string root)
    {
        assets = Path.Combine(root, "Assets", "Caratula");
        GlobalFontSettings.FontResolver = new Fuentes(assets);
    }

    public static DateTimeOffset FechaLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zona);

    public MemoryStream Generar(Permisionario titular, Asignacion? unidad, string foto, CaratulaEmitida emission, string url)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Registro de permisionarios - Carátula";
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.Letter;
        using (var g = XGraphics.FromPdfPage(page))
            Dibujar(g, titular, unidad, foto, emission, url);

        var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return stream;
    }

    private void Dibujar(XGraphics g, Permisionario p, Asignacion? unidad, string foto, CaratulaEmitida emission, string url)
    {
        var blue = new XSolidBrush(XColor.FromArgb(80, 150, 158));
        var fonts = new Dictionary<(double, bool), XFont>();
        XFont Font(double size, bool bold = false)
        {
            var key = (size, bold);
            if (!fonts.TryGetValue(key, out var font))
                fonts[key] = font = new XFont("Caratula", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            return font;
        }
        string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "S/D" : value;
        void Line(string value, double x, double y, double width, double size = 8, bool bold = false,
            bool center = false, XBrush? color = null)
        {
            value = value.Replace('\n', ' ').Replace('\r', ' ');
            var font = Font(size, bold);
            double measured = g.MeasureString(value, font).Width;
            if (measured > width)
                font = Font(size * width / measured, bold);
            g.DrawString(value, font, color ?? XBrushes.Black, new XRect(x, y, width, 13),
                center ? XStringFormats.Center : XStringFormats.CenterLeft);
        }
        void Bar(string label, double x, double y, double width, double height = 13, bool shadow = false)
        {
            if (shadow) g.DrawRectangle(XBrushes.Black, x + 2, y + 3, width, height);
            g.DrawRectangle(blue, x, y, width, height);
            Line(label, x + 3, y + (height - 13) / 2, width - 6, 8.4, true, true, XBrushes.White);
        }
        void Field(string label, string? value, double x, double y, double width, double labelWidth = 103)
        {
            Bar(label, x, y, labelWidth);
            Line(Value(value), x + labelWidth + 5, y, width - labelWidth - 6);
        }
        void Picture(XImage image, double x, double y, double width, double height)
        {
            double ratio = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
            double w = image.PixelWidth * ratio, h = image.PixelHeight * ratio;
            g.DrawImage(image, x + (width - w) / 2, y + (height - h) / 2, w, h);
        }

        using var logo = XImage.FromFile(Path.Combine(assets, "logotipo.png"));
        Picture(logo, 81, 36, 150, 44);
        Line("REGISTRO DE PERMISIONARIOS", 246, 50, 296, 15, true, true);
        g.DrawRectangle(blue, 246, 72, 296, 3);

        Bar("NÚMERO ECONÓMICO", 72, 94, 204, 18);
        Line(Value(p.Economico), 72, 122, 204, 28, true, true);
        Bar("FECHA", 72, 156, 199);
        Line(FechaLocal(emission.Fecha).ToString("dd-MMMM-yyyy", Cultura), 72, 169, 199, center: true);
        Bar("NOMBRE DEL PERMISIONARIO", 72, 195, 199);
        Line(Value(p.Paterno), 72, 209, 199);
        Line(Value(p.Materno), 72, 222, 199);
        Line(Value(p.Nombre), 72, 235, 199);
        Bar("RFC DEL PERMISIONARIO", 72, 256, 199);
        Line(Value(p.Rfc), 72, 269, 199, center: true);
        Bar("AGRUPACIÓN", 72, 286, 199);
        Line(Value(p.Agrupacion), 72, 299, 199, center: true);

        using var portrait = AbrirRetrato(foto);
        Picture(portrait, 395, 98, 130, 121);
        using var qrData = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var matrix = qrData.ModuleMatrix;
        double pixel = 91d / matrix.Count;
        g.DrawRectangle(XBrushes.White, 414, 220, 91, 91);
        for (int y = 0; y < matrix.Count; y++)
        for (int x = 0; x < matrix.Count; x++)
            if (matrix[y][x])
                g.DrawRectangle(XBrushes.Black, 414 + x * pixel, 220 + y * pixel, pixel + 0.04, pixel + 0.04);
        Line("VERIFICAR CARÁTULA", 395, 310, 130, 7, true, true);

        Bar("DOMICILIO DEL PERMISIONARIO", 66, 326, 453, shadow: true);
        Field("CALLE", p.Calle, 66, 347, 453, 90);
        Field("COLONIA", p.Colonia, 66, 365, 453, 90);
        string locality = string.Join(", ", new[] { p.Municipio, p.Estado }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Field("LOCALIDAD", locality, 66, 383, 453, 90);
        Field("C.P.", p.Cp, 66, 401, 130, 55);
        Field("TEL.", p.Telefonos, 202, 401, 317, 42);

        var details = unidad is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(unidad.Detalles);
        string? Get(string key) => details?.GetValueOrDefault(key);
        Bar("DATOS DEL VEHÍCULO", 66, 429, 453, shadow: true);
        Bar("MARCA", 66, 453, 142, 17);
        Bar("MODELO", 219, 453, 142, 17);
        Bar("PLACA", 372, 453, 147, 17);
        Line(Value(Get("marca")), 66, 472, 142, center: true);
        Line(Value(unidad?.Nombre), 219, 472, 142, center: true);
        Line(Value(unidad?.Referencia), 372, 472, 147, center: true);

        Bar("DOCUMENTOS DEL VEHÍCULO", 66, 501, 453, shadow: true);
        Field("FACTURA", Get("numeroFactura"), 66, 524, 218);
        Field("No. DE SERIE", Get("serie"), 293, 524, 226);
        Field("T. CIRCULACIÓN", Get("tarjeta"), 66, 546, 218);
        Field("PÓLIZA DE SEGURO", Get("poliza"), 293, 546, 226);

        Line("EL PERMISIONARIO TENDRÁ RESPONSABILIDAD SOLIDARIA RESPECTO DE LOS ACTOS IRREGULARES COMETIDOS POR SUS OPERADORES,", 72, 575, 447, 6);
        Line("POR LO QUE EN SU CASO, ESTAS DARÁN LUGAR A LAS SANCIONES CORRESPONDIENTES E INCLUSO A LA RESCISIÓN DEL CONVENIO", 72, 584, 447, 6);
        Line("INDIVIDUAL DE ACCESO A ZONA FEDERAL.", 72, 593, 447, 6);

        Line("ACEPTO", 60, 619, 162, 8, true, true);
        Line("PERMISIONARIO", 60, 630, 162, 8, true, true);
        Line("Vo.Bo.", 371, 619, 165, 8, true, true);
        Line("ENCARGADO DE LA SUBDIRECCIÓN", 371, 630, 165, 8, true, true);
        g.DrawLine(XPens.Black, 60, 698, 222, 698);
        g.DrawLine(XPens.Black, 371, 698, 536, 698);
        Line($"FOLIO {emission.Id:N}".ToUpperInvariant(), 60, 715, 476, 7, true, true);
        Line("Escanea el código y coteja la carátula con el original registrado.", 60, 728, 476, 7, center: true);
    }

    private static XImage AbrirRetrato(string path)
    {
        try { return XImage.FromFile(path); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("La fotografía no se pudo incorporar. Vuelve a cargarla como JPG o PNG RGB.", error);
        }
    }

    private sealed class Fuentes(string path) : IFontResolver
    {
        public byte[] GetFont(string faceName) =>
            File.ReadAllBytes(Path.Combine(path, faceName == "bold" ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf"));

        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "bold" : "regular");
    }
}
