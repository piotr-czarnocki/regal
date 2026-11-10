using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Regal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStocktakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StocktakeId",
                table: "StockDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StocktakeCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeCounters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "Stocktakes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocktakes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StocktakeId = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<int>(type: "integer", nullable: false),
                    Counted = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Expected = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeLines", x => x.Id);
                    table.CheckConstraint("CK_StocktakeLines_Counted_NonNegative", "\"Counted\" IS NULL OR \"Counted\" >= 0");
                    table.ForeignKey(
                        name: "FK_StocktakeLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeLines_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_StocktakeId",
                table: "StockDocuments",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLines_BatchId",
                table: "StocktakeLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLines_StocktakeId_BatchId",
                table: "StocktakeLines",
                columns: new[] { "StocktakeId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_Number",
                table: "Stocktakes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_Status_CreatedAt",
                table: "Stocktakes",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_UserId",
                table: "Stocktakes",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocuments_Stocktakes_StocktakeId",
                table: "StockDocuments",
                column: "StocktakeId",
                principalTable: "Stocktakes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A closed sheet stays closed: neither it nor its lines change once approved or
            // cancelled, whatever the application does (SPEC 5.14). Drafts change freely.
            migrationBuilder.Sql("""
                CREATE FUNCTION stocktakes_closed_stay_closed() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Status" <> 'Draft' THEN
                        RAISE EXCEPTION 'Arkusz spisu % jest zamknięty.', OLD."Id" USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER stocktakes_closed_stay_closed BEFORE UPDATE ON "Stocktakes"
                    FOR EACH ROW EXECUTE FUNCTION stocktakes_closed_stay_closed();

                CREATE FUNCTION stocktake_lines_only_in_draft() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    sheet integer := COALESCE(NEW."StocktakeId", OLD."StocktakeId");
                BEGIN
                    IF (SELECT "Status" FROM "Stocktakes" WHERE "Id" = sheet) <> 'Draft' THEN
                        RAISE EXCEPTION 'Arkusz spisu % jest zamknięty.', sheet USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $$;
                CREATE TRIGGER stocktake_lines_only_in_draft BEFORE INSERT OR UPDATE OR DELETE ON "StocktakeLines"
                    FOR EACH ROW EXECUTE FUNCTION stocktake_lines_only_in_draft();
                """);
            migrationBuilder.Sql("REVOKE DELETE ON \"Stocktakes\" FROM regal_app;");
            migrationBuilder.Sql("REVOKE DELETE ON \"StocktakeCounters\" FROM regal_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER stocktake_lines_only_in_draft ON \"StocktakeLines\"; DROP FUNCTION stocktake_lines_only_in_draft(); DROP TRIGGER stocktakes_closed_stay_closed ON \"Stocktakes\"; DROP FUNCTION stocktakes_closed_stay_closed();");

            migrationBuilder.DropForeignKey(
                name: "FK_StockDocuments_Stocktakes_StocktakeId",
                table: "StockDocuments");

            migrationBuilder.DropTable(
                name: "StocktakeCounters");

            migrationBuilder.DropTable(
                name: "StocktakeLines");

            migrationBuilder.DropTable(
                name: "Stocktakes");

            migrationBuilder.DropIndex(
                name: "IX_StockDocuments_StocktakeId",
                table: "StockDocuments");

            migrationBuilder.DropColumn(
                name: "StocktakeId",
                table: "StockDocuments");
        }
    }
}
