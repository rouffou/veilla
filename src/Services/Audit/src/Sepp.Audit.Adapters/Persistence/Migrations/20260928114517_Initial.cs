using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Audit.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entree_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false),
                    evenement_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horodatage = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    utilisateur_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    objet_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    objet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motif = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    bris_de_glace = table.Column<bool>(type: "boolean", nullable: false),
                    empreinte_precedente = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    empreinte = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entree_audit", x => x.id);
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
                name: "sceau_purge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numero_final = table.Column<long>(type: "bigint", nullable: false),
                    empreinte_finale = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    nombre_entrees = table.Column<long>(type: "bigint", nullable: false),
                    purge_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sceau_purge", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_evenement_source_id",
                table: "entree_audit",
                column: "evenement_source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_horodatage",
                table: "entree_audit",
                column: "horodatage",
                filter: "bris_de_glace");

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_objet_type_objet_id",
                table: "entree_audit",
                columns: new[] { "objet_type", "objet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_utilisateur_id_horodatage",
                table: "entree_audit",
                columns: new[] { "utilisateur_id", "horodatage" });

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_zone_horodatage",
                table: "entree_audit",
                columns: new[] { "zone", "horodatage" });

            migrationBuilder.CreateIndex(
                name: "ix_entree_audit_zone_numero",
                table: "entree_audit",
                columns: new[] { "zone", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_processed_at",
                table: "outbox_message",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sceau_purge_zone_numero_final",
                table: "sceau_purge",
                columns: new[] { "zone", "numero_final" },
                unique: true);

            // NF-04 : journal en ajout seul, protégé par la base elle-même (indépendamment des droits applicatifs).
            // - toute modification d'une entrée ou d'un sceau est refusée, de même que toute troncature ;
            // - une suppression n'est admise que dans une purge légale : autorisation locale à la transaction
            //   (sepp.audit_purge), entrée de plus de 10 ans, et préfixe de la chaîne de sa zone (jamais unitaire) ;
            // - chaque purge est scellée par la base (sceau_purge) : dernier maillon supprimé, à partir duquel la
            //   vérification de la chaîne reprend.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_refuser_modification() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Journal d''audit en ajout seul : % interdit sur %.', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE FUNCTION audit_controler_suppression() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('sepp.audit_purge', true) IS DISTINCT FROM 'on' THEN
                        RAISE EXCEPTION 'Journal d''audit en ajout seul : suppression hors purge légale interdite.'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF OLD.horodatage >= now() - interval '10 years' THEN
                        RAISE EXCEPTION 'Journal d''audit : l''entrée % (zone %) a moins de 10 ans et ne peut être purgée (NF-04).', OLD.numero, OLD.zone
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN OLD;
                END;
                $$;

                CREATE FUNCTION audit_sceller_purge() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    purge record;
                BEGIN
                    FOR purge IN SELECT zone, max(numero) AS numero_final, count(*) AS nombre FROM entrees_purgees GROUP BY zone LOOP
                        IF EXISTS (SELECT 1 FROM entree_audit e WHERE e.zone = purge.zone AND e.numero < purge.numero_final) THEN
                            RAISE EXCEPTION 'Journal d''audit : seule la purge d''un préfixe de chaîne est permise (zone %), jamais une suppression unitaire.', purge.zone
                                USING ERRCODE = 'insufficient_privilege';
                        END IF;
                        INSERT INTO sceau_purge (id, zone, numero_final, empreinte_finale, nombre_entrees, purge_le)
                        SELECT gen_random_uuid(), p.zone, p.numero, p.empreinte, purge.nombre, now()
                        FROM entrees_purgees p
                        WHERE p.zone = purge.zone AND p.numero = purge.numero_final;
                    END LOOP;
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER entree_audit_ajout_seul BEFORE UPDATE ON entree_audit
                    FOR EACH ROW EXECUTE FUNCTION audit_refuser_modification();
                CREATE TRIGGER entree_audit_suppression BEFORE DELETE ON entree_audit
                    FOR EACH ROW EXECUTE FUNCTION audit_controler_suppression();
                CREATE TRIGGER entree_audit_sceau_purge AFTER DELETE ON entree_audit
                    REFERENCING OLD TABLE AS entrees_purgees
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_sceller_purge();
                CREATE TRIGGER entree_audit_sans_troncature BEFORE TRUNCATE ON entree_audit
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_refuser_modification();
                CREATE TRIGGER sceau_purge_ajout_seul BEFORE UPDATE OR DELETE OR TRUNCATE ON sceau_purge
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_refuser_modification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS sceau_purge_ajout_seul ON sceau_purge;
                DROP TRIGGER IF EXISTS entree_audit_sans_troncature ON entree_audit;
                DROP TRIGGER IF EXISTS entree_audit_sceau_purge ON entree_audit;
                DROP TRIGGER IF EXISTS entree_audit_suppression ON entree_audit;
                DROP TRIGGER IF EXISTS entree_audit_ajout_seul ON entree_audit;
                DROP FUNCTION IF EXISTS audit_sceller_purge();
                DROP FUNCTION IF EXISTS audit_controler_suppression();
                DROP FUNCTION IF EXISTS audit_refuser_modification();
                """);

            migrationBuilder.DropTable(
                name: "entree_audit");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "sceau_purge");
        }
    }
}
