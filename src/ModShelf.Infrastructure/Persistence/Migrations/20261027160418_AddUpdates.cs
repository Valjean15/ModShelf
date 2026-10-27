using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdates : Migration
    {
        // Static, rather than an array per call, for CA1861.
        private static readonly string[] UpdateIndexColumns = ["ModSequence", "FileId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryUpdates",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    VersionLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReplacesFileId = table.Column<string>(type: "TEXT", nullable: true),
                    FoundAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryUpdates", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_LibraryUpdates_LibraryMods_ModSequence",
                        column: x => x.ModSequence,
                        principalTable: "LibraryMods",
                        principalColumn: "Sequence",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryUpdates_ModSequence_FileId",
                table: "LibraryUpdates",
                columns: UpdateIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryUpdates");
        }
    }
}
