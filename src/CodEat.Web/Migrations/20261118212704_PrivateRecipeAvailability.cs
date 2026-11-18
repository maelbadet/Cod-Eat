using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodEat.Web.Migrations
{
    /// <inheritdoc />
    public partial class PrivateRecipeAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AvailablePrivately",
                table: "Recipes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailablePrivately",
                table: "Recipes");
        }
    }
}
