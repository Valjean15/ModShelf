using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryEvents",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    GameKey = table.Column<string>(type: "TEXT", nullable: false),
                    ModId = table.Column<string>(type: "TEXT", nullable: false),
                    ModName = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", nullable: true),
                    VersionLabel = table.Column<string>(type: "TEXT", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    ReplacesFileId = table.Column<string>(type: "TEXT", nullable: true),
                    ReplacesVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ByRule = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryEvents", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEvents_At",
                table: "LibraryEvents",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryEvents");
        }
    }
}
