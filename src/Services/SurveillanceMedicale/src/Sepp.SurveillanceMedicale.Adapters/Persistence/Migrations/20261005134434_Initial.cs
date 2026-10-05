using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "affectation_personne",
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
                    table.PrimaryKey("pk_affectation_personne", x => x.affectation_id);
                });

            migrationBuilder.CreateTable(
                name: "dossier_sante",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestionnaire_cpmt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_ouverture = table.Column<DateOnly>(type: "date", nullable: false),
                    statut_archivage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_archivage = table.Column<DateOnly>(type: "date", nullable: true),
                    date_purge_prevue = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dossier_sante", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duree_conservation_exposition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_agent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    annees = table.Column<int>(type: "integer", nullable: false),
                    base_legale = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duree_conservation_exposition", x => x.id);
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
                name: "lot_vaccin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    centre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vaccin_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    numero_lot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    peremption = table.Column<DateOnly>(type: "date", nullable: false),
                    quantite_initiale = table.Column<int>(type: "integer", nullable: false),
                    quantite_restante = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lot_vaccin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mesurage_exposition",
                columns: table => new
                {
                    mesurage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    groupe_exposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    niveau = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mesurage_exposition", x => x.mesurage_id);
                });

            migrationBuilder.CreateTable(
                name: "modele_questionnaire",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    questions = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version1 = table.Column<int>(type: "integer", nullable: false),
                    titre_de = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    titre_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    titre_fr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    titre_nl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modele_questionnaire", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "modele_texte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cpmt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    rubrique = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    texte = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modele_texte", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obligation_due",
                columns: table => new
                {
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    date_due = table.Column<DateOnly>(type: "date", nullable: false),
                    date_limite = table.Column<DateOnly>(type: "date", nullable: true),
                    satisfaite_par_examen_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obligation_due", x => x.obligation_id);
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
                name: "preuve_destruction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_purge_prevue = table.Column<DateOnly>(type: "date", nullable: false),
                    detruit_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    motif_validation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    nombre_elements = table.Column<int>(type: "integer", nullable: false),
                    empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preuve_destruction", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profil_risque_poste",
                columns: table => new
                {
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codes_risques = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profil_risque_poste", x => new { x.poste_id, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "rendez_vous_prevu",
                columns: table => new
                {
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    obligation_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rendez_vous_prevu", x => x.rendez_vous_id);
                });

            migrationBuilder.CreateTable(
                name: "schema_vaccinal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vaccin_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    codes_risques = table.Column<string[]>(type: "text[]", nullable: false),
                    nombre_doses = table.Column<int>(type: "integer", nullable: false),
                    intervalles_mois = table.Column<int[]>(type: "integer[]", nullable: false),
                    rappel_mois = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_schema_vaccinal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "valeur_reference",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code_mesure = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minimum = table.Column<decimal>(type: "numeric", nullable: true),
                    maximum = table.Column<decimal>(type: "numeric", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_valeur_reference", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "declaration_mp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    auteur_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contenu_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    reference_fedris = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_envoi = table.Column<DateOnly>(type: "date", nullable: true),
                    date_issue = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_mp", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaration_mp_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "examen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    professionnel_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    obligation_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_cloture = table.Column<DateOnly>(type: "date", nullable: true),
                    hors_delai_legal = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_examen", x => x.id);
                    table.ForeignKey(
                        name: "fk_examen_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exposition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    niveau = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    periode_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    periode_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    mesurage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    groupe_exposition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exposition", x => x.id);
                    table.ForeignKey(
                        name: "fk_exposition_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "piece_jointe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categorie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titre_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    description_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    date_document = table.Column<DateOnly>(type: "date", nullable: false),
                    ajoutee_le = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_piece_jointe", x => x.id);
                    table.ForeignKey(
                        name: "fk_piece_jointe_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_rempli",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modele_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    modele_version = table.Column<int>(type: "integer", nullable: false),
                    reponses_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rempli_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_rempli", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_rempli_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rattachement_groupe_exposition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    groupe_exposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rattachement_groupe_exposition", x => x.id);
                    table.ForeignKey(
                        name: "fk_rattachement_groupe_exposition_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_tuberculinique",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_pose = table.Column<DateOnly>(type: "date", nullable: false),
                    date_lecture = table.Column<DateOnly>(type: "date", nullable: true),
                    lecture_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    realise_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_tuberculinique", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_tuberculinique_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transfert_dossier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contrepartie = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    motif = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_demande = table.Column<DateOnly>(type: "date", nullable: false),
                    demande_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paquet_chiffre = table.Column<string>(type: "text", nullable: true),
                    empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reference_canal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    date_transmission = table.Column<DateOnly>(type: "date", nullable: true),
                    date_integration = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfert_dossier", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfert_dossier_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vaccination",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vaccin_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dose = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    centre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    administre_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    remarque_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    enregistre_registre = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vaccination", x => x.id);
                    table.ForeignKey(
                        name: "fk_vaccination_dossier_sante_dossier_id",
                        column: x => x.dossier_id,
                        principalTable: "dossier_sante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vaccination_lots_vaccins_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lot_vaccin",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demande_information_fedris",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_demande = table.Column<DateOnly>(type: "date", nullable: false),
                    echeance = table.Column<DateOnly>(type: "date", nullable: true),
                    objet_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    date_reponse = table.Column<DateOnly>(type: "date", nullable: true),
                    reponse_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    declaration_mp_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demande_information_fedris", x => x.id);
                    table.ForeignKey(
                        name: "fk_demande_information_fedris_declaration_mp_declaration_mp_id",
                        column: x => x.declaration_mp_id,
                        principalTable: "declaration_mp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    date_examen = table.Column<DateOnly>(type: "date", nullable: false),
                    categorie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mesures = table.Column<string[]>(type: "text[]", nullable: false),
                    valide_jusqu_au = table.Column<DateOnly>(type: "date", nullable: true),
                    justification_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    recommandations_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    auteur_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    signee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reference_signature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decision", x => x.id);
                    table.ForeignKey(
                        name: "fk_decision_examen_examen_id",
                        column: x => x.examen_id,
                        principalTable: "examen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observation_clinique",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anamnese_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    examen_clinique_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    saisie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observation_clinique", x => x.id);
                    table.ForeignKey(
                        name: "fk_observation_clinique_examen_examen_id",
                        column: x => x.examen_id,
                        principalTable: "examen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposition_frequence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultat_acte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_proposition = table.Column<DateOnly>(type: "date", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decide_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_decision = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposition_frequence", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposition_frequence_examen_examen_id",
                        column: x => x.examen_id,
                        principalTable: "examen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resultat_acte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valeurs_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    commentaire_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    inhabituel = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resultat_acte", x => x.id);
                    table.ForeignKey(
                        name: "fk_resultat_acte_examen_examen_id",
                        column: x => x.examen_id,
                        principalTable: "examen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_introduction = table.Column<DateOnly>(type: "date", nullable: false),
                    date_limite_introduction = table.Column<DateOnly>(type: "date", nullable: false),
                    introduit_dans_le_delai = table.Column<bool>(type: "boolean", nullable: false),
                    date_limite_issue = table.Column<DateOnly>(type: "date", nullable: false),
                    issue = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    date_issue = table.Column<DateOnly>(type: "date", nullable: true),
                    commentaire_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recours", x => x.id);
                    table.ForeignKey(
                        name: "fk_recours_decision_decision_id",
                        column: x => x.decision_id,
                        principalTable: "decision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affectation_personne_personne_id",
                table: "affectation_personne",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_decision_dossier_id",
                table: "decision",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_decision_examen_id",
                table: "decision",
                column: "examen_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_decision_personne_id",
                table: "decision",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_mp_dossier_id",
                table: "declaration_mp",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_demande_information_fedris_declaration_mp_id",
                table: "demande_information_fedris",
                column: "declaration_mp_id");

            migrationBuilder.CreateIndex(
                name: "ix_dossier_sante_personne_id",
                table: "dossier_sante",
                column: "personne_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dossier_sante_statut_archivage",
                table: "dossier_sante",
                column: "statut_archivage");

            migrationBuilder.CreateIndex(
                name: "ix_duree_conservation_exposition_code_agent",
                table: "duree_conservation_exposition",
                column: "code_agent",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_examen_dossier_id_professionnel_id",
                table: "examen",
                columns: new[] { "dossier_id", "professionnel_id" });

            migrationBuilder.CreateIndex(
                name: "ix_examen_personne_id",
                table: "examen",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_exposition_dossier_id_mesurage_id",
                table: "exposition",
                columns: new[] { "dossier_id", "mesurage_id" },
                unique: true,
                filter: "mesurage_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lot_vaccin_centre_id_vaccin_code_numero_lot",
                table: "lot_vaccin",
                columns: new[] { "centre_id", "vaccin_code", "numero_lot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mesurage_exposition_groupe_exposition_id",
                table: "mesurage_exposition",
                column: "groupe_exposition_id");

            migrationBuilder.CreateIndex(
                name: "ix_modele_questionnaire_code_version",
                table: "modele_questionnaire",
                columns: new[] { "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_modele_texte_cpmt_id_code",
                table: "modele_texte",
                columns: new[] { "cpmt_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_due_personne_id",
                table: "obligation_due",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_observation_clinique_examen_id",
                table: "observation_clinique",
                column: "examen_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_piece_jointe_dossier_id",
                table: "piece_jointe",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_preuve_destruction_dossier_id",
                table: "preuve_destruction",
                column: "dossier_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proposition_frequence_examen_id",
                table: "proposition_frequence",
                column: "examen_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_rempli_dossier_id",
                table: "questionnaire_rempli",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rattachement_groupe_exposition_dossier_id",
                table: "rattachement_groupe_exposition",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rattachement_groupe_exposition_groupe_exposition_id",
                table: "rattachement_groupe_exposition",
                column: "groupe_exposition_id");

            migrationBuilder.CreateIndex(
                name: "ix_recours_decision_id",
                table: "recours",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_prevu_personne_id",
                table: "rendez_vous_prevu",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_resultat_acte_examen_id",
                table: "resultat_acte",
                column: "examen_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_tuberculinique_dossier_id",
                table: "test_tuberculinique",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfert_dossier_dossier_id",
                table: "transfert_dossier",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_vaccination_dossier_id",
                table: "vaccination",
                column: "dossier_id");

            migrationBuilder.CreateIndex(
                name: "ix_vaccination_lot_id",
                table: "vaccination",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_valeur_reference_type_acte_code_mesure",
                table: "valeur_reference",
                columns: new[] { "type_acte", "code_mesure" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affectation_personne");

            migrationBuilder.DropTable(
                name: "demande_information_fedris");

            migrationBuilder.DropTable(
                name: "duree_conservation_exposition");

            migrationBuilder.DropTable(
                name: "exposition");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "mesurage_exposition");

            migrationBuilder.DropTable(
                name: "modele_questionnaire");

            migrationBuilder.DropTable(
                name: "modele_texte");

            migrationBuilder.DropTable(
                name: "obligation_due");

            migrationBuilder.DropTable(
                name: "observation_clinique");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "parametre_legal_local");

            migrationBuilder.DropTable(
                name: "piece_jointe");

            migrationBuilder.DropTable(
                name: "preuve_destruction");

            migrationBuilder.DropTable(
                name: "profil_risque_poste");

            migrationBuilder.DropTable(
                name: "proposition_frequence");

            migrationBuilder.DropTable(
                name: "questionnaire_rempli");

            migrationBuilder.DropTable(
                name: "rattachement_groupe_exposition");

            migrationBuilder.DropTable(
                name: "recours");

            migrationBuilder.DropTable(
                name: "rendez_vous_prevu");

            migrationBuilder.DropTable(
                name: "resultat_acte");

            migrationBuilder.DropTable(
                name: "schema_vaccinal");

            migrationBuilder.DropTable(
                name: "test_tuberculinique");

            migrationBuilder.DropTable(
                name: "transfert_dossier");

            migrationBuilder.DropTable(
                name: "vaccination");

            migrationBuilder.DropTable(
                name: "valeur_reference");

            migrationBuilder.DropTable(
                name: "declaration_mp");

            migrationBuilder.DropTable(
                name: "decision");

            migrationBuilder.DropTable(
                name: "lot_vaccin");

            migrationBuilder.DropTable(
                name: "examen");

            migrationBuilder.DropTable(
                name: "dossier_sante");
        }
    }
}
