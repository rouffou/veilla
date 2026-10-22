using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Planification.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeplacementConfirmationEtRaisonAnnulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commentaire_annulation",
                table: "rendez_vous",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "nombre_deplacements",
                table: "rendez_vous",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "presence_confirmee_le",
                table: "rendez_vous",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raison_annulation",
                table: "rendez_vous",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commentaire_annulation",
                table: "rendez_vous");

            migrationBuilder.DropColumn(
                name: "nombre_deplacements",
                table: "rendez_vous");

            migrationBuilder.DropColumn(
                name: "presence_confirmee_le",
                table: "rendez_vous");

            migrationBuilder.DropColumn(
                name: "raison_annulation",
                table: "rendez_vous");
        }
    }
}
