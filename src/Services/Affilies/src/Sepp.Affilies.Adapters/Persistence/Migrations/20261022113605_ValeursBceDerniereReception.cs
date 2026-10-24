using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepp.Affilies.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValeursBceDerniereReception : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code_nace_bce",
                table: "affilie",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "denomination_bce",
                table: "affilie",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forme_juridique_bce",
                table: "affilie",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "code_nace_bce",
                table: "affilie");

            migrationBuilder.DropColumn(
                name: "denomination_bce",
                table: "affilie");

            migrationBuilder.DropColumn(
                name: "forme_juridique_bce",
                table: "affilie");
        }
    }
}
