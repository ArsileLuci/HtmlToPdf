using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HtmlToPdf.Migrations
{
    public partial class IndexByOriginHash : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Files_OriginHash",
                table: "Files",
                column: "OriginHash");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Files_OriginHash",
                table: "Files");
        }
    }
}
