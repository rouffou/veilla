using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Audit.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParametreLegalLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parametre_legal_local",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    valide_jusqu_au = table.Column<DateOnly>(type: "date", nullable: true),
                    valeur = table.Column<decimal>(type: "numeric", nullable: false),
                    unite = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal_local", x => new { x.code, x.valide_du });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parametre_legal_local");
        }
    }
}
