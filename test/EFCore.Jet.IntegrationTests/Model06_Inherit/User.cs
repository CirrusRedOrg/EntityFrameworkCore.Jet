using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model06_Inherit
{
    public class User : BaseEntity
    {

        public string Username { get; set; }
        public string Passwd { get; set; }
        public string Firstname { get; set; }
        public string Middlename { get; set; }
        public string Secondname { get; set; }
        public DateTime BirthDate { get; set; }
        public string Email { get; set; }
        public string Mobilenumber { get; set; }
        public bool IsActivated { get; set; }
        public string ActivationCode { get; set; }
        public DateTime? CurrentCreateSession { get; set; }
        public DateTime? PreviousCreateSession { get; set; }
        public virtual Address Address { get; set; }
    }

}
