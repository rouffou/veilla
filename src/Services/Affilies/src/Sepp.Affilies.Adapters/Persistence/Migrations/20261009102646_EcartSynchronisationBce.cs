using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Affilies.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EcartSynchronisationBce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ecart_synchronisation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_bce = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    affilie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    date_extraction = table.Column<DateOnly>(type: "date", nullable: false),
                    detecte_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    derniere_detection_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolu_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolu_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecart_synchronisation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ecart_synchronisation_numero_bce_code_reference",
                table: "ecart_synchronisation",
                columns: new[] { "numero_bce", "code", "reference" },
                unique: true,
                filter: "statut = 'Ouvert'");

            migrationBuilder.CreateIndex(
                name: "ix_ecart_synchronisation_statut",
                table: "ecart_synchronisation",
                column: "statut");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ecart_synchronisation");
        }
    }
}
