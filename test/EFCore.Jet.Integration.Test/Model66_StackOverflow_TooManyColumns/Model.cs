using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model66_StackOverflow_TooManyColumns
{
    [Table("MyFlashCard")]
    public partial class MyFlashCard
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public MyFlashCard()
        {
            MyFlashCardPics = new HashSet<MyFlashCardPic>();
        }
        public int Id { get; set; }

        public int? FaslID { get; set; }

        public virtual FasleManJdl FasleManJdl { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<MyFlashCardPic> MyFlashCardPics { get; set; }
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
