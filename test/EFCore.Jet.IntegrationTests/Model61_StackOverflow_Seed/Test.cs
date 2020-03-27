using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model61_StackOverflow_Seed
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            using (var context = new Context(GetConnection()))
            {
// all classrooms
                context.ClassRooms.AddOrUpdate(
                    cl => cl.ClassRoomName,
                    new ClassRoom
                    {
                        ClassRoomName = "Cherry",
                        MaxStudents = 20
                    }
                );

                context.SaveChanges();


                context.ClassSchedules.AddOrUpdate(
                    cls => cls.ScheduleName,
                    new ClassSchedule
                    {
                        ScheduleName = "Yippie red class",
                        StartTime = DateTime.Parse("2017-09-17T18:00:00"),
                        EndTime = DateTime.Parse("2017-09-17T20:00:00"),
                        ClassRooms = context.ClassRooms.FirstOrDefault(x => x.ClassRoomName == "Cherry")
                    }
                );

                var schedules = context.ClassRooms.First().ClassSchedules.ToList();
                Assert.AreEqual(1, schedules.Count);
            }
        }
    }
}
