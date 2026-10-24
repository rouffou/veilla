using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Affilies.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OccupationsProjetees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "occupation_locale",
                columns: table => new
                {
                    occupation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: true),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    fin_recue_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_locale", x => x.occupation_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_occupation_locale_personne_id",
                table: "occupation_locale",
                column: "personne_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occupation_locale");
        }
    }
}
