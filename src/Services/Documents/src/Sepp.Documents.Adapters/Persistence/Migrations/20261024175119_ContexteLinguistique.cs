using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Documents.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContexteLinguistique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contexte_linguistique",
                columns: table => new
                {
                    sujet = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sujet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regime = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    langue = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    evenement_du = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contexte_linguistique", x => new { x.sujet, x.sujet_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contexte_linguistique");
        }
    }
}
