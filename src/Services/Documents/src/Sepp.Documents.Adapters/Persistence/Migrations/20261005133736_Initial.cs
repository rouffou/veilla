using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Documents.Adapters.Persistence.Migrations
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
                name: "modele",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    contenu = table.Column<string>(type: "text", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valide_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    valide_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publie_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    publie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version1 = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modele", x => x.id);
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
                name: "champ_modele",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type_champ = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    obligatoire = table.Column<bool>(type: "boolean", nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modele_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_champ_modele", x => x.id);
                    table.ForeignKey(
                        name: "fk_champ_modele_modele_modele_id",
                        column: x => x.modele_id,
                        principalTable: "modele",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modele_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_modele = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_modele = table.Column<int>(type: "integer", nullable: false),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    motif_langue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    service_proprietaire = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    objet_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    objet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_destinataire = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destinataire_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exemplaire = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cle_idempotence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    stockage_uri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    empreinte = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    empreinte_stockage = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    taille = table.Column<long>(type: "bigint", nullable: false),
                    cle_chiffrement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    autorite_horodatage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    jeton_horodatage = table.Column<string>(type: "text", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    publie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_modele_modele_id",
                        column: x => x.modele_id,
                        principalTable: "modele",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "signature",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    signataire_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    preuve = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    empreinte_signee = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signature", x => x.id);
                    table.ForeignKey(
                        name: "fk_signature_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_champ_modele_modele_id_nom",
                table: "champ_modele",
                columns: new[] { "modele_id", "nom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_cle_idempotence",
                table: "document",
                column: "cle_idempotence",
                unique: true,
                filter: "cle_idempotence IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_modele_id",
                table: "document",
                column: "modele_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_objet_type_objet_id",
                table: "document",
                columns: new[] { "objet_type", "objet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_type_destinataire_destinataire_id",
                table: "document",
                columns: new[] { "type_destinataire", "destinataire_id" });

            migrationBuilder.CreateIndex(
                name: "ix_modele_code_langue_version",
                table: "modele",
                columns: new[] { "code", "langue", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_signature_document_id_signataire_id",
                table: "signature",
                columns: new[] { "document_id", "signataire_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champ_modele");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "signature");

            migrationBuilder.DropTable(
                name: "document");

            migrationBuilder.DropTable(
                name: "modele");
        }
    }
}
