using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Jet
{
    public class JetMigrationTest : TestBase<JetMigrationTest.Context>
    {
        [ConditionalFact]
        public virtual void Create_table_with_HasDefaultValueSql()
        {
            using var context = CreateContext(
                model: builder =>
                {
                    builder.Entity<Cookie>(
                        entity =>
                        {
                            entity.Property(e => e.BestServedBefore)
                                .HasDefaultValueSql("#2021-12-31#");

                            entity.HasData(
                                new Cookie
                                {
                                    CookieId = 1,
                                    Name = "Basic",
                                });
                        });
                });
            
            var cookies = context.Set<Cookie>()
                .ToList();
            
            Assert.Single(cookies);
            Assert.Equal(new DateTime(2021, 12, 31), cookies[0].BestServedBefore);
            
            AssertSql(
                $@"CREATE TABLE `Cookie` (
    `CookieId` integer NOT NULL,
    `Name` longchar NULL,
    `BestServedBefore` datetime NOT NULL DEFAULT #2021-12-31#,
    CONSTRAINT `PK_Cookie` PRIMARY KEY (`CookieId`)
);


INSERT INTO `Cookie` (`CookieId`, `Name`)
VALUES (1, 'Basic');


SELECT `c`.`CookieId`, `c`.`BestServedBefore`, `c`.`Name`
FROM `Cookie` AS `c`");
        }

        [ConditionalFact]
        public virtual void Create_many_to_many_table_with_explicit_counter_column_type()
        {
            using var context = CreateContext(
                model: builder =>
                {
                    builder.Entity<Cookie>(entity =>
                    {
                        entity.Property(e => e.CookieId)
                            .HasColumnType("counter");
                        
                        entity.HasData(
                            new Cookie { CookieId = 1, Name = "Chocolate Chip" });
                    });

                    builder.Entity<Backery>(entity =>
                    {
                        entity.Property(e => e.BackeryId)
                            .HasColumnType("counter");
                        
                        entity.HasData(
                            new Backery { BackeryId = 1, Name = "Bread & Cookies" });
                    });
            
                    builder.Entity<CookieBackery>(entity =>
                    {
                        entity.HasKey(e => new { e.CookieId, e.BackeryId });

                        entity.HasOne(d => d.Cookie)
                            .WithMany()
                            .HasForeignKey(d => d.CookieId);

                        entity.HasOne(d => d.Backery)
                            .WithMany()
                            .HasForeignKey(d => d.BackeryId);
                        
                        entity.HasData(
                            new CookieBackery { CookieId = 1, BackeryId = 1 });
                    });
                });
            
            var cookieBackeries = context.Set<CookieBackery>()
                .Include(cb => cb.Cookie)
                .Include(cb => cb.Backery)
                .ToList();
            
            Assert.Single(cookieBackeries);
            Assert.Equal(1, cookieBackeries[0].Cookie.CookieId);
            Assert.Equal(1, cookieBackeries[0].Backery.BackeryId);

            AssertSql(
                $@"CREATE TABLE `Backery` (
    `BackeryId` counter NOT NULL,
    `Name` longchar NULL,
    CONSTRAINT `PK_Backery` PRIMARY KEY (`BackeryId`)
);


CREATE TABLE `Cookie` (
    `CookieId` counter NOT NULL,
    `Name` longchar NULL,
    `BestServedBefore` datetime NOT NULL,
    CONSTRAINT `PK_Cookie` PRIMARY KEY (`CookieId`)
);


CREATE TABLE `CookieBackery` (
    `CookieId` integer NOT NULL,
    `BackeryId` integer NOT NULL,
    CONSTRAINT `PK_CookieBackery` PRIMARY KEY (`CookieId`, `BackeryId`),
    CONSTRAINT `FK_CookieBackery_Backery_BackeryId` FOREIGN KEY (`BackeryId`) REFERENCES `Backery` (`BackeryId`) ON DELETE CASCADE,
    CONSTRAINT `FK_CookieBackery_Cookie_CookieId` FOREIGN KEY (`CookieId`) REFERENCES `Cookie` (`CookieId`) ON DELETE CASCADE
);


INSERT INTO `Backery` (`BackeryId`, `Name`)
VALUES (1, 'Bread & Cookies');


INSERT INTO `Cookie` (`CookieId`, `BestServedBefore`, `Name`)
VALUES (1, #1899-12-30#, 'Chocolate Chip');


INSERT INTO `CookieBackery` (`CookieId`, `BackeryId`)
VALUES (1, 1);


CREATE INDEX `IX_CookieBackery_BackeryId` ON `CookieBackery` (`BackeryId`);


SELECT `c`.`CookieId`, `c`.`BackeryId`, `c0`.`CookieId`, `c0`.`BestServedBefore`, `c0`.`Name`, `b`.`BackeryId`, `b`.`Name`
FROM (`CookieBackery` AS `c`
INNER JOIN `Cookie` AS `c0` ON `c`.`CookieId` = `c0`.`CookieId`)
INNER JOIN `Backery` AS `b` ON `c`.`BackeryId` = `b`.`BackeryId`");
        }

        [ConditionalFact]
        public virtual void Create_many_to_many_table_with_explicit_int_column_type()
        {
            using var context = CreateContext(
                model: builder =>
                {
                    builder.Entity<Cookie>(entity =>
                    {
                        entity.Property(e => e.CookieId)
                            .HasColumnType("int");
                        
                        entity.HasData(
                            new Cookie { CookieId = 1, Name = "Chocolate Chip" });
                    });

                    builder.Entity<Backery>(entity =>
                    {
                        entity.Property(e => e.BackeryId)
                            .HasColumnType("int");
                        
                        entity.HasData(
                            new Backery { BackeryId = 1, Name = "Bread & Cookies" });
                    });
            
                    builder.Entity<CookieBackery>(entity =>
                    {
                        entity.HasKey(e => new { e.CookieId, e.BackeryId });

                        entity.HasOne(d => d.Cookie)
                            .WithMany()
                            .HasForeignKey(d => d.CookieId);

                        entity.HasOne(d => d.Backery)
                            .WithMany()
                            .HasForeignKey(d => d.BackeryId);
                        
                        entity.HasData(
                            new CookieBackery { CookieId = 1, BackeryId = 1 });
                    });
                });
            
            var cookieBackeries = context.Set<CookieBackery>()
                .Include(cb => cb.Cookie)
                .Include(cb => cb.Backery)
                .ToList();
            
            Assert.Single(cookieBackeries);
            Assert.Equal(1, cookieBackeries[0].Cookie.CookieId);
            Assert.Equal(1, cookieBackeries[0].Backery.BackeryId);

            AssertSql(
                $@"CREATE TABLE `Backery` (
    `BackeryId` counter NOT NULL,
    `Name` longchar NULL,
    CONSTRAINT `PK_Backery` PRIMARY KEY (`BackeryId`)
);


CREATE TABLE `Cookie` (
    `CookieId` counter NOT NULL,
    `Name` longchar NULL,
    `BestServedBefore` datetime NOT NULL,
    CONSTRAINT `PK_Cookie` PRIMARY KEY (`CookieId`)
);


CREATE TABLE `CookieBackery` (
    `CookieId` integer NOT NULL,
    `BackeryId` integer NOT NULL,
    CONSTRAINT `PK_CookieBackery` PRIMARY KEY (`CookieId`, `BackeryId`),
    CONSTRAINT `FK_CookieBackery_Backery_BackeryId` FOREIGN KEY (`BackeryId`) REFERENCES `Backery` (`BackeryId`) ON DELETE CASCADE,
    CONSTRAINT `FK_CookieBackery_Cookie_CookieId` FOREIGN KEY (`CookieId`) REFERENCES `Cookie` (`CookieId`) ON DELETE CASCADE
);


INSERT INTO `Backery` (`BackeryId`, `Name`)
VALUES (1, 'Bread & Cookies');


INSERT INTO `Cookie` (`CookieId`, `BestServedBefore`, `Name`)
VALUES (1, #1899-12-30#, 'Chocolate Chip');


INSERT INTO `CookieBackery` (`CookieId`, `BackeryId`)
VALUES (1, 1);


CREATE INDEX `IX_CookieBackery_BackeryId` ON `CookieBackery` (`BackeryId`);


SELECT `c`.`CookieId`, `c`.`BackeryId`, `c0`.`CookieId`, `c0`.`BestServedBefore`, `c0`.`Name`, `b`.`BackeryId`, `b`.`Name`
FROM (`CookieBackery` AS `c`
INNER JOIN `Cookie` AS `c0` ON `c`.`CookieId` = `c0`.`CookieId`)
INNER JOIN `Backery` AS `b` ON `c`.`BackeryId` = `b`.`BackeryId`");
        }

        [ConditionalFact]
        public virtual void Create_many_to_many_table_with_inappropriate_counter_column_type()
        {
            using var context = CreateContext(
                model: builder =>
                {
                    builder.Entity<Cookie>(entity =>
                    {
                        entity.Property(e => e.CookieId)
                            .HasColumnType("int");
                        
                        entity.HasData(
                            new Cookie { CookieId = 1, Name = "Chocolate Chip" });
                    });

                    builder.Entity<Backery>(entity =>
                    {
                        entity.Property(e => e.BackeryId)
                            .HasColumnType("int");
                        
                        entity.HasData(
                            new Backery { BackeryId = 1, Name = "Bread & Cookies" });
                    });
            
                    builder.Entity<CookieBackery>(entity =>
                    {
                        entity.HasKey(e => new { e.CookieId, e.BackeryId });

                        entity.Property(e => e.CookieId)
                            .HasColumnType("counter");

                        entity.Property(e => e.BackeryId)
                            .HasColumnType("counter");

                        entity.HasOne(d => d.Cookie)
                            .WithMany()
                            .HasForeignKey(d => d.CookieId);

                        entity.HasOne(d => d.Backery)
                            .WithMany()
                            .HasForeignKey(d => d.BackeryId);
                        
                        entity.HasData(
                            new CookieBackery { CookieId = 1, BackeryId = 1 });
                    });
                });
            
            var cookieBackeries = context.Set<CookieBackery>()
                .Include(cb => cb.Cookie)
                .Include(cb => cb.Backery)
                .ToList();
            
            Assert.Single(cookieBackeries);
            Assert.Equal(1, cookieBackeries[0].Cookie.CookieId);
            Assert.Equal(1, cookieBackeries[0].Backery.BackeryId);

            AssertSql(
                $@"CREATE TABLE `Backery` (
    `BackeryId` counter NOT NULL,
    `Name` longchar NULL,
    CONSTRAINT `PK_Backery` PRIMARY KEY (`BackeryId`)
);


CREATE TABLE `Cookie` (
    `CookieId` counter NOT NULL,
    `Name` longchar NULL,
    `BestServedBefore` datetime NOT NULL,
    CONSTRAINT `PK_Cookie` PRIMARY KEY (`CookieId`)
);


CREATE TABLE `CookieBackery` (
    `CookieId` integer NOT NULL,
    `BackeryId` integer NOT NULL,
    CONSTRAINT `PK_CookieBackery` PRIMARY KEY (`CookieId`, `BackeryId`),
    CONSTRAINT `FK_CookieBackery_Backery_BackeryId` FOREIGN KEY (`BackeryId`) REFERENCES `Backery` (`BackeryId`) ON DELETE CASCADE,
    CONSTRAINT `FK_CookieBackery_Cookie_CookieId` FOREIGN KEY (`CookieId`) REFERENCES `Cookie` (`CookieId`) ON DELETE CASCADE
);


INSERT INTO `Backery` (`BackeryId`, `Name`)
VALUES (1, 'Bread & Cookies');


INSERT INTO `Cookie` (`CookieId`, `BestServedBefore`, `Name`)
VALUES (1, #1899-12-30#, 'Chocolate Chip');


INSERT INTO `CookieBackery` (`CookieId`, `BackeryId`)
VALUES (1, 1);


CREATE INDEX `IX_CookieBackery_BackeryId` ON `CookieBackery` (`BackeryId`);


SELECT `c`.`CookieId`, `c`.`BackeryId`, `c0`.`CookieId`, `c0`.`BestServedBefore`, `c0`.`Name`, `b`.`BackeryId`, `b`.`Name`
FROM (`CookieBackery` AS `c`
INNER JOIN `Cookie` AS `c0` ON `c`.`CookieId` = `c0`.`CookieId`)
INNER JOIN `Backery` AS `b` ON `c`.`BackeryId` = `b`.`BackeryId`");
        }

        private void AssertSql(string expected)
            => Assert.Equal(expected.Replace("\r\n", "\n"), Sql.Replace("\r\n", "\n"));

        public class Cookie
        {
            public int CookieId { get; set; }
            public string Name { get; set; }
            public DateTime BestServedBefore { get; set; }
        }

        public class Backery
        {
            public int BackeryId { get; set; }
            public string Name { get; set; }
        }

        public class CookieBackery
        {
            public int CookieId { get; set; }
            public int BackeryId { get; set; }

            public virtual Cookie Cookie { get; set; }
            public virtual Backery Backery { get; set; }
        }

        public class Context : ContextBase
        {
        }
    }
}