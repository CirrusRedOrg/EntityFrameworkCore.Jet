using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model28
{
    [Table("Users28")]
    public class User : AbpUser<Tenant, User>
    { // AbpUser is a 3rd party class which defines Id as a primary key

        public string AccessToken { get; set; }
        public int UserId { get; set; }

        public virtual List<Advertisement> Advertisements { get; set; }
    }

    public class Advertisement : Entity
    {
        [Key]
        public int Id { get; set; }

        public string Title { get; set; }
        public string Message { get; set; }
        public List<AdImage> AdImages { get; set; }

        public virtual User User { get; set; }
    }

    public class AdImage : Entity
    {
        [Key]
        public int Id { get; set; }
        public string Image { get; set; }

        public virtual Advertisement Advertisement { get; set; }
    }

    public class Entity
    {
        
    }

    [Table("Tenants28")]
    public class Tenant
    {
        
    }

    public class AbpUser<T1, T2>
    {
        
    }

}
