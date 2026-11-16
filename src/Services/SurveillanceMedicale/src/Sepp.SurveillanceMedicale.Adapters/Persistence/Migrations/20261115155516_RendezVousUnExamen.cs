using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RendezVousUnExamen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_examen_rendez_vous_id",
                table: "examen",
                column: "rendez_vous_id",
                unique: true,
                filter: "rendez_vous_id IS NOT NULL AND statut <> 'Annule'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_examen_rendez_vous_id",
                table: "examen");
        }
    }
}
