using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Reintegration.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendrier_local",
                columns: table => new
                {
                    annee = table.Column<int>(type: "integer", nullable: false),
                    jours_supplementaires = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendrier_local", x => x.annee);
                });

            migrationBuilder.CreateTable(
                name: "echange_ehealth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incapacite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trajet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinataire = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sens = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    objet = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contenu_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    reference_destinataire = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    accord_travailleur = table.Column<bool>(type: "boolean", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference_e_health = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    en_reponse_a = table.Column<Guid>(type: "uuid", nullable: true),
                    tentatives = table.Column<int>(type: "integer", nullable: false),
                    code_erreur = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    envoye_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_echange_ehealth", x => x.id);
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
                name: "incapacite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin_prevue = table.Column<DateOnly>(type: "date", nullable: true),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notifiee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gestionnaire_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    effectif_employeur = table.Column<int>(type: "integer", nullable: true),
                    travailleur_informe_le = table.Column<DateOnly>(type: "date", nullable: true),
                    document_information_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trajet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trajet_demande_le = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    prochaine_echeance = table.Column<DateOnly>(type: "date", nullable: true),
                    prochaine_minuterie = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    minuteries_declenchees = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incapacite", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obligation_reintegration",
                columns: table => new
                {
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_examen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obligation_reintegration", x => x.obligation_id);
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
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    valide_jusqu_au = table.Column<DateOnly>(type: "date", nullable: true),
                    valeur = table.Column<decimal>(type: "numeric", nullable: false),
                    unite = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal_local", x => new { x.code, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "trajet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incapacite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiateur = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_demande = table.Column<DateOnly>(type: "date", nullable: false),
                    date_limite_invitation = table.Column<DateOnly>(type: "date", nullable: false),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invitation_envoyee_le = table.Column<DateOnly>(type: "date", nullable: true),
                    invitation_recommandee = table.Column<bool>(type: "boolean", nullable: false),
                    evalue_le = table.Column<DateOnly>(type: "date", nullable: true),
                    evaluateur_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note_evaluation_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    formulaire_le = table.Column<DateOnly>(type: "date", nullable: true),
                    conclusion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    formulaire_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reponse_employeur = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reponse_employeur_le = table.Column<DateOnly>(type: "date", nullable: true),
                    echeance_reponse_travailleur = table.Column<DateOnly>(type: "date", nullable: true),
                    reponse_travailleur = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reponse_travailleur_le = table.Column<DateOnly>(type: "date", nullable: true),
                    fin_le = table.Column<DateOnly>(type: "date", nullable: true),
                    issue = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    medecin_conseil_informe_le = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    prochaine_echeance = table.Column<DateOnly>(type: "date", nullable: true),
                    prochaine_minuterie = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    minuteries_declenchees = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trajet", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "estimation_potentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    resultat = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    methode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    auteur_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commentaire_chiffre = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    incapacite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estimation_potentiel", x => x.id);
                    table.ForeignKey(
                        name: "fk_estimation_potentiel_incapacite_incapacite_id",
                        column: x => x.incapacite_id,
                        principalTable: "incapacite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "absence_rdv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_rdv = table.Column<DateOnly>(type: "date", nullable: false),
                    notifie_medecin_conseil = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trajet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_rdv", x => x.id);
                    table.ForeignKey(
                        name: "fk_absence_rdv_trajet_trajet_id",
                        column: x => x.trajet_id,
                        principalTable: "trajet",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "etape_trajet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    echeance = table.Column<DateOnly>(type: "date", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trajet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etape_trajet", x => x.id);
                    table.ForeignKey(
                        name: "fk_etape_trajet_trajet_trajet_id",
                        column: x => x.trajet_id,
                        principalTable: "trajet",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absence_rdv_trajet_id_rendez_vous_id",
                table: "absence_rdv",
                columns: new[] { "trajet_id", "rendez_vous_id" },
                unique: true,
                filter: "rendez_vous_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_echange_ehealth_incapacite_id",
                table: "echange_ehealth",
                column: "incapacite_id");

            migrationBuilder.CreateIndex(
                name: "ix_echange_ehealth_reference_e_health",
                table: "echange_ehealth",
                column: "reference_e_health",
                unique: true,
                filter: "reference_e_health IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_echange_ehealth_statut",
                table: "echange_ehealth",
                column: "statut");

            migrationBuilder.CreateIndex(
                name: "ix_echange_ehealth_trajet_id",
                table: "echange_ehealth",
                column: "trajet_id");

            migrationBuilder.CreateIndex(
                name: "ix_estimation_potentiel_incapacite_id",
                table: "estimation_potentiel",
                column: "incapacite_id");

            migrationBuilder.CreateIndex(
                name: "ix_etape_trajet_trajet_id",
                table: "etape_trajet",
                column: "trajet_id");

            migrationBuilder.CreateIndex(
                name: "ix_incapacite_affilie_id",
                table: "incapacite",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_incapacite_personne_id_affilie_id",
                table: "incapacite",
                columns: new[] { "personne_id", "affilie_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incapacite_prochaine_echeance",
                table: "incapacite",
                column: "prochaine_echeance",
                filter: "prochaine_echeance IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_reintegration_personne_id",
                table: "obligation_reintegration",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trajet_affilie_id",
                table: "trajet",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_trajet_incapacite_id",
                table: "trajet",
                column: "incapacite_id");

            migrationBuilder.CreateIndex(
                name: "ix_trajet_personne_id_affilie_id",
                table: "trajet",
                columns: new[] { "personne_id", "affilie_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trajet_prochaine_echeance",
                table: "trajet",
                column: "prochaine_echeance",
                filter: "prochaine_echeance IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_rdv");

            migrationBuilder.DropTable(
                name: "calendrier_local");

            migrationBuilder.DropTable(
                name: "echange_ehealth");

            migrationBuilder.DropTable(
                name: "estimation_potentiel");

            migrationBuilder.DropTable(
                name: "etape_trajet");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "obligation_reintegration");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "parametre_legal_local");

            migrationBuilder.DropTable(
                name: "incapacite");

            migrationBuilder.DropTable(
                name: "trajet");
        }
    }
}
