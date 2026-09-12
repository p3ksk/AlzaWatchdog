using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAt = table.Column<long>(type: "bigint", nullable: false),
                    HasAlzaPlus = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WatchLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchLists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TrackedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    WatchListId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProductCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    ImageUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    Currency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true),
                    LastPrice = table.Column<string>(type: "longtext", nullable: true),
                    LastPlusPrice = table.Column<string>(type: "longtext", nullable: true),
                    LastCouponPrice = table.Column<string>(type: "longtext", nullable: true),
                    LastAvailability = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    LastCheckedAt = table.Column<long>(type: "bigint", nullable: true),
                    LastError = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedItems_WatchLists_WatchListId",
                        column: x => x.WatchListId,
                        principalTable: "WatchLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TrackedItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Price = table.Column<string>(type: "longtext", nullable: true),
                    PlusPrice = table.Column<string>(type: "longtext", nullable: true),
                    CouponPrice = table.Column<string>(type: "longtext", nullable: true),
                    Availability = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CapturedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceSnapshots_TrackedItems_TrackedItemId",
                        column: x => x.TrackedItemId,
                        principalTable: "TrackedItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshots_TrackedItemId_CapturedAt",
                table: "PriceSnapshots",
                columns: new[] { "TrackedItemId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_IsActive_ProductCode",
                table: "TrackedItems",
                columns: new[] { "IsActive", "ProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_WatchListId_ProductCode",
                table: "TrackedItems",
                columns: new[] { "WatchListId", "ProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchLists_UserId",
                table: "WatchLists",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceSnapshots");

            migrationBuilder.DropTable(
                name: "TrackedItems");

            migrationBuilder.DropTable(
                name: "WatchLists");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
