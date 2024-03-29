﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace AlertHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DangerNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Location = table.Column<Point>(type: "geography", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DisasterType = table.Column<int>(type: "int", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Municipality = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Directions = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DangerNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DangerReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisasterType = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<Point>(type: "geography", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DangerReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DangerReports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFcmDeviceIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFcmDeviceIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFcmDeviceIds_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Location = table.Column<Point>(type: "geography", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLocations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActiveDangerReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DangerReportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveDangerReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveDangerReports_DangerReports_DangerReportId",
                        column: x => x.DangerReportId,
                        principalTable: "DangerReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchivedDangerReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DangerReportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedDangerReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedDangerReports_DangerReports_DangerReportId",
                        column: x => x.DangerReportId,
                        principalTable: "DangerReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoordinatesInformation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Country = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Municipality = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DangerReportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoordinatesInformation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoordinatesInformation_DangerReports_DangerReportId",
                        column: x => x.DangerReportId,
                        principalTable: "DangerReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveDangerReports_DangerReportId",
                table: "ActiveDangerReports",
                column: "DangerReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedDangerReports_DangerReportId",
                table: "ArchivedDangerReports",
                column: "DangerReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatesInformation_Country",
                table: "CoordinatesInformation",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatesInformation_Culture",
                table: "CoordinatesInformation",
                column: "Culture");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatesInformation_DangerReportId",
                table: "CoordinatesInformation",
                column: "DangerReportId");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatesInformation_Municipality",
                table: "CoordinatesInformation",
                column: "Municipality");

            migrationBuilder.CreateIndex(
                name: "IX_DangerReports_CreatedAt",
                table: "DangerReports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DangerReports_UserId",
                table: "DangerReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFcmDeviceIds_UserId",
                table: "UserFcmDeviceIds",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLocations_UserId",
                table: "UserLocations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveDangerReports");

            migrationBuilder.DropTable(
                name: "ArchivedDangerReports");

            migrationBuilder.DropTable(
                name: "CoordinatesInformation");

            migrationBuilder.DropTable(
                name: "DangerNotifications");

            migrationBuilder.DropTable(
                name: "UserFcmDeviceIds");

            migrationBuilder.DropTable(
                name: "UserLocations");

            migrationBuilder.DropTable(
                name: "DangerReports");
        }
    }
}
