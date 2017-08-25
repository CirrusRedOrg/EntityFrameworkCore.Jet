using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EntityFramework.Jet.FunctionalTests.Migrations
{
    public partial class Migration01 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileInfo",
                columns: table => new
                {
                    FileInfoId = table.Column<int>(nullable: false)
                        .Annotation("Jet:ValueGeneration", "True"),
                    BlindedName = table.Column<string>(nullable: true),
                    ContainsSynapse = table.Column<bool>(nullable: false),
                    Path = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileInfo", x => x.FileInfoId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileInfo");
        }
    }
}
