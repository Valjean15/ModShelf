using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibrary : Migration
    {
        // Static, rather than an array per call, for CA1861.
        private static readonly string[] FileIndexColumns = ["ModSequence", "FileId"];
        private static readonly string[] ModIndexColumns = ["SourceId", "GameKey", "ModId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryMods",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    GameKey = table.Column<string>(type: "TEXT", nullable: false),
                    ModId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsWatched = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryMods", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "LibraryFiles",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    VersionLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    SizeInBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFiles", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_LibraryFiles_LibraryMods_ModSequence",
                        column: x => x.ModSequence,
                        principalTable: "LibraryMods",
                        principalColumn: "Sequence",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFiles_ModSequence_FileId",
                table: "LibraryFiles",
                columns: FileIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryMods_SourceId_GameKey_ModId",
                table: "LibraryMods",
                columns: ModIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryFiles");

            migrationBuilder.DropTable(
                name: "LibraryMods");
        }
    }
}
