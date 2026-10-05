using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Personnes.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_message",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => new { x.message_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personne",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    niss_chiffre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    niss_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prenom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_naissance = table.Column<DateOnly>(type: "date", nullable: false),
                    sexe = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    canal_prefere = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    adresse_boite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_code_postal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_localite = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    adresse_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_pays = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    adresse_rue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personne", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "etat_particulier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_chiffre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    affilie_declarant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etat_particulier", x => x.id);
                    table.ForeignKey(
                        name: "fk_etat_particulier_personne_personne_id",
                        column: x => x.personne_id,
                        principalTable: "personne",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "occupation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_utilisateur_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type_travailleur = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    type_contrat = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    reference_dimona = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation", x => x.id);
                    table.ForeignKey(
                        name: "fk_occupation_personne_personne_id",
                        column: x => x.personne_id,
                        principalTable: "personne",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "affectation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occupation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affectation", x => x.id);
                    table.ForeignKey(
                        name: "fk_affectation_occupation_occupation_id",
                        column: x => x.occupation_id,
                        principalTable: "occupation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affectation_occupation_id",
                table: "affectation",
                column: "occupation_id");

            migrationBuilder.CreateIndex(
                name: "ix_affectation_poste_id",
                table: "affectation",
                column: "poste_id");

            migrationBuilder.CreateIndex(
                name: "ix_etat_particulier_personne_id",
                table: "etat_particulier",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_affilie_id",
                table: "occupation",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_affilie_utilisateur_id",
                table: "occupation",
                column: "affilie_utilisateur_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_personne_id",
                table: "occupation",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_reference_dimona",
                table: "occupation",
                column: "reference_dimona",
                unique: true,
                filter: "reference_dimona IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_personne_niss_hash",
                table: "personne",
                column: "niss_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affectation");

            migrationBuilder.DropTable(
                name: "etat_particulier");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "occupation");

            migrationBuilder.DropTable(
                name: "personne");
        }
    }
}
