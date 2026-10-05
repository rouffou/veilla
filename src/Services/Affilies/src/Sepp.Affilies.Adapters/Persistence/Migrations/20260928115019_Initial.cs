using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Affilies.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "groupe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groupe", x => x.id);
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
                name: "affilie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_bce = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    denomination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    forme_juridique = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code_nace = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    commission_paritaire = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    categorie_tarifaire = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    date_affiliation = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    regime_linguistique = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    groupe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affilie", x => x.id);
                    table.ForeignKey(
                        name: "fk_affilie_groupe_groupe_id",
                        column: x => x.groupe_id,
                        principalTable: "groupe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fonction = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_affilie_affilie_id",
                        column: x => x.affilie_id,
                        principalTable: "affilie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modification_affilie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auteur = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    horodatage = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    avant = table.Column<string>(type: "jsonb", nullable: true),
                    apres = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modification_affilie", x => x.id);
                    table.ForeignKey(
                        name: "fk_modification_affilie_affilie_affilie_id",
                        column: x => x.affilie_id,
                        principalTable: "affilie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operation_affilie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_effet = table.Column<DateOnly>(type: "date", nullable: false),
                    affilie_absorbant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    affilies_beneficiaires = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    sepp_contrepartie = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    date_realisation = table.Column<DateOnly>(type: "date", nullable: true),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_affilie", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_affilie_affilie_affilie_id",
                        column: x => x.affilie_id,
                        principalTable: "affilie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organe_concertation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organe_concertation", x => x.id);
                    table.ForeignKey(
                        name: "fk_organe_concertation_affilie_affilie_id",
                        column: x => x.affilie_id,
                        principalTable: "affilie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unite_etablissement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_ue_bce = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    adresse_boite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_code_pays = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    adresse_code_postal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_localite = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    adresse_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_rue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unite_etablissement", x => x.id);
                    table.ForeignKey(
                        name: "fk_unite_etablissement_affilie_affilie_id",
                        column: x => x.affilie_id,
                        principalTable: "affilie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reunion_concertation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_reunion = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    participation_sepp = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    organe_concertation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reunion_concertation", x => x.id);
                    table.ForeignKey(
                        name: "fk_reunion_concertation_organe_concertation_organe_concertatio",
                        column: x => x.organe_concertation_id,
                        principalTable: "organe_concertation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unite_etablissement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    adresse_boite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresse_code_pays = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    adresse_code_postal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_localite = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    adresse_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adresse_rue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_unite_etablissement_unite_etablissement_id",
                        column: x => x.unite_etablissement_id,
                        principalTable: "unite_etablissement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "departement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departement", x => x.id);
                    table.ForeignKey(
                        name: "fk_departement_departement_parent_id",
                        column: x => x.parent_id,
                        principalTable: "departement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departement_site_site_id",
                        column: x => x.site_id,
                        principalTable: "site",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affilie_denomination",
                table: "affilie",
                column: "denomination");

            migrationBuilder.CreateIndex(
                name: "ix_affilie_groupe_id",
                table: "affilie",
                column: "groupe_id");

            migrationBuilder.CreateIndex(
                name: "ix_affilie_numero_bce",
                table: "affilie",
                column: "numero_bce",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contact_affilie_id",
                table: "contact",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_departement_parent_id",
                table: "departement",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_departement_site_id",
                table: "departement",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_modification_affilie_affilie_id_numero_version",
                table: "modification_affilie",
                columns: new[] { "affilie_id", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operation_affilie_affilie_absorbant_id",
                table: "operation_affilie",
                column: "affilie_absorbant_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_affilie_affilie_id",
                table: "operation_affilie",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_organe_concertation_affilie_id",
                table: "organe_concertation",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reunion_concertation_organe_concertation_id",
                table: "reunion_concertation",
                column: "organe_concertation_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_unite_etablissement_id",
                table: "site",
                column: "unite_etablissement_id");

            migrationBuilder.CreateIndex(
                name: "ix_unite_etablissement_affilie_id",
                table: "unite_etablissement",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_unite_etablissement_numero_ue_bce",
                table: "unite_etablissement",
                column: "numero_ue_bce",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact");

            migrationBuilder.DropTable(
                name: "departement");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "modification_affilie");

            migrationBuilder.DropTable(
                name: "operation_affilie");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "reunion_concertation");

            migrationBuilder.DropTable(
                name: "site");

            migrationBuilder.DropTable(
                name: "organe_concertation");

            migrationBuilder.DropTable(
                name: "unite_etablissement");

            migrationBuilder.DropTable(
                name: "affilie");

            migrationBuilder.DropTable(
                name: "groupe");
        }
    }
}
