using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Personnes.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdreMutationsRegistreNational : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rang",
                table: "mutation_registre_national",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Mutations déjà historisées : le rang reprend l'ordre de réception (identifiants UUID v7, ordonnés dans le temps).
            migrationBuilder.Sql("""
                UPDATE mutation_registre_national AS m
                SET rang = r.rang
                FROM (
                    SELECT id, ROW_NUMBER() OVER (PARTITION BY personne_id ORDER BY created_at, id) AS rang
                    FROM mutation_registre_national
                ) AS r
                WHERE m.id = r.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rang",
                table: "mutation_registre_national");
        }
    }
}
