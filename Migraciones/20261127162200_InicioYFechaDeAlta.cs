using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transportacion.Migraciones
{
    /// <inheritdoc />
    public partial class InicioYFechaDeAlta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "Alta",
                table: "Permisionarios",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Permisionarios" AS p
                SET "Alta" = (h.fecha AT TIME ZONE 'America/Mexico_City')::date
                FROM (
                    SELECT "PermisionarioId", min("Fecha") AS fecha
                    FROM "Historial"
                    WHERE "Tipo" = 'permisionario' AND "RegistroId" = 0 AND "Accion" = 'Alta'
                    GROUP BY "PermisionarioId"
                ) AS h
                WHERE p."Id" = h."PermisionarioId" AND p."Alta" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Alta",
                table: "Permisionarios");
        }
    }
}
