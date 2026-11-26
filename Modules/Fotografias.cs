using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Fotografias
{
    private const int Limite = 5 * 1024 * 1024;

    public static string Panel(HttpContext http, string route, bool edit, long id, long? record = null, string? type = null)
    {
        var storage = http.RequestServices.GetRequiredService<RepositorioPdf>();
        var db = http.RequestServices.GetRequiredService<RegistroDb>();
        bool exists = Versiones(db, id, record, type).Any() || File.Exists(storage.RutaFoto(id, record));
        string image = exists ? $"<img class=record-photo src='{route}' alt='Fotografía de la ficha'>" : "<p>Sin fotografía.</p>";
        string form = edit ? $"""
            <form method=post enctype=multipart/form-data action='{route}'>{Vista.Csrf(http)}
            <label>Imagen JPG o PNG, hasta 5 MB<input type=file name=foto accept="image/jpeg,image/png" required></label>
            <div class=actions><button>{(exists ? "Reemplazar fotografía" : "Guardar fotografía")}</button></div></form>
            """ : "";
        return $"<section class=panel><h2>Fotografía</h2>{image}{form}</section>";
    }

    public static void Mapear(RouteGroupBuilder group, string module, string? type = null)
    {
        if (type is null)
        {
            group.MapGet("/{id:long}/foto", (long id, HttpContext http, RegistroDb db, RepositorioPdf store, long? version) =>
                Abrir(id, null, null, http, db, store, version));
            group.MapPost("/{id:long}/foto", (long id, HttpContext http, RegistroDb db, RepositorioPdf store) =>
                Subir(id, null, null, http, db, store)).RequireAuthorization(module + ".Cambiar");
            return;
        }

        group.MapGet("/{registro:long}/foto", (long id, long registro, HttpContext http, RegistroDb db, RepositorioPdf store, long? version) =>
            Abrir(id, registro, type, http, db, store, version));
        group.MapPost("/{registro:long}/foto", (long id, long registro, HttpContext http, RegistroDb db, RepositorioPdf store) =>
            Subir(id, registro, type, http, db, store)).RequireAuthorization(module + ".Cambiar");
    }

    private static async Task<bool> ExisteRegistro(RegistroDb db, long id, long? record, string? type) =>
        record is null || await db.Asignaciones.AnyAsync(row =>
            row.Id == record && row.PermisionarioId == id && row.Tipo == type);

    private static IQueryable<VersionFicha> Versiones(RegistroDb db, long id, long? record, string? type)
    {
        string kind = type ?? HistorialFichas.Titular;
        long recordId = record ?? 0;
        return db.Historial.Where(row => row.PermisionarioId == id && row.RegistroId == recordId &&
            row.Tipo == kind && row.Foto != null);
    }

    private static string? Formato(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        return null;
    }

    private static async Task<IResult> Abrir(long id, long? record, string? type, HttpContext http, RegistroDb db, RepositorioPdf store, long? version)
    {
        if (!await db.Permisionarios.AnyAsync(row => row.Id == id) || !await ExisteRegistro(db, id, record, type))
            return Results.NotFound();
        var key = await Versiones(db, id, record, type).Where(row => version == null || row.Id == version)
            .OrderByDescending(row => row.Foto != Guid.Empty).ThenByDescending(row => row.Id)
            .Select(row => row.Foto).FirstOrDefaultAsync();
        if (version is not null && key is null) return Results.NotFound();
        var path = store.RutaFoto(id, record, key.GetValueOrDefault());
        if (!File.Exists(path)) return Results.NotFound();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        byte[] header = new byte[8];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || Formato(header) is not string format)
        {
            await stream.DisposeAsync();
            return Results.NotFound();
        }
        stream.Position = 0;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        return Results.File(stream, format);
    }

    private static async Task<IResult> Subir(long id, long? record, string? type, HttpContext http, RegistroDb db, RepositorioPdf store)
    {
        string target = record is null ? $"/permisionarios/{id}" :
            $"/permisionarios/{id}/{(type == "taxistas" ? "operadores" : "unidades")}/{record}";
        IResult Invalid() => Vista.Pagina(http, "Fotografía rechazada",
            $"<p>Selecciona una imagen JPG o PNG de hasta 5 MB.</p><a href='{target}'>Volver a la ficha</a>", 400);
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var file = form.Files.GetFile("foto");
        if (file is null || file.Length is < 8 or > Limite) return Invalid();
        await using var source = file.OpenReadStream();
        byte[] header = new byte[8];
        await source.ReadExactlyAsync(header, http.RequestAborted);
        if (Formato(header) is null) return Invalid();
        source.Position = 0;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.BloquearFicha(id);
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null || !await ExisteRegistro(db, id, record, type)) return Results.NotFound();
        if (holder.Baja is not null) return Results.Conflict("El expediente está dado de baja.");
        if (record is not null && await db.Asignaciones.AnyAsync(row =>
            row.Id == record && row.PermisionarioId == id && row.Tipo == type && row.Baja != null))
            return Results.Conflict("El registro está dado de baja.");

        string kind = type ?? HistorialFichas.Titular;
        if (File.Exists(store.RutaFoto(id, record)) && !await Versiones(db, id, record, type).AnyAsync())
        {
            var baseline = HistorialFichas.Nueva(http, id, kind, record ?? 0, "Fotografía anterior a la actualización");
            baseline.Foto = Guid.Empty;
            db.Historial.Add(baseline);
        }
        var photo = HistorialFichas.Nueva(http, id, kind, record ?? 0, "Carga de fotografía");
        Guid key = Guid.NewGuid();
        photo.Foto = key;
        db.Historial.Add(photo);
        string destination = store.RutaFoto(id, record, key);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(output, http.RequestAborted);
                output.Flush(flushToDisk: true);
            }
            Acceso.Registrar(db, http, "Guardar fotografía", $"Ficha {id} · {type ?? "permisionario"} {record}");
            await db.SaveChangesAsync();
            File.Move(temporary, destination);
            await transaction.CommitAsync();
        }
        finally
        {
            File.Delete(temporary);
        }
        return Results.Redirect(target);
    }
}
