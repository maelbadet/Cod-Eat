using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodEat.Web.Migrations
{
    /// <inheritdoc />
    public partial class StructuredRecipeImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportStateJson",
                table: "Recipes",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportStateJson",
                table: "Recipes");
        }
    }
}
