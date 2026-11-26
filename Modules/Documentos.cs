using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public sealed class RepositorioPdf
{
    private readonly string root;

    public RepositorioPdf(string configuredPath, string webRoot)
    {
        if (!Path.IsPathFullyQualified(configuredPath))
            throw new InvalidOperationException("Documentos:Raiz debe ser una ruta absoluta.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPath));
        string relative = Path.GetRelativePath(webRoot, root);
        if (!Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != "..")
            throw new InvalidOperationException("El repositorio debe estar fuera de wwwroot.");
        Directory.CreateDirectory(root);
    }

    public string Ruta(long id, Guid key) => Path.Combine(root, id.ToString(), $"{key:N}.pdf");

    public async Task Guardar(long id, Guid key, Stream source, CancellationToken cancellation)
    {
        string path = Ruta(id, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".part";
        try
        {
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await source.CopyToAsync(destination, cancellation);
                destination.Flush(flushToDisk: true);
            }
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

public static class Documentos
{
    private static readonly (string Code, string Label)[] Tipos =
    [
        ("PORTADA", "Portada"), ("PERMISO", "Permiso"), ("IDENTIFICACION", "Identificación"),
        ("RFC", "RFC"), ("CONS_FISCAL", "Constancia fiscal"), ("ACTA_NACIMIENTO", "Acta de nacimiento"),
        ("COMPROBANTE_DOM", "Comprobante de domicilio")
    ];
    private const int Limite = 25 * 1024 * 1024;

    public static void Mapear(WebApplication app)
    {
        var group = app.MapGroup("/permisionarios/{id:long}/documentos").RequireAuthorization("Documentos.Consultar");
        group.MapGet("", Panel);
        group.MapPost("", Subir).RequireAuthorization("Documentos.Cambiar");
        group.MapGet("/{version:long}", Abrir);
    }

    private static async Task<IResult> Panel(long id, HttpContext http, RegistroDb db, string? tipo)
    {
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        string selected = Tipos.Any(type => type.Code == tipo) ? tipo! : "PERMISO";
        string options = string.Concat(Tipos.Select(type =>
            $"<option value='{type.Code}' {(type.Code == selected ? "selected" : "")}>{type.Label}</option>"));
        var versions = await db.Versiones.Where(row => row.PermisionarioId == id && row.Tipo == selected)
            .OrderByDescending(row => row.Numero).ToListAsync();
        string Link(VersionPdf row) => $"""
            <a href="/permisionarios/{id}/documentos/{row.Id}" target="_blank" rel="noopener">{Vista.E(row.Nombre)}</a>
            <small>Versión {row.Numero} · {row.Fecha:yyyy-MM-dd HH:mm} UTC · {Vista.E(row.Usuario)}</small>
            """;
        string current = versions.Count == 0 ? "<p>Sin PDF cargado.</p>" : Link(versions[0]);
        string history = versions.Count < 2 ? "<p>Sin versiones anteriores.</p>" :
            "<ul>" + string.Concat(versions.Skip(1).Select(row => $"<li>{Link(row)}</li>")) + "</ul>";
        string upload = holder.Baja is null && Perfiles.Puede(http.User, "Documentos.Cambiar")
            ? $"""
                <form class=panel method=post enctype=multipart/form-data>{Vista.Csrf(http)}<h2>Cargar una versión</h2>
                <input type=hidden name=tipo value='{selected}'>
                <label>Archivo PDF (hasta 25 MB)<input type=file name=pdf accept="application/pdf,.pdf" required id=pdf-input></label>
                <iframe title="Vista previa local del PDF" id=pdf-preview hidden></iframe>
                <div class=actions><button>Guardar versión</button></div></form><script src=/pdf.js defer></script>
                """ : "";
        return Vista.Pagina(http, "Documentos", $"""
            <p><a href=/permisionarios/{id}>Ficha {Vista.E(holder.Economico)}</a></p>
            <form class=panel><div class=grid><label>Tipo de documento<select name=tipo>{options}</select></label></div>
            <div class=actions><button>Consultar</button></div></form>
            <section class=panel><h2>Versión actual</h2>{current}</section>{upload}
            <details class=panel><summary>Historial de versiones</summary>{history}</details>
            """);
    }

    private static async Task<IResult> Subir(long id, HttpContext http, RegistroDb db, RepositorioPdf storage)
    {
        var token = http.RequestAborted;
        var form = await http.Request.ReadFormAsync(token);
        string type = form["tipo"].ToString();
        var file = form.Files.GetFile("pdf");
        IResult Invalid() => Vista.Pagina(http, "Archivo rechazado",
            $"<p class=alert>Selecciona un PDF válido de hasta 25 MB.</p><a href='/permisionarios/{id}/documentos'>Volver a documentos</a>", 400);
        if (!Tipos.Any(item => item.Code == type) || file is null || file.Length < 5 ||
            file.Length > Limite || !Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Invalid();
        await using var source = file.OpenReadStream();
        byte[] magic = new byte[5];
        await source.ReadExactlyAsync(magic, token);
        if (!magic.AsSpan().SequenceEqual("%PDF-"u8)) return Invalid();
        source.Position = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.BloquearFicha(id, token);
        var holder = await db.Permisionarios.SingleOrDefaultAsync(row => row.Id == id, token);
        if (holder is null) return Results.NotFound();
        if (holder.Baja is not null) return Results.Conflict("El expediente está dado de baja.");
        int number = 1 + (await db.Versiones.Where(row => row.PermisionarioId == id && row.Tipo == type)
            .MaxAsync(row => (int?)row.Numero, token) ?? 0);

        Guid key = Guid.NewGuid();
        string name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (name.Length > 255 || name.Any(char.IsControl)) return Invalid();
        await storage.Guardar(id, key, source, token);
        db.Versiones.Add(new VersionPdf
        {
            PermisionarioId = id, Tipo = type, Numero = number, Clave = key,
            Nombre = name, Bytes = file.Length, Fecha = DateTimeOffset.UtcNow, Usuario = http.User.Identity!.Name!
        });
        Acceso.Registrar(db, http, "Carga de PDF", $"{id} · {type} · versión {number}");
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(CancellationToken.None);
        return Results.Redirect($"/permisionarios/{id}/documentos?tipo={type}");
    }

    private static async Task<IResult> Abrir(long id, long version, HttpContext http, RegistroDb db, RepositorioPdf storage)
    {
        var row = await db.Versiones.SingleOrDefaultAsync(item => item.Id == version && item.PermisionarioId == id);
        if (row is null) return Results.NotFound();
        string path = storage.Ruta(id, row.Clave);
        if (!File.Exists(path)) return Results.NotFound("El archivo no está disponible en el repositorio.");
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        return Results.File(path, contentType: "application/pdf", enableRangeProcessing: true);
    }
}
