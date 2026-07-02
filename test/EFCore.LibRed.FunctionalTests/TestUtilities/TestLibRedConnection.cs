// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class TestLibRedConnection(RelationalConnectionDependencies dependencies) : LibRedRelationalConnection(dependencies)
    {
        private readonly Func<int, DbException> _createExceptionFunc = null!;

        public int ErrorNumber { get; set; } = -2;
        public Queue<bool?> OpenFailures { get; } = new();
        public int OpenCount { get; set; }
        public Queue<bool?> CommitFailures { get; } = new();
        public Queue<bool?> ExecutionFailures { get; } = new();
        public int ExecutionCount { get; set; }

        public override bool Open(bool errorsExpected = false)
        {
            PreOpen();

            return base.Open(errorsExpected);
        }

        public override Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
        {
            PreOpen();

            return base.OpenAsync(cancellationToken, errorsExpected);
        }

        private void PreOpen()
        {
            if (DbConnection.State == ConnectionState.Open)
            {
                return;
            }

            OpenCount++;
            if (OpenFailures.Count <= 0)
            {
                return;
            }

            var fail = OpenFailures.Dequeue();

            if (fail.HasValue)
            {
                throw _createExceptionFunc(ErrorNumber);
            }
        }
    }
}
