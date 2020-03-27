using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model48_Cast
{
    [Table("Entities48")]
    public class Entity
    {
        public string Id { get; set; }
        public int? Number { get; set; }
    }
}
