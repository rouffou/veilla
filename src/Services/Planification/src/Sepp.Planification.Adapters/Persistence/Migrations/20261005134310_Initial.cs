using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Planification.Adapters.Persistence.Migrations
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
                name: "lieu",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    adresse = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    code_postal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actif = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lieu", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obligation_a_planifier",
                columns: table => new
                {
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_due = table.Column<DateOnly>(type: "date", nullable: false),
                    date_limite = table.Column<DateOnly>(type: "date", nullable: true),
                    echue = table.Column<bool>(type: "boolean", nullable: false),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    urgence_non_couverte = table.Column<bool>(type: "boolean", nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obligation_a_planifier", x => x.obligation_id);
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
                    unite = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal_local", x => new { x.code, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "preference_convocation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preference_convocation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ressource",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    competences = table.Column<string[]>(type: "text[]", nullable: false),
                    lieu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    fournisseur_agenda = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    compte_agenda = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ressource", x => x.id);
                    table.ForeignKey(
                        name: "fk_ressource_lieu_lieu_id",
                        column: x => x.lieu_id,
                        principalTable: "lieu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "absence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_externe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence", x => x.id);
                    table.ForeignKey(
                        name: "fk_absence_ressource_ressource_id",
                        column: x => x.ressource_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duree_standard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duree_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duree_standard", x => x.id);
                    table.ForeignKey(
                        name: "fk_duree_standard_ressource_ressource_id",
                        column: x => x.ressource_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "modele_agenda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lieu_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_modele_agenda", x => x.id);
                    table.ForeignKey(
                        name: "fk_modele_agenda_lieu_lieu_id",
                        column: x => x.lieu_id,
                        principalTable: "lieu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_modele_agenda_ressource_ressource_id",
                        column: x => x.ressource_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lieu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    conseiller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unite_mobile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chauffeur_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capacite_journaliere = table.Column<int>(type: "integer", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    heure_debut = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duree_minutes = table.Column<int>(type: "integer", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_lieu_lieu_id",
                        column: x => x.lieu_id,
                        principalTable: "lieu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_ressource_conseiller_id",
                        column: x => x.conseiller_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plage_modele",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    jour = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    debut = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    duree_minutes = table.Column<int>(type: "integer", nullable: false),
                    reserve_urgence = table.Column<bool>(type: "boolean", nullable: false),
                    ouvert_en_ligne = table.Column<bool>(type: "boolean", nullable: false),
                    ressources_associees = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modele_agenda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plage_modele", x => x.id);
                    table.ForeignKey(
                        name: "fk_plage_modele_modele_agenda_modele_agenda_id",
                        column: x => x.modele_agenda_id,
                        principalTable: "modele_agenda",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "creneau",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lieu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reserve_urgence = table.Column<bool>(type: "boolean", nullable: false),
                    ouvert_en_ligne = table.Column<bool>(type: "boolean", nullable: false),
                    modele_agenda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_creneau", x => x.id);
                    table.ForeignKey(
                        name: "fk_creneau_lieu_lieu_id",
                        column: x => x.lieu_id,
                        principalTable: "lieu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_creneau_modele_agenda_modele_agenda_id",
                        column: x => x.modele_agenda_id,
                        principalTable: "modele_agenda",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_creneau_ressource_ressource_id",
                        column: x => x.ressource_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_creneau_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "etape_tournee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordre = table.Column<int>(type: "integer", nullable: false),
                    emplacement = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    heure_arrivee = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    raccordement_electrique = table.Column<bool>(type: "boolean", nullable: false),
                    raccordement_eau = table.Column<bool>(type: "boolean", nullable: false),
                    raccordement_reseau = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etape_tournee", x => x.id);
                    table.ForeignKey(
                        name: "fk_etape_tournee_session_session_id",
                        column: x => x.session_id,
                        principalTable: "session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "occupation_ressource",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    creneau_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_ressource", x => x.id);
                    table.ForeignKey(
                        name: "fk_occupation_ressource_creneau_creneau_id",
                        column: x => x.creneau_id,
                        principalTable: "creneau",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_occupation_ressource_ressource_ressource_id",
                        column: x => x.ressource_id,
                        principalTable: "ressource",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rendez_vous",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    creneau_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ressource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lieu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type_acte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motif_annulation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    obligation_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    origine = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    urgent = table.Column<bool>(type: "boolean", nullable: false),
                    planifie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrivee_a = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appele_a = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    salle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rappel1emis_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rappel2emis_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reconvocation_de_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_agenda_externe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rendez_vous", x => x.id);
                    table.ForeignKey(
                        name: "fk_rendez_vous_creneau_creneau_id",
                        column: x => x.creneau_id,
                        principalTable: "creneau",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "convocation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recommande = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_emission = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    date_envoi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convocation", x => x.id);
                    table.ForeignKey(
                        name: "fk_convocation_rendez_vous_rendez_vous_id",
                        column: x => x.rendez_vous_id,
                        principalTable: "rendez_vous",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absence_ressource_id_debut",
                table: "absence",
                columns: new[] { "ressource_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_absence_source_reference_externe",
                table: "absence",
                columns: new[] { "source", "reference_externe" },
                unique: true,
                filter: "reference_externe IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_convocation_lot_id",
                table: "convocation",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_convocation_rendez_vous_id",
                table: "convocation",
                column: "rendez_vous_id");

            migrationBuilder.CreateIndex(
                name: "ix_creneau_lieu_id",
                table: "creneau",
                column: "lieu_id");

            migrationBuilder.CreateIndex(
                name: "ix_creneau_modele_agenda_id",
                table: "creneau",
                column: "modele_agenda_id");

            migrationBuilder.CreateIndex(
                name: "ix_creneau_ressource_id_debut",
                table: "creneau",
                columns: new[] { "ressource_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_creneau_session_id",
                table: "creneau",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_creneau_statut_debut",
                table: "creneau",
                columns: new[] { "statut", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_duree_standard_ressource_id",
                table: "duree_standard",
                column: "ressource_id");

            migrationBuilder.CreateIndex(
                name: "ux_duree_standard_globale",
                table: "duree_standard",
                column: "type_acte",
                unique: true,
                filter: "ressource_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_duree_standard_ressource",
                table: "duree_standard",
                columns: new[] { "type_acte", "ressource_id" },
                unique: true,
                filter: "ressource_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_etape_tournee_session_id",
                table: "etape_tournee",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_lieu_affilie_id",
                table: "lieu",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_modele_agenda_lieu_id",
                table: "modele_agenda",
                column: "lieu_id");

            migrationBuilder.CreateIndex(
                name: "ix_modele_agenda_ressource_id",
                table: "modele_agenda",
                column: "ressource_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_a_planifier_affilie_id_date_due",
                table: "obligation_a_planifier",
                columns: new[] { "affilie_id", "date_due" });

            migrationBuilder.CreateIndex(
                name: "ix_obligation_a_planifier_personne_id",
                table: "obligation_a_planifier",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_a_planifier_rendez_vous_id",
                table: "obligation_a_planifier",
                column: "rendez_vous_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_ressource_creneau_id",
                table: "occupation_ressource",
                column: "creneau_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_ressource_ressource_id_debut",
                table: "occupation_ressource",
                columns: new[] { "ressource_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plage_modele_modele_agenda_id",
                table: "plage_modele",
                column: "modele_agenda_id");

            migrationBuilder.CreateIndex(
                name: "ix_preference_convocation_affilie_id",
                table: "preference_convocation",
                column: "affilie_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_affilie_id_debut",
                table: "rendez_vous",
                columns: new[] { "affilie_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_lieu_id_debut",
                table: "rendez_vous",
                columns: new[] { "lieu_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_personne_id_debut",
                table: "rendez_vous",
                columns: new[] { "personne_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_ressource_id_debut",
                table: "rendez_vous",
                columns: new[] { "ressource_id", "debut" });

            migrationBuilder.CreateIndex(
                name: "ux_rendez_vous_creneau_actif",
                table: "rendez_vous",
                column: "creneau_id",
                unique: true,
                filter: "statut IN ('Planifie', 'Arrive', 'EnSalle')");

            migrationBuilder.CreateIndex(
                name: "ix_ressource_lieu_id",
                table: "ressource",
                column: "lieu_id");

            migrationBuilder.CreateIndex(
                name: "ix_ressource_reference_id",
                table: "ressource",
                column: "reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_conseiller_id",
                table: "session",
                column: "conseiller_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_date",
                table: "session",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_session_lieu_id",
                table: "session",
                column: "lieu_id");

            // Pas de double réservation d'une ressource (PLA-01, PLA-05) : EF ne sait pas exprimer une contrainte d'exclusion.
            // Deux occupations de la même ressource ne peuvent pas avoir de périodes qui se chevauchent ([debut, fin[).
            // btree_gist est une extension de confiance (PostgreSQL 13 et plus) : le propriétaire de la base peut l'installer ;
            // sur Azure Database for PostgreSQL, elle doit être autorisée (azure.extensions).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE occupation_ressource
                    ADD CONSTRAINT ex_occupation_ressource_chevauchement
                    EXCLUDE USING gist (ressource_id WITH =, tstzrange(debut, fin) WITH &&);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE occupation_ressource
                    ADD CONSTRAINT ck_occupation_ressource_periode CHECK (fin > debut);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence");

            migrationBuilder.DropTable(
                name: "convocation");

            migrationBuilder.DropTable(
                name: "duree_standard");

            migrationBuilder.DropTable(
                name: "etape_tournee");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "obligation_a_planifier");

            migrationBuilder.DropTable(
                name: "occupation_ressource");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "parametre_legal_local");

            migrationBuilder.DropTable(
                name: "plage_modele");

            migrationBuilder.DropTable(
                name: "preference_convocation");

            migrationBuilder.DropTable(
                name: "rendez_vous");

            migrationBuilder.DropTable(
                name: "creneau");

            migrationBuilder.DropTable(
                name: "modele_agenda");

            migrationBuilder.DropTable(
                name: "session");

            migrationBuilder.DropTable(
                name: "ressource");

            migrationBuilder.DropTable(
                name: "lieu");
        }
    }
}
