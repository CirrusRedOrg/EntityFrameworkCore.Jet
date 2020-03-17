using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model40_HardMapping
{
    public abstract class EntityWhichCanBeOwned
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Owner")]
        public int? OwnerId { get; set; }

        public virtual User Owner { get; set; }
    }

    public class Car : EntityWhichCanBeOwned
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public virtual ICollection<User> Owners { get; set; }
    }

    public class Dog : EntityWhichCanBeOwned
    {
        public string Name { get; set; }
        public string BarkingText { get; set; }
        public virtual ICollection<User> Owners { get; set; }
    }

    public class User : EntityWhichCanBeOwned // yes, owner can be owned
    {
        public virtual ICollection<Dog> OwnedDogs { get; set; }
        public virtual ICollection<Car> OwnedCars { get; set; }
        public virtual Dog FavouriteDog { get; set; }
        public virtual Dog MostHatedDog { get; set; }
        public virtual Car CurrentlyUsedCar { get; set; }
    }

}
