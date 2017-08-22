using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model61_StackOverflow_Seed
{

    public class ClassRoom
    {
        public ClassRoom()
        {
            this.ClassSchedules = new HashSet<ClassSchedule>();
        }

        public int Id { get; set; }
        public string ClassRoomName { get; set; }
        public int MaxStudents { get; set; }

        public ICollection<ClassSchedule> ClassSchedules { get; set; }
    }
    public class ClassSchedule
    {
        public int Id { get; set; }
        public string ScheduleName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public virtual ClassRoom ClassRooms { get; set; }
    }
}
