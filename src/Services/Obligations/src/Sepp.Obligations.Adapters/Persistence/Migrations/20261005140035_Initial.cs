using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Obligations.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "affectation_locale",
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
                    table.PrimaryKey("pk_affectation_locale", x => x.affectation_id);
                });

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
                name: "demande_travailleur",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_demande = table.Column<DateOnly>(type: "date", nullable: false),
                    enregistree_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demande_travailleur", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "etat_particulier_local",
                columns: table => new
                {
                    etat_particulier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categorie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etat_particulier_local", x => x.etat_particulier_id);
                });

            migrationBuilder.CreateTable(
                name: "examen_local",
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
                    table.PrimaryKey("pk_examen_local", x => x.examen_id);
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
                name: "incapacite_locale",
                columns: table => new
                {
                    incapacite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incapacite_locale", x => x.incapacite_id);
                });

            migrationBuilder.CreateTable(
                name: "liste_nominative_locale",
                columns: table => new
                {
                    liste_nominative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_liste = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    date_reference = table.Column<DateOnly>(type: "date", nullable: false),
                    date_generation = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liste_nominative_locale", x => x.liste_nominative_id);
                });

            migrationBuilder.CreateTable(
                name: "obligation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    origine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    codes_risques = table.Column<string[]>(type: "text[]", nullable: false),
                    date_due = table.Column<DateOnly>(type: "date", nullable: false),
                    date_limite = table.Column<DateOnly>(type: "date", nullable: true),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_rendez_vous = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    date_realisation = table.Column<DateOnly>(type: "date", nullable: true),
                    examen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_report = table.Column<DateOnly>(type: "date", nullable: true),
                    motif_annulation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    echue_signalee_le = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obligation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "occupation_locale",
                columns: table => new
                {
                    occupation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_debut = table.Column<DateOnly>(type: "date", nullable: true),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    fin_recue_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_locale", x => x.occupation_id);
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
                    unite = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametre_legal_local", x => new { x.code, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "profil_risque_poste_local",
                columns: table => new
                {
                    poste_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codes_risques = table.Column<string[]>(type: "text[]", nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profil_risque_poste_local", x => new { x.poste_id, x.valide_du });
                });

            migrationBuilder.CreateTable(
                name: "regle_surveillance_locale",
                columns: table => new
                {
                    code_risque = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    risque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categorie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type_surveillance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    frequence_mois = table.Column<int>(type: "integer", nullable: true),
                    surveillance_prolongee = table.Column<bool>(type: "boolean", nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regle_surveillance_locale", x => new { x.code_risque, x.version });
                });

            migrationBuilder.CreateTable(
                name: "rendez_vous_local",
                columns: table => new
                {
                    rendez_vous_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    obligation_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    annule = table.Column<bool>(type: "boolean", nullable: false),
                    planifie_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rendez_vous_local", x => x.rendez_vous_id);
                });

            migrationBuilder.CreateTable(
                name: "reprise_locale",
                columns: table => new
                {
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_reprise = table.Column<DateOnly>(type: "date", nullable: false),
                    debut_absence = table.Column<DateOnly>(type: "date", nullable: false),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reprise_locale", x => new { x.personne_id, x.affilie_id, x.date_reprise });
                });

            migrationBuilder.CreateTable(
                name: "surcharge_frequence_locale",
                columns: table => new
                {
                    surcharge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cible_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_risque = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    frequence_mois = table.Column<int>(type: "integer", nullable: false),
                    valide_du = table.Column<DateOnly>(type: "date", nullable: false),
                    valide_jusqu_au = table.Column<DateOnly>(type: "date", nullable: true),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surcharge_frequence_locale", x => x.surcharge_id);
                });

            migrationBuilder.CreateTable(
                name: "trajet_local",
                columns: table => new
                {
                    trajet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_demande = table.Column<DateOnly>(type: "date", nullable: true),
                    date_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    statut = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trajet_local", x => x.trajet_id);
                });

            migrationBuilder.CreateTable(
                name: "trace_calcul",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    regle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    regle_version = table.Column<int>(type: "integer", nullable: true),
                    explication = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    entrees = table.Column<string[]>(type: "text[]", nullable: false),
                    date_calcul = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trace_calcul", x => x.id);
                    table.ForeignKey(
                        name: "fk_trace_calcul_obligation_obligation_id",
                        column: x => x.obligation_id,
                        principalTable: "obligation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affectation_locale_personne_id",
                table: "affectation_locale",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_affectation_locale_poste_id",
                table: "affectation_locale",
                column: "poste_id");

            migrationBuilder.CreateIndex(
                name: "ix_demande_travailleur_personne_id",
                table: "demande_travailleur",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_etat_particulier_local_personne_id",
                table: "etat_particulier_local",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_examen_local_personne_id",
                table: "examen_local",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_incapacite_locale_personne_id",
                table: "incapacite_locale",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_liste_nominative_locale_affilie_id",
                table: "liste_nominative_locale",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligation_affilie_id_statut",
                table: "obligation",
                columns: new[] { "affilie_id", "statut" });

            migrationBuilder.CreateIndex(
                name: "ix_obligation_personne_id_cle",
                table: "obligation",
                columns: new[] { "personne_id", "cle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occupation_locale_affilie_id",
                table: "occupation_locale",
                column: "affilie_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_locale_personne_id",
                table: "occupation_locale",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rendez_vous_local_personne_id",
                table: "rendez_vous_local",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_surcharge_frequence_locale_cible_id",
                table: "surcharge_frequence_locale",
                column: "cible_id");

            migrationBuilder.CreateIndex(
                name: "ix_trace_calcul_obligation_id",
                table: "trace_calcul",
                column: "obligation_id");

            migrationBuilder.CreateIndex(
                name: "ix_trajet_local_personne_id",
                table: "trajet_local",
                column: "personne_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affectation_locale");

            migrationBuilder.DropTable(
                name: "calendrier_local");

            migrationBuilder.DropTable(
                name: "demande_travailleur");

            migrationBuilder.DropTable(
                name: "etat_particulier_local");

            migrationBuilder.DropTable(
                name: "examen_local");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "incapacite_locale");

            migrationBuilder.DropTable(
                name: "liste_nominative_locale");

            migrationBuilder.DropTable(
                name: "occupation_locale");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "parametre_legal_local");

            migrationBuilder.DropTable(
                name: "profil_risque_poste_local");

            migrationBuilder.DropTable(
                name: "regle_surveillance_locale");

            migrationBuilder.DropTable(
                name: "rendez_vous_local");

            migrationBuilder.DropTable(
                name: "reprise_locale");

            migrationBuilder.DropTable(
                name: "surcharge_frequence_locale");

            migrationBuilder.DropTable(
                name: "trace_calcul");

            migrationBuilder.DropTable(
                name: "trajet_local");

            migrationBuilder.DropTable(
                name: "obligation");
        }
    }
}
