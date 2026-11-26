using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transportacion.Migraciones
{
    /// <inheritdoc />
    public partial class DetallesDeOperadoresYUnidades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detalles",
                table: "Asignaciones",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detalles",
                table: "Asignaciones");
        }
    }
}
