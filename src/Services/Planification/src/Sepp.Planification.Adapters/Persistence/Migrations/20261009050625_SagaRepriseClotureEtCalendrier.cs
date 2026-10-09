using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Planification.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SagaRepriseClotureEtCalendrier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cloturee",
                table: "obligation_a_planifier",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_cloture",
                table: "obligation_a_planifier",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statut_cloture",
                table: "obligation_a_planifier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "date_non_remise",
                table: "convocation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "calendrier_local",
                columns: table => new
                {
                    annee = table.Column<int>(type: "integer", nullable: false),
                    jours_supplementaires = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendrier_local", x => x.annee);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendrier_local");

            migrationBuilder.DropColumn(
                name: "cloturee",
                table: "obligation_a_planifier");

            migrationBuilder.DropColumn(
                name: "date_cloture",
                table: "obligation_a_planifier");

            migrationBuilder.DropColumn(
                name: "statut_cloture",
                table: "obligation_a_planifier");

            migrationBuilder.DropColumn(
                name: "date_non_remise",
                table: "convocation");
        }
    }
}
