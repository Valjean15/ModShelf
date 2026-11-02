using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModDetails",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    GameKey = table.Column<string>(type: "TEXT", nullable: false),
                    ModId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PictureUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailBlurredUrl = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasAdultContent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Downloads = table.Column<long>(type: "INTEGER", nullable: true),
                    Endorsements = table.Column<long>(type: "INTEGER", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModDetails", x => new { x.SourceId, x.GameKey, x.ModId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModDetails");
        }
    }
}
