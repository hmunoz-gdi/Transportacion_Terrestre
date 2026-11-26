using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transportacion.Migraciones
{
    /// <inheritdoc />
    public partial class NacimientoPermisionario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "Nacimiento",
                table: "Permisionarios",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nacimiento",
                table: "Permisionarios");
        }
    }
}
