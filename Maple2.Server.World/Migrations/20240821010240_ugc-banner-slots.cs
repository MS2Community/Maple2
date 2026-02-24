using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
    public partial class ugcbannerslots : Migration {
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.RenameColumn(
                name: "item",
                table: "character-shop-item-data",
                newName: "Item");

            migrationBuilder.CreateTable(
                name: "ugc-banner-slot",
                columns: table => new {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BannerId = table.Column<long>(type: "bigint", nullable: false),
                    ActivateTime = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Template = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table => {
                    table.PrimaryKey("PK_ugc-banner-slot", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ugc-banner-slot_BannerId",
                table: "ugc-banner-slot",
                column: "BannerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "ugc-banner-slot");

            migrationBuilder.RenameColumn(
                name: "Item",
                table: "character-shop-item-data",
                newName: "item");
        }
    }
}
