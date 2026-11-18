using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodEat.Web.Migrations
{
    /// <inheritdoc />
    public partial class HouseholdMemberSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_HouseholdId",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "MemberSlot",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "HouseholdId" ORDER BY "Id") AS slot
                    FROM "AspNetUsers"
                )
                UPDATE "AspNetUsers" AS u SET "MemberSlot" = ranked.slot
                FROM ranked WHERE u."Id" = ranked."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_HouseholdId_MemberSlot",
                table: "AspNetUsers",
                columns: new[] { "HouseholdId", "MemberSlot" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberSlot",
                table: "AspNetUsers",
                sql: "\"MemberSlot\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_HouseholdId_MemberSlot",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MemberSlot",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MemberSlot",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_HouseholdId",
                table: "AspNetUsers",
                column: "HouseholdId");
        }
    }
}
