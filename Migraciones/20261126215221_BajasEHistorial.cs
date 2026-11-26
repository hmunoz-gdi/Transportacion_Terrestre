using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transportacion.Migraciones
{
    /// <inheritdoc />
    public partial class BajasEHistorial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Asignaciones_PermisionarioId",
                table: "Asignaciones");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Baja",
                table: "Asignaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Historial",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PermisionarioId = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    RegistroId = table.Column<long>(type: "bigint", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Usuario = table.Column<string>(type: "text", nullable: false),
                    Accion = table.Column<string>(type: "text", nullable: false),
                    Datos = table.Column<string>(type: "jsonb", nullable: true),
                    Foto = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Historial_Permisionarios_PermisionarioId",
                        column: x => x.PermisionarioId,
                        principalTable: "Permisionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Asignaciones_PermisionarioId_Tipo_Baja",
                table: "Asignaciones",
                columns: new[] { "PermisionarioId", "Tipo", "Baja" });

            migrationBuilder.CreateIndex(
                name: "IX_Historial_PermisionarioId_Tipo_RegistroId_Id",
                table: "Historial",
                columns: new[] { "PermisionarioId", "Tipo", "RegistroId", "Id" });

            migrationBuilder.Sql("""
                WITH fechas AS (
                    SELECT "Id", CASE
                        WHEN ("Detalles"->>'baja') ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                         AND pg_input_is_valid("Detalles"->>'baja', 'date')
                        THEN ("Detalles"->>'baja')::date END AS fecha
                    FROM "Asignaciones" WHERE "Tipo" = 'taxistas'
                )
                UPDATE "Asignaciones" AS a
                SET "Baja" = fechas.fecha::timestamp AT TIME ZONE 'America/Mexico_City'
                FROM fechas WHERE a."Id" = fechas."Id"
                  AND fechas.fecha <= (CURRENT_TIMESTAMP AT TIME ZONE 'America/Mexico_City')::date;

                INSERT INTO "Historial" ("PermisionarioId", "Tipo", "RegistroId", "Fecha", "Usuario", "Accion", "Datos")
                SELECT p."Id", 'permisionario', 0, CURRENT_TIMESTAMP, 'Sistema', 'Ficha al actualizar', to_jsonb(p)
                FROM "Permisionarios" p;

                INSERT INTO "Historial" ("PermisionarioId", "Tipo", "RegistroId", "Fecha", "Usuario", "Accion", "Datos")
                SELECT a."PermisionarioId", a."Tipo", a."Id", CURRENT_TIMESTAMP, 'Sistema', 'Ficha al actualizar', to_jsonb(a)
                FROM "Asignaciones" a;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Historial");

            migrationBuilder.DropIndex(
                name: "IX_Asignaciones_PermisionarioId_Tipo_Baja",
                table: "Asignaciones");

            migrationBuilder.DropColumn(
                name: "Baja",
                table: "Asignaciones");

            migrationBuilder.CreateIndex(
                name: "IX_Asignaciones_PermisionarioId",
                table: "Asignaciones",
                column: "PermisionarioId");
        }
    }
}
