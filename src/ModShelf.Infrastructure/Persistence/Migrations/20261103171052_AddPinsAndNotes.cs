using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPinsAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "LibraryMods",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "LibraryMods",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SkippedUpdates",
                columns: table => new
                {
                    ModSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkippedUpdates", x => new { x.ModSequence, x.FileId });
                    table.ForeignKey(
                        name: "FK_SkippedUpdates_LibraryMods_ModSequence",
                        column: x => x.ModSequence,
                        principalTable: "LibraryMods",
                        principalColumn: "Sequence",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkippedUpdates");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "LibraryMods");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "LibraryMods");
        }
    }
}
