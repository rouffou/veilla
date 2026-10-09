using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Obligations.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessusReprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "annulee",
                table: "reprise_locale",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "reprise_id",
                table: "reprise_locale",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "absent",
                table: "rendez_vous_local",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "convocation_envoyee_le",
                table: "rendez_vous_local",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "convocation_non_remise_le",
                table: "rendez_vous_local",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motif_annulation",
                table: "rendez_vous_local",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replanifie_du",
                table: "rendez_vous_local",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "decision_recue",
                columns: table => new
                {
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recue_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decision_recue", x => x.examen_id);
                });

            migrationBuilder.CreateTable(
                name: "processus_reprise",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_reprise = table.Column<DateOnly>(type: "date", nullable: false),
                    debut_absence = table.Column<DateOnly>(type: "date", nullable: false),
                    origine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_limite = table.Column<DateOnly>(type: "date", nullable: true),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debut_rendez_vous = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rendez_vous_absents = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    convocation_rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    convocation_envoyee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    convocation_non_remise_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    urgence_non_couverte = table.Column<bool>(type: "boolean", nullable: false),
                    replanification_requise = table.Column<bool>(type: "boolean", nullable: false),
                    motif_replanification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    examen_le = table.Column<DateOnly>(type: "date", nullable: true),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    document_employeur_id = table.Column<Guid>(type: "uuid", nullable: true),
                    examen_non_requis = table.Column<bool>(type: "boolean", nullable: false),
                    sans_objet = table.Column<bool>(type: "boolean", nullable: false),
                    hors_delai = table.Column<bool>(type: "boolean", nullable: false),
                    annulee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motif_annulation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prochaine_echeance = table.Column<DateOnly>(type: "date", nullable: true),
                    type_minuterie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    minuteries_declenchees = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processus_reprise", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_affilie_id",
                table: "processus_reprise",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_decision_id",
                table: "processus_reprise",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_examen_id",
                table: "processus_reprise",
                column: "examen_id");

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_obligation_id",
                table: "processus_reprise",
                column: "obligation_id");

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_personne_id",
                table: "processus_reprise",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_processus_reprise_prochaine_echeance",
                table: "processus_reprise",
                column: "prochaine_echeance",
                filter: "prochaine_echeance IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_processus_reprise_actif",
                table: "processus_reprise",
                columns: new[] { "personne_id", "affilie_id", "date_reprise" },
                unique: true,
                filter: "annulee_le IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decision_recue");

            migrationBuilder.DropTable(
                name: "processus_reprise");

            migrationBuilder.DropColumn(
                name: "annulee",
                table: "reprise_locale");

            migrationBuilder.DropColumn(
                name: "reprise_id",
                table: "reprise_locale");

            migrationBuilder.DropColumn(
                name: "absent",
                table: "rendez_vous_local");

            migrationBuilder.DropColumn(
                name: "convocation_envoyee_le",
                table: "rendez_vous_local");

            migrationBuilder.DropColumn(
                name: "convocation_non_remise_le",
                table: "rendez_vous_local");

            migrationBuilder.DropColumn(
                name: "motif_annulation",
                table: "rendez_vous_local");

            migrationBuilder.DropColumn(
                name: "replanifie_du",
                table: "rendez_vous_local");
        }
    }
}
