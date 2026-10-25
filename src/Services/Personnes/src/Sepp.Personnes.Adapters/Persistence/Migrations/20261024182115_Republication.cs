using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Personnes.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Republication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "republication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    curseur = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    publies = table.Column<int>(type: "integer", nullable: false),
                    lots = table.Column<int>(type: "integer", nullable: false),
                    demandee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    demandee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erreur = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_republication", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_republication_demandee_le",
                table: "republication",
                column: "demandee_le");

            migrationBuilder.CreateIndex(
                name: "ix_republication_type",
                table: "republication",
                column: "type",
                unique: true,
                filter: "statut = 'EnCours'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "republication");
        }
    }
}
