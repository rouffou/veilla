using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Referentiels.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendrier_annuel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    annee = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendrier_annuel", x => x.id);
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
                name: "nomenclature",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    libelle_de = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    libelle_fr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_nl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nomenclature", x => x.id);
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
                name: "parametre_legal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unite = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    base_legale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    libelle_de = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    libelle_fr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_nl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jour_ferie_supplementaire",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    calendrier_annuel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    libelle_de = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    libelle_fr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_nl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jour_ferie_supplementaire", x => x.id);
                    table.ForeignKey(
                        name: "fk_jour_ferie_supplementaire_calendrier_annuel_calendrier_annu",
                        column: x => x.calendrier_annuel_id,
                        principalTable: "calendrier_annuel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entree_nomenclature",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code_parent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    libelle_de = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    libelle_fr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    libelle_nl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entree_nomenclature", x => x.id);
                    table.ForeignKey(
                        name: "fk_entree_nomenclature_nomenclature_nomenclature_id",
                        column: x => x.nomenclature_id,
                        principalTable: "nomenclature",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "valeur_parametre",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valeur = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parametre_legal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valeur_parametre", x => x.id);
                    table.ForeignKey(
                        name: "fk_valeur_parametre_parametre_legal_parametre_legal_id",
                        column: x => x.parametre_legal_id,
                        principalTable: "parametre_legal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendrier_annuel_annee",
                table: "calendrier_annuel",
                column: "annee",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entree_nomenclature_nomenclature_id_code",
                table: "entree_nomenclature",
                columns: new[] { "nomenclature_id", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_jour_ferie_supplementaire_calendrier_annuel_id",
                table: "jour_ferie_supplementaire",
                column: "calendrier_annuel_id");

            migrationBuilder.CreateIndex(
                name: "ix_nomenclature_code",
                table: "nomenclature",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parametre_legal_code",
                table: "parametre_legal",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_valeur_parametre_parametre_legal_id",
                table: "valeur_parametre",
                column: "parametre_legal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entree_nomenclature");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "jour_ferie_supplementaire");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "valeur_parametre");

            migrationBuilder.DropTable(
                name: "nomenclature");

            migrationBuilder.DropTable(
                name: "calendrier_annuel");

            migrationBuilder.DropTable(
                name: "parametre_legal");
        }
    }
}
