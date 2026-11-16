using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RendezVousProfessionnelConvoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "evenement_du",
                table: "rendez_vous_prevu",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professionnel_id",
                table: "rendez_vous_prevu",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evenement_du",
                table: "rendez_vous_prevu");

            migrationBuilder.DropColumn(
                name: "professionnel_id",
                table: "rendez_vous_prevu");
        }
    }
}
