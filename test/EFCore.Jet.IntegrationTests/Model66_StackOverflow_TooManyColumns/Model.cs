﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model66_StackOverflow_TooManyColumns
{
    [Table("MyFlashCard")]
    [method: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public partial class MyFlashCard()
    {
        public int Id { get; set; }

        public int? FaslID { get; set; }

        public virtual FasleManJdl FasleManJdl { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<MyFlashCardPic> MyFlashCardPics { get; set; } = [];
    }

    public class FasleManJdl
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Description { get; set; }
    }

    [Table("MyFlashCardPic")]
    public partial class MyFlashCardPic
    {
        public int Id { get; set; }

        [ForeignKey("MyFlashCard")]
        public int MyFlashCardId { get; set; }

        public virtual MyFlashCard MyFlashCard { get; set; }
    }

}
