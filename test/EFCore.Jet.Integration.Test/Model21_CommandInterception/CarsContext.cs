using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model21_CommandInterception
{
    public class CarsContext : DbContext
    {
        private readonly IDbInterceptor _dbInterceptor;
        private readonly IDbInterceptor _dbTreeInterceptor;

        public CarsContext(DbConnection connection)
            : base(new DbContextOptionsBuilder<CarsContext>().UseJet(connection).Options)
        {
            _dbInterceptor = new DbCommandInterceptor();
            _dbTreeInterceptor = new DbCommandTreeInterceptor();
            DbInterception.Add(_dbInterceptor);
            DbInterception.Add(_dbTreeInterceptor);
        }

        // For migration test
        public CarsContext()
        { }

        public DbSet<Car> Cars { get; set; }


        public override void Dispose()
        {
            DbInterception.Remove(_dbInterceptor);
            DbInterception.Remove(_dbTreeInterceptor);
        }
    }
}
