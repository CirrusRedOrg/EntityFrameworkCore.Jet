using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model39_DetachedEntities
{
    public abstract class Test : TestBase<MyContext>
    {

        [TestMethod]
        public void Model39_DetachedEntitiesRun1()
        {
            base.DisposeContext();
            base.CreateContext();

            if (!Context.Grades.Any(_ => _.Id == 1))
            {
                Context.Grades.Add(new Grade()
                {
                    Id = 1,
                    Quantity = 50,
                    Name = "Dont care",
                    GradeWidths = new List<GradeWidth>(
                    [
                        new GradeWidth() {Width = 10},
                        new GradeWidth() {Width = 20},
                        new GradeWidth() {Width = 30}
                    ])
                });
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Grade grade = new() {Id = 1, Quantity = 50, Name = "Dont care"};
                UpdateQuantity(Context, grade);
            }
        }

        private static void UpdateQuantity(MyContext context, object f)
        {
            context.Attach(f);
            var be = context.Entry(f);
            be.Property("Quantity").IsModified = true;

            context.SaveChanges();
        }

        [TestMethod]
        public void Model39_DetachedEntitiesRun2()
        {

            base.DisposeContext();
            base.CreateContext();

            {
                Context.Grades.RemoveRange(Context.Grades);
                Context.GradeWidths.RemoveRange(Context.GradeWidths);
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                Context.Grades.Add(new Grade()
                {
                    Id = 1, 
                    Quantity = 50, 
                    Name = "Dont care",
                    GradeWidths = new List<GradeWidth>(
                    [
                        new GradeWidth() {Width = 10},
                        new GradeWidth() {Width = 20},
                        new GradeWidth() {Width = 30}
                    ])
                });
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                int gradeId = Context.Grades.First().Id;
                Grade grade = Context.Grades.Include(g => g.GradeWidths).Where(_ => _.Id == gradeId).AsNoTracking().First();

                // We need to reset all the ids
                grade.Id = 0;
                foreach (GradeWidth gradeWidth in grade.GradeWidths)
                    gradeWidth.Id = 0;


                Context.Grades.Add(grade);
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();


            {
                Assert.AreEqual(2, Context.Grades.Count());
                Assert.AreEqual(6, Context.GradeWidths.Count());
            }

        }

    }
}
