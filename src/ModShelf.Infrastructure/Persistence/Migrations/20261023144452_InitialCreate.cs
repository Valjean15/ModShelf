using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModShelf.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadJobs",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    GameKey = table.Column<string>(type: "TEXT", nullable: false),
                    ModId = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    FailureReason = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJobs", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_Id",
                table: "DownloadJobs",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJobs");
        }
    }
}
