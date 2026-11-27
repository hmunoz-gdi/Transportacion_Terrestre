using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public sealed class RegistroDb(DbContextOptions<RegistroDb> options) : IdentityDbContext(options)
{
    public DbSet<Permisionario> Permisionarios => Set<Permisionario>();
    public DbSet<VersionPdf> Versiones => Set<VersionPdf>();
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();
    public DbSet<VersionFicha> Historial => Set<VersionFicha>();
    public DbSet<CaratulaEmitida> Caratulas => Set<CaratulaEmitida>();
    public DbSet<Evento> Eventos => Set<Evento>();

    public Task BloquearFicha(long id, CancellationToken token = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({id})", token);

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<CaratulaEmitida>().HasIndex(row => row.CodigoHash).IsUnique();
        model.Entity<CaratulaEmitida>().HasIndex(row => new { row.PermisionarioId, row.Fecha });
        model.Entity<CaratulaEmitida>().HasOne<Permisionario>().WithMany().HasForeignKey(row => row.PermisionarioId);
        model.Entity<CaratulaEmitida>().HasOne<Asignacion>().WithMany().HasForeignKey(row => row.UnidadId);

        model.Entity<VersionFicha>().Property(row => row.Datos).HasColumnType("jsonb");
        model.Entity<VersionFicha>().HasIndex(row => new { row.PermisionarioId, row.Tipo, row.RegistroId, row.Id });
        model.Entity<VersionFicha>().HasOne<Permisionario>().WithMany().HasForeignKey(row => row.PermisionarioId);
        model.Entity<Asignacion>().HasIndex(row => new { row.PermisionarioId, row.Tipo, row.Baja });
        model.Entity<Asignacion>().Property(row => row.Detalles).HasColumnType("jsonb").HasDefaultValue("{}");
        model.Entity<Asignacion>().HasOne<Permisionario>().WithMany().HasForeignKey(row => row.PermisionarioId);
        model.Entity<Permisionario>().HasIndex(row => row.Economico);
        model.Entity<VersionPdf>().HasIndex(row => new { row.PermisionarioId, row.Tipo, row.Numero }).IsUnique();
        model.Entity<VersionPdf>().HasOne<Permisionario>().WithMany().HasForeignKey(row => row.PermisionarioId);
    }
}

public sealed class Permisionario
{
    public long Id { get; set; }
    public string Economico { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Paterno { get; set; } = "";
    public string Materno { get; set; } = "";
    public string Agrupacion { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string Colonia { get; set; } = "";
    public string Cp { get; set; } = "";
    public string Calle { get; set; } = "";
    public string Rfc { get; set; } = "";
    public DateOnly? Nacimiento { get; set; }
    public DateOnly? Alta { get; set; }
    public string Permiso { get; set; } = "";
    public DateOnly? Vigencia { get; set; }
    public string Telefonos { get; set; } = "";
    public string Correos { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public DateTimeOffset? Baja { get; set; }
}

public sealed class VersionPdf
{
    public long Id { get; set; }
    public long PermisionarioId { get; set; }
    public string Tipo { get; set; } = "";
    public int Numero { get; set; }
    public Guid Clave { get; set; }
    public string Nombre { get; set; } = "";
    public long Bytes { get; set; }
    public string Usuario { get; set; } = "";
    public DateTimeOffset Fecha { get; set; }
}

public sealed class Evento
{
    public long Id { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "";
    public string Accion { get; set; } = "";
    public string Detalle { get; set; } = "";
}

public sealed class Asignacion
{
    public long Id { get; set; }
    public long PermisionarioId { get; set; }
    public string Tipo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Detalles { get; set; } = "{}";
    public DateOnly? Vigencia { get; set; }
    public DateTimeOffset? Baja { get; set; }
}

public sealed class VersionFicha
{
    public long Id { get; set; }
    public long PermisionarioId { get; set; }
    public string Tipo { get; set; } = "";
    public long RegistroId { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public string Usuario { get; set; } = "";
    public string Accion { get; set; } = "";
    public string? Datos { get; set; }
    public Guid? Foto { get; set; }
}


public sealed class CaratulaEmitida
{
    public Guid Id { get; set; }
    public long PermisionarioId { get; set; }
    public long? UnidadId { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Usuario { get; set; } = "";
    public string CodigoHash { get; set; } = "";
    public string PdfHash { get; set; } = "";
}
