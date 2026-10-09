using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <summary>
    /// Migration de données (saga « examen de reprise », #264, ARC-33, lot 6) : le code de type d'examen
    /// <c>ACTES_SUPPLEMENTAIRES</c>, propre à ce service, est remplacé par <c>ACTES_MEDICAUX_SUPPLEMENTAIRES</c>, code partagé
    /// de <c>Sepp.Contracts.Examens.TypesExamen</c> (source de vérité, repris du service Obligations, §5.1).
    /// <para>
    /// Tables concernées (toutes les colonnes qui stockent un type d'examen en clair) : <c>examen.type_examen</c>,
    /// <c>decision.type_examen</c>, <c>obligation_due.type_examen</c> (projection ; Obligations publie déjà le nouveau code,
    /// la mise à jour est donc normalement sans effet ici), et les messages d'intégration encore non publiés de
    /// <c>outbox_message</c> (charge JSON, propriété <c>typeExamen</c>) pour qu'aucun ancien code ne sorte du service après
    /// la migration. Les messages déjà publiés et l'inbox ne sont pas réécrits (historique de ce qui a été échangé). Les
    /// paquets de transfert de dossier (<c>transfert_dossier.paquet_chiffre</c>) sont chiffrés (ARC-45) et figés par leur
    /// empreinte : ils gardent le code de leur date d'export.
    /// </para>
    /// <para>
    /// Traçabilité : aucun contenu clinique n'est lu ni modifié ; seuls des codes de référentiel le sont. Le nombre de lignes
    /// mises à jour est consigné dans le journal de la migration (avis PostgreSQL <c>RAISE NOTICE</c>).
    /// </para>
    /// </summary>
    public partial class CodeActesMedicauxSupplementaires : Migration
    {
        private const string AncienCode = "ACTES_SUPPLEMENTAIRES";
        private const string NouveauCode = "ACTES_MEDICAUX_SUPPLEMENTAIRES";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE
                    n integer;
                BEGIN
                    UPDATE examen SET type_examen = '{{NouveauCode}}' WHERE type_examen = '{{AncienCode}}';
                    GET DIAGNOSTICS n = ROW_COUNT;
                    RAISE NOTICE 'Codes de type d''examen : % ligne(s) de examen migrée(s) de {{AncienCode}} vers {{NouveauCode}}.', n;

                    UPDATE decision SET type_examen = '{{NouveauCode}}' WHERE type_examen = '{{AncienCode}}';
                    GET DIAGNOSTICS n = ROW_COUNT;
                    RAISE NOTICE 'Codes de type d''examen : % ligne(s) de decision migrée(s).', n;

                    UPDATE obligation_due SET type_examen = '{{NouveauCode}}' WHERE type_examen = '{{AncienCode}}';
                    GET DIAGNOSTICS n = ROW_COUNT;
                    RAISE NOTICE 'Codes de type d''examen : % ligne(s) de obligation_due migrée(s).', n;

                    UPDATE outbox_message
                    SET payload = jsonb_set(payload, '{typeExamen}', to_jsonb('{{NouveauCode}}'::text))
                    WHERE processed_at IS NULL AND payload ->> 'typeExamen' = '{{AncienCode}}';
                    GET DIAGNOSTICS n = ROW_COUNT;
                    RAISE NOTICE 'Codes de type d''examen : % message(s) non publié(s) de outbox_message migré(s).', n;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement sans effet : l'ancien code n'est plus reconnu par les autres services, et les lignes qui
            // portaient déjà le nouveau code (projection d'Obligations) ne se distinguent plus des lignes migrées.
        }
    }
}
