using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetraitObligationsEtCalendrierLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retiree_le",
                table: "obligation_due",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statut_retrait",
                table: "obligation_due",
                type: "character varying(40)",
                maxLength: 40,
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
                name: "retiree_le",
                table: "obligation_due");

            migrationBuilder.DropColumn(
                name: "statut_retrait",
                table: "obligation_due");
        }
    }
}
