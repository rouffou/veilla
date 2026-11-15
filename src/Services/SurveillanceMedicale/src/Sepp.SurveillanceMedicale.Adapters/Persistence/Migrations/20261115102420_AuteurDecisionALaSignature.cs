using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuteurDecisionALaSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "auteur_id",
                table: "decision",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "modifiee_le",
                table: "decision",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modifiee_par",
                table: "decision",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "redigee_par",
                table: "decision",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "version_contenu",
                table: "decision",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ADR 0017 (#401) : les décisions existantes ont été rédigées par leur auteur, à la version 1 du contenu. Les décisions
            // signées gardent leur auteur légal ; celui d'un brouillon n'est fixé qu'à la signature, il est donc remis à vide.
            migrationBuilder.Sql("UPDATE decision SET redigee_par = auteur_id, version_contenu = 1;");
            migrationBuilder.Sql("UPDATE decision SET auteur_id = NULL WHERE statut = 'Brouillon';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE decision SET auteur_id = redigee_par WHERE auteur_id IS NULL;");

            migrationBuilder.DropColumn(
                name: "modifiee_le",
                table: "decision");

            migrationBuilder.DropColumn(
                name: "modifiee_par",
                table: "decision");

            migrationBuilder.DropColumn(
                name: "redigee_par",
                table: "decision");

            migrationBuilder.DropColumn(
                name: "version_contenu",
                table: "decision");

            migrationBuilder.AlterColumn<string>(
                name: "auteur_id",
                table: "decision",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
