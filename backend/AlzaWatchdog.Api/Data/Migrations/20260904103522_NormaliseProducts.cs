using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormaliseProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceSnapshots_TrackedItems_TrackedItemId",
                table: "PriceSnapshots");

            migrationBuilder.RenameColumn(
                name: "TrackedItemId",
                table: "PriceSnapshots",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_PriceSnapshots_TrackedItemId_CapturedAt",
                table: "PriceSnapshots",
                newName: "IX_PriceSnapshots_ProductId_CapturedAt");

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    LastPrice = table.Column<string>(type: "TEXT", nullable: true),
                    LastPlusPrice = table.Column<string>(type: "TEXT", nullable: true),
                    LastCouponPrice = table.Column<string>(type: "TEXT", nullable: true),
                    LastAvailability = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastCheckedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "TrackedItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ----------------------------------------------------------------
            // Move the data across before anything is dropped. EF scaffolds the
            // drops first, which would throw away every price and every history
            // row; the operations below are the same ones, reordered, with the
            // copy in the middle.
            // ----------------------------------------------------------------

            // One product per distinct code. The winning row keeps its own id, so
            // its snapshots already point at the right place and need no remapping.
            migrationBuilder.Sql("""
                INSERT INTO Products (Id, ProductCode, CanonicalUrl, Name, ImageUrl, Currency,
                                      LastPrice, LastPlusPrice, LastCouponPrice, LastAvailability,
                                      LastCheckedAt, LastError, ConsecutiveFailures, IsActive, CreatedAt)
                SELECT t.Id, t.ProductCode, t.CanonicalUrl, t.Name, t.ImageUrl, t.Currency,
                       t.LastPrice, t.LastPlusPrice, t.LastCouponPrice, t.LastAvailability,
                       t.LastCheckedAt, t.LastError, t.ConsecutiveFailures, t.IsActive, t.CreatedAt
                FROM TrackedItems t
                WHERE t.Id = (SELECT MIN(t2.Id) FROM TrackedItems t2
                              WHERE t2.ProductCode = t.ProductCode);
                """);

            // Every tracking now points at the shared product.
            migrationBuilder.Sql("""
                UPDATE TrackedItems
                SET ProductId = (SELECT p.Id FROM Products p
                                 WHERE p.ProductCode = TrackedItems.ProductCode);
                """);

            // Snapshots still carry the tracked-item id they were renamed from, so
            // the duplicates collapse onto the product their item now points at.
            migrationBuilder.Sql("""
                UPDATE PriceSnapshots
                SET ProductId = (SELECT t.ProductId FROM TrackedItems t
                                 WHERE t.Id = PriceSnapshots.ProductId);
                """);

            // Two lists watching one product recorded the same readings twice.
            // Only rows identical in every respect are dropped — anything that
            // differs is kept, since it may be a genuine reading.
            migrationBuilder.Sql("""
                DELETE FROM PriceSnapshots
                WHERE Id NOT IN (
                    SELECT keep FROM (
                        SELECT MIN(Id) AS keep FROM PriceSnapshots
                        GROUP BY ProductId, CapturedAt, Price, PlusPrice, CouponPrice, Availability
                    ) survivors);
                """);

            migrationBuilder.DropIndex(
                name: "IX_TrackedItems_IsActive_ProductCode",
                table: "TrackedItems");

            migrationBuilder.DropIndex(
                name: "IX_TrackedItems_WatchListId_ProductCode",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastAvailability",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastCouponPrice",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastPlusPrice",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "LastPrice",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "TrackedItems");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_ProductId",
                table: "TrackedItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_WatchListId_ProductId",
                table: "TrackedItems",
                columns: new[] { "WatchListId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive_LastCheckedAt",
                table: "Products",
                columns: new[] { "IsActive", "LastCheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductCode",
                table: "Products",
                column: "ProductCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceSnapshots_Products_ProductId",
                table: "PriceSnapshots",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceSnapshots_Products_ProductId",
                table: "PriceSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackedItems_Products_ProductId",
                table: "TrackedItems");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_TrackedItems_ProductId",
                table: "TrackedItems");

            migrationBuilder.DropIndex(
                name: "IX_TrackedItems_WatchListId_ProductId",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "TrackedItems");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "PriceSnapshots",
                newName: "TrackedItemId");

            migrationBuilder.RenameIndex(
                name: "IX_PriceSnapshots_ProductId_CapturedAt",
                table: "PriceSnapshots",
                newName: "IX_PriceSnapshots_TrackedItemId_CapturedAt");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "TrackedItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TrackedItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastAvailability",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastCheckedAt",
                table: "TrackedItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCouponPrice",
                table: "TrackedItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPlusPrice",
                table: "TrackedItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPrice",
                table: "TrackedItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "TrackedItems",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_IsActive_ProductCode",
                table: "TrackedItems",
                columns: new[] { "IsActive", "ProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_WatchListId_ProductCode",
                table: "TrackedItems",
                columns: new[] { "WatchListId", "ProductCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceSnapshots_TrackedItems_TrackedItemId",
                table: "PriceSnapshots",
                column: "TrackedItemId",
                principalTable: "TrackedItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
