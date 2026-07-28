// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.TestModels;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class MonsterFixupChangedChangingLibRedTest(
        MonsterFixupChangedChangingLibRedTest.MonsterFixupChangedChangingLibRedFixture fixture)
        :
            MonsterFixupTestBase<MonsterFixupChangedChangingLibRedTest.MonsterFixupChangedChangingLibRedFixture>(fixture)
    {
        public class MonsterFixupChangedChangingLibRedFixture : MonsterFixupChangedChangingFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

            public override MonsterContext CreateContext(DbContextOptions options)
            {
                var context = base.CreateContext(options);

                void DropUnsupportedPartialNullCompositeForeignKey(object? sender, SavingChangesEventArgs eventArgs)
                {
                    context.SavingChanges -= DropUnsupportedPartialNullCompositeForeignKey;
                    context.Database.ExecuteSql(
                        $"ALTER TABLE `ProductWebFeature` DROP CONSTRAINT `FK_ProductWebFeature_ProductPhoto_PhotoId_ProductId`");
                }

                context.SavingChanges += DropUnsupportedPartialNullCompositeForeignKey;
                return context;
            }

            protected override void OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(
                ModelBuilder builder)
            {
                base.OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(builder);

                builder.Entity<TMessage>().Property(e => e.MessageId).UseJetIdentityColumn();

                builder.Entity<TProduct>()
                    .OwnsOne(
                        c => (TDimensions)c.Dimensions, db =>
                        {
                            db.Property(d => d.Depth).HasColumnType("decimal(18,2)");
                            db.Property(d => d.Width).HasColumnType("decimal(18,2)");
                            db.Property(d => d.Height).HasColumnType("decimal(18,2)");
                        });

                builder.Entity<TProductPhoto>().Property(e => e.PhotoId).UseJetIdentityColumn();
                builder.Entity<TProductReview>().Property(e => e.ReviewId).UseJetIdentityColumn();

                builder.Entity<TComputerDetail>()
                    .OwnsOne(
                        c => (TDimensions)c.Dimensions, db =>
                        {
                            db.Property(d => d.Depth).HasColumnType("decimal(18,2)");
                            db.Property(d => d.Width).HasColumnType("decimal(18,2)");
                            db.Property(d => d.Height).HasColumnType("decimal(18,2)");
                        });
            }
        }
    }
}
