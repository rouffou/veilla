using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Integrations.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "correspondance_identifiant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_externe = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valeur_externe = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type_interne = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    identifiant_interne = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correspondance_identifiant", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entreprise_bce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_bce = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    denomination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    forme_juridique = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code_nace = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    date_extraction = table.Column<DateOnly>(type: "date", nullable: false),
                    actualisee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entreprise_bce", x => x.id);
                });

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
                name: "journal_flux",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flux = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sens = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type_message = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cle_idempotence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reference_externe = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recu_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    traite_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    derniere_tentative_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre_enregistrements = table.Column<int>(type: "integer", nullable: false),
                    code_erreur = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message_erreur = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tentatives = table.Column<int>(type: "integer", nullable: false),
                    charge_utile_chiffree = table.Column<string>(type: "text", nullable: true),
                    charge_utile_purgee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_flux", x => x.id);
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
                name: "position_flux",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flux = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mise_a_jour_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_flux", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unite_etablissement_bce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    denomination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entreprise_bce_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    adresse_boite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_code_pays = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    adresse_code_postal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_localite = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    adresse_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_rue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unite_etablissement_bce", x => x.id);
                    table.ForeignKey(
                        name: "fk_unite_etablissement_bce_entreprise_bce_entreprise_bce_id",
                        column: x => x.entreprise_bce_id,
                        principalTable: "entreprise_bce",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correspondance_identifiant_type_externe_valeur_externe",
                table: "correspondance_identifiant",
                columns: new[] { "type_externe", "valeur_externe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_correspondance_identifiant_type_interne_identifiant_interne",
                table: "correspondance_identifiant",
                columns: new[] { "type_interne", "identifiant_interne" });

            migrationBuilder.CreateIndex(
                name: "ix_entreprise_bce_numero_bce",
                table: "entreprise_bce",
                column: "numero_bce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_flux_flux_cle_idempotence",
                table: "journal_flux",
                columns: new[] { "flux", "cle_idempotence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_flux_flux_statut_recu_le",
                table: "journal_flux",
                columns: new[] { "flux", "statut", "recu_le" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_flux_recu_le",
                table: "journal_flux",
                column: "recu_le");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_position_flux_flux",
                table: "position_flux",
                column: "flux",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unite_etablissement_bce_entreprise_bce_id",
                table: "unite_etablissement_bce",
                column: "entreprise_bce_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correspondance_identifiant");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "journal_flux");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "position_flux");

            migrationBuilder.DropTable(
                name: "unite_etablissement_bce");

            migrationBuilder.DropTable(
                name: "entreprise_bce");
        }
    }
}
