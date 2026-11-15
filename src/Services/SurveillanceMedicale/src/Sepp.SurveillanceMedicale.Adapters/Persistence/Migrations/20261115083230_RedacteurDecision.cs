using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.SurveillanceMedicale.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RedacteurDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "redigee_par",
                table: "decision",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // #401 : avant cette migration, seul le CPMT auteur rédigeait ; il est donc le rédacteur des décisions existantes.
            migrationBuilder.Sql("UPDATE decision SET redigee_par = auteur_id WHERE redigee_par = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "redigee_par",
                table: "decision");
        }
    }
}
