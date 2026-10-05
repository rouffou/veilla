using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.PostesRisques.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "affectation_poste",
                columns: table => new
                {
                    affectation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affectation_poste", x => x.affectation_id);
                });

            migrationBuilder.CreateTable(
                name: "examen_realise",
                columns: table => new
                {
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_examen_realise", x => x.examen_id);
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
                name: "liste_nominative",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    date_reference = table.Column<DateOnly>(type: "date", nullable: false),
                    date_generation = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conserver_jusqu_au = table.Column<DateOnly>(type: "date", nullable: false),
                    proposition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version1 = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liste_nominative", x => x.id);
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
                name: "parametre_legal_local",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    valide_jusqu_au = table.Column<DateOnly>(type: "date", nullable: true),
                    valeur = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unite = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal_local", x => new { x.code, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "poste",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intitule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    metier_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poste", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    categorie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference_legale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("pk_risque", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ligne_liste_nominative",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codes_risques = table.Column<string[]>(type: "text[]", nullable: false),
                    date_derniere_evaluation = table.Column<DateOnly>(type: "date", nullable: true),
                    origine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    liste_nominative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_liste_nominative", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_liste_nominative_liste_nominative_liste_nominative_id",
                        column: x => x.liste_nominative_id,
                        principalTable: "liste_nominative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposition_liste_nominative",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liste_nominative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_liste = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version_liste = table.Column<int>(type: "integer", nullable: false),
                    origine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    proposee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_proposition = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motif = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decide_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_decision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motif_refus = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    liste_resultante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposition_liste_nominative", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposition_liste_nominative_liste_nominative_liste_nominat",
                        column: x => x.liste_nominative_id,
                        principalTable: "liste_nominative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposition_poste_risque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    proposee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_proposition = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motif = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_avis_cppt = table.Column<DateOnly>(type: "date", nullable: true),
                    document_avis_cppt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decide_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_decision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motif_refus = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposition_poste_risque", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposition_poste_risque_poste_poste_id",
                        column: x => x.poste_id,
                        principalTable: "poste",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "poste_risque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risque_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niveau_exposition = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valide_par_cpmt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_avis_cppt = table.Column<DateOnly>(type: "date", nullable: false),
                    document_avis_cppt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poste_risque", x => x.id);
                    table.ForeignKey(
                        name: "fk_poste_risque_poste_poste_id",
                        column: x => x.poste_id,
                        principalTable: "poste",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_poste_risque_risque_risque_id",
                        column: x => x.risque_id,
                        principalTable: "risque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regle_surveillance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    type_surveillance = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    frequence_mois = table.Column<int>(type: "integer", nullable: true),
                    actes_supplementaires = table.Column<string[]>(type: "text[]", nullable: false),
                    vaccins = table.Column<string[]>(type: "text[]", nullable: false),
                    surveillance_prolongee = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version1 = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regle_surveillance", x => x.id);
                    table.ForeignKey(
                        name: "fk_regle_surveillance_risque_risque_id",
                        column: x => x.risque_id,
                        principalTable: "risque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "surcharge_frequence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cible_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risque_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    frequence_mois = table.Column<int>(type: "integer", nullable: false),
                    motif = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    cpmt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surcharge_frequence", x => x.id);
                    table.ForeignKey(
                        name: "fk_surcharge_frequence_risque_risque_id",
                        column: x => x.risque_id,
                        principalTable: "risque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ligne_proposition_liste",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proposition_liste_nominative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_proposition_liste", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_proposition_liste_proposition_liste_nominative_propos",
                        column: x => x.proposition_liste_nominative_id,
                        principalTable: "proposition_liste_nominative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ligne_proposition_poste_risque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risque_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niveau_exposition = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proposition_poste_risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_proposition_poste_risque", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_proposition_poste_risque_proposition_poste_risque_pro",
                        column: x => x.proposition_poste_risque_id,
                        principalTable: "proposition_poste_risque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ligne_proposition_poste_risque_risque_risque_id",
                        column: x => x.risque_id,
                        principalTable: "risque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affectation_poste_personne_id",
                table: "affectation_poste",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_affectation_poste_poste_id",
                table: "affectation_poste",
                column: "poste_id");

            migrationBuilder.CreateIndex(
                name: "ix_examen_realise_affilie_id_personne_id_date",
                table: "examen_realise",
                columns: new[] { "affilie_id", "personne_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_ligne_liste_nominative_liste_nominative_id",
                table: "ligne_liste_nominative",
                column: "liste_nominative_id");

            migrationBuilder.CreateIndex(
                name: "ix_ligne_liste_nominative_personne_id",
                table: "ligne_liste_nominative",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_ligne_proposition_liste_proposition_liste_nominative_id",
                table: "ligne_proposition_liste",
                column: "proposition_liste_nominative_id");

            migrationBuilder.CreateIndex(
                name: "ix_ligne_proposition_poste_risque_proposition_poste_risque_id",
                table: "ligne_proposition_poste_risque",
                column: "proposition_poste_risque_id");

            migrationBuilder.CreateIndex(
                name: "ix_ligne_proposition_poste_risque_risque_id",
                table: "ligne_proposition_poste_risque",
                column: "risque_id");

            migrationBuilder.CreateIndex(
                name: "ix_liste_nominative_affilie_id_type_version",
                table: "liste_nominative",
                columns: new[] { "affilie_id", "type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_poste_affilie_id",
                table: "poste",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_poste_risque_poste_id_risque_id",
                table: "poste_risque",
                columns: new[] { "poste_id", "risque_id" });

            migrationBuilder.CreateIndex(
                name: "ix_poste_risque_risque_id",
                table: "poste_risque",
                column: "risque_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposition_liste_nominative_affilie_id_statut",
                table: "proposition_liste_nominative",
                columns: new[] { "affilie_id", "statut" });

            migrationBuilder.CreateIndex(
                name: "ix_proposition_liste_nominative_liste_nominative_id",
                table: "proposition_liste_nominative",
                column: "liste_nominative_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposition_poste_risque_affilie_id_statut",
                table: "proposition_poste_risque",
                columns: new[] { "affilie_id", "statut" });

            migrationBuilder.CreateIndex(
                name: "ix_proposition_poste_risque_poste_id",
                table: "proposition_poste_risque",
                column: "poste_id");

            migrationBuilder.CreateIndex(
                name: "ix_regle_surveillance_risque_id_version",
                table: "regle_surveillance",
                columns: new[] { "risque_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_risque_code",
                table: "risque",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_surcharge_frequence_affilie_id_cible_type_cible_id",
                table: "surcharge_frequence",
                columns: new[] { "affilie_id", "cible_type", "cible_id" });

            migrationBuilder.CreateIndex(
                name: "ix_surcharge_frequence_risque_id",
                table: "surcharge_frequence",
                column: "risque_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affectation_poste");

            migrationBuilder.DropTable(
                name: "examen_realise");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "ligne_liste_nominative");

            migrationBuilder.DropTable(
                name: "ligne_proposition_liste");

            migrationBuilder.DropTable(
                name: "ligne_proposition_poste_risque");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "parametre_legal_local");

            migrationBuilder.DropTable(
                name: "poste_risque");

            migrationBuilder.DropTable(
                name: "regle_surveillance");

            migrationBuilder.DropTable(
                name: "surcharge_frequence");

            migrationBuilder.DropTable(
                name: "proposition_liste_nominative");

            migrationBuilder.DropTable(
                name: "proposition_poste_risque");

            migrationBuilder.DropTable(
                name: "risque");

            migrationBuilder.DropTable(
                name: "liste_nominative");

            migrationBuilder.DropTable(
                name: "poste");
        }
    }
}
