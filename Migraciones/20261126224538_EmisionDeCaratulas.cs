using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transportacion.Migraciones
{
    /// <inheritdoc />
    public partial class EmisionDeCaratulas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Caratulas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PermisionarioId = table.Column<long>(type: "bigint", nullable: false),
                    UnidadId = table.Column<long>(type: "bigint", nullable: true),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Usuario = table.Column<string>(type: "text", nullable: false),
                    CodigoHash = table.Column<string>(type: "text", nullable: false),
                    PdfHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Caratulas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Caratulas_Asignaciones_UnidadId",
                        column: x => x.UnidadId,
                        principalTable: "Asignaciones",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Caratulas_Permisionarios_PermisionarioId",
                        column: x => x.PermisionarioId,
                        principalTable: "Permisionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Caratulas_CodigoHash",
                table: "Caratulas",
                column: "CodigoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Caratulas_PermisionarioId_Fecha",
                table: "Caratulas",
                columns: new[] { "PermisionarioId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Caratulas_UnidadId",
                table: "Caratulas",
                column: "UnidadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Caratulas");
        }
    }
}
