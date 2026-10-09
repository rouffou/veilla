using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Personnes.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MutationsRegistreNational : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_deces",
                table: "personne",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mutation_registre_national",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    date_effet = table.Column<DateOnly>(type: "date", nullable: false),
                    avant_chiffre = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    apres_chiffre = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    personne_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mutation_registre_national", x => x.id);
                    table.ForeignKey(
                        name: "fk_mutation_registre_national_personne_personne_id",
                        column: x => x.personne_id,
                        principalTable: "personne",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mutation_registre_national_personne_id",
                table: "mutation_registre_national",
                column: "personne_id");

            migrationBuilder.CreateIndex(
                name: "ix_mutation_registre_national_reference",
                table: "mutation_registre_national",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mutation_registre_national");

            migrationBuilder.DropColumn(
                name: "date_deces",
                table: "personne");
        }
    }
}
