using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DemandesCopieDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demande_copie_dossier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deposee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    date_limite = table.Column<DateOnly>(type: "date", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    traitee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    traitee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motif_refus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    publiee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demande_copie_dossier", x => x.id);
                    table.ForeignKey(
                        name: "fk_demande_copie_dossier_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_demande_copie_dossier_dossier_id",
                table: "demande_copie_dossier",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_demande_copie_dossier_personne_id",
                table: "demande_copie_dossier",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_demande_copie_dossier_statut",
                table: "demande_copie_dossier",
                column: "statut");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demande_copie_dossier");
        }
    }
}
