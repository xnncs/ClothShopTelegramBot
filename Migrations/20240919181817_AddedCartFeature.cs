using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopTelegramBot.Migrations
{
    /// <inheritdoc />
    public partial class AddedCartFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("510cf288-78b3-4591-9abe-aef60f0e7ab8"));

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carts_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartShoppingItem",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemsAddedId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartShoppingItem", x => new { x.CartId, x.ItemsAddedId });
                    table.ForeignKey(
                        name: "FK_CartShoppingItem_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartShoppingItem_ShoppingItems_ItemsAddedId",
                        column: x => x.ItemsAddedId,
                        principalTable: "ShoppingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carts_OwnerId",
                table: "Carts",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartShoppingItem_ItemsAddedId",
                table: "CartShoppingItem",
                column: "ItemsAddedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartShoppingItem");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Age", "IsAdmin", "TelegramId", "Username" },
                values: new object[] { new Guid("510cf288-78b3-4591-9abe-aef60f0e7ab8"), 18, true, 1367636999L, "zitiret" });
        }
    }
}
