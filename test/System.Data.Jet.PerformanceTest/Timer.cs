using System;

namespace System.Data.Jet.PerformanceTest
{
    class Timer : IDisposable
    {
        private readonly string _actionName;
        private DateTime _startTime;
        public Timer(string actionName, bool showStartTime = false)
        {
            if (actionName == null) throw new ArgumentNullException(nameof(actionName));

            _actionName = actionName;
            _startTime = DateTime.Now;
            if (showStartTime)
                Console.WriteLine($"{_actionName} starting at {_startTime}");
        }

        public void Dispose()
        {
            if (_startTime == DateTime.MinValue)
                throw new InvalidOperationException($"Timer for action '{_actionName}' already disposed");

            DateTime endTime = DateTime.Now;

            Console.WriteLine($"{_actionName} started at {_startTime} is finishing at {endTime} after {(endTime-_startTime).TotalSeconds}");

            _startTime = DateTime.MinValue;
        }
    }
}
