// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.



// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Provides CLR methods that get translated to database functions when used in LINQ to Entities queries.
    ///     The methods on this class are accessed via <see cref="EF.Functions" />.
    /// </summary>
    public static class JetDbFunctionsExtensions
    {
        /// <summary>
        ///     Counts the number of year boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(YEAR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of year boundaries crossed between the dates.</returns>
        public static int DateDiffYear(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

        /// <summary>
        ///     Counts the number of year boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(YEAR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of year boundaries crossed between the dates.</returns>
        public static int? DateDiffYear(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

        /// <summary>
        ///     Counts the number of year boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(YEAR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of year boundaries crossed between the dates.</returns>
        public static int DateDiffYear(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

        /// <summary>
        ///     Counts the number of year boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(YEAR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of year boundaries crossed between the dates.</returns>
        public static int? DateDiffYear(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

        /// <summary>
        ///     Counts the number of month boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MONTH,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of month boundaries crossed between the dates.</returns>
        public static int DateDiffMonth(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

        /// <summary>
        ///     Counts the number of month boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MONTH,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of month boundaries crossed between the dates.</returns>
        public static int? DateDiffMonth(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

        /// <summary>
        ///     Counts the number of month boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MONTH,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of month boundaries crossed between the dates.</returns>
        public static int DateDiffMonth(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

        /// <summary>
        ///     Counts the number of month boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MONTH,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of month boundaries crossed between the dates.</returns>
        public static int? DateDiffMonth(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

        /// <summary>
        ///     Counts the number of day boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(DAY,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of day boundaries crossed between the dates.</returns>
        public static int DateDiffDay(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

        /// <summary>
        ///     Counts the number of day boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(DAY,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of day boundaries crossed between the dates.</returns>
        public static int? DateDiffDay(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

        /// <summary>
        ///     Counts the number of day boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(DAY,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of day boundaries crossed between the dates.</returns>
        public static int DateDiffDay(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

        /// <summary>
        ///     Counts the number of day boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(DAY,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of day boundaries crossed between the dates.</returns>
        public static int? DateDiffDay(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the dates.</returns>
        public static int DateDiffHour(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the dates.</returns>
        public static int? DateDiffHour(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the dates.</returns>
        public static int DateDiffHour(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the dates.</returns>
        public static int? DateDiffHour(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the timespans.</returns>
        public static int DateDiffHour(
            this DbFunctions _,
            TimeSpan startTimeSpan,
            TimeSpan endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of hour boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(HOUR,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of hour boundaries crossed between the timespans.</returns>
        public static int? DateDiffHour(
            this DbFunctions _,
            TimeSpan? startTimeSpan,
            TimeSpan? endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the dates.</returns>
        public static int DateDiffMinute(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the dates.</returns>
        public static int? DateDiffMinute(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the dates.</returns>
        public static int DateDiffMinute(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the dates.</returns>
        public static int? DateDiffMinute(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the timespans.</returns>
        public static int DateDiffMinute(
            this DbFunctions _,
            TimeSpan startTimeSpan,
            TimeSpan endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of minute boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(MINUTE,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of minute boundaries crossed between the timespans.</returns>
        public static int? DateDiffMinute(
            this DbFunctions _,
            TimeSpan? startTimeSpan,
            TimeSpan? endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the dates.</returns>
        public static int DateDiffSecond(
            this DbFunctions _,
            DateTime startDate,
            DateTime endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the dates.</returns>
        public static int? DateDiffSecond(
            this DbFunctions _,
            DateTime? startDate,
            DateTime? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the dates.</returns>
        public static int DateDiffSecond(
            this DbFunctions _,
            DateTimeOffset startDate,
            DateTimeOffset endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startDate and endDate.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startDate">Starting date for the calculation.</param>
        /// <param name="endDate">Ending date for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the dates.</returns>
        public static int? DateDiffSecond(
            this DbFunctions _,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the timespans.</returns>
        public static int DateDiffSecond(
            this DbFunctions _,
            TimeSpan startTimeSpan,
            TimeSpan endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Counts the number of second boundaries crossed between the startTimeSpan and endTimeSpan.
        ///     Corresponds to Jet's DATEDIFF(SECOND,startDate,endDate).
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="startTimeSpan">Starting timespan for the calculation.</param>
        /// <param name="endTimeSpan">Ending timespan for the calculation.</param>
        /// <returns>Number of second boundaries crossed between the timespans.</returns>
        public static int? DateDiffSecond(
            this DbFunctions _,
            TimeSpan? startTimeSpan,
            TimeSpan? endTimeSpan)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

        /// <summary>
        ///     Validate if the given string is a valid date.
        ///     Corresponds to the Jet's ISDATE('date').
        /// </summary>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="expression">Expression to validate</param>
        /// <returns>true for valid date and false otherwise.</returns>
        public static bool IsDate(
            this DbFunctions _,
            string expression)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(IsDate)));

        public static double Random(
            this DbFunctions _)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(Random)));


        /// <summary>
        ///     Returns the length of <paramref name="byteArray"/>, or the length of <paramref name="byteArray"/> <c>-1</c> in some cases. If the actual length of <paramref name="byteArray"/> is even and the last byte is
        ///     <c>0x00</c>, the length of <paramref name="byteArray"/> <c>-1</c> is returned. Otherwise, the actual length of <paramref name="byteArray"/> is returned.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Jet SQL reads byte arrays into strings. As Jet uses Unicode, the internal length will always be a multiple of 2. If your data has an odd number of bytes, Jet internally adds a <c>0x00</c> byte to
        ///     the end of the array.
        ///     </para>
        ///     <para>
        ///     This method will test if the last byte of the array is <c>0x00</c>. If it is <c>0x00</c>, it is assumed that the last byte was added by Jet to fill the array to an even number of bytes and the internal
        ///     length <c>-1</c> is returned. In all other cases, the internal length is returned.
        ///     </para>
        ///     <para>
        ///     If the actual data length is odd, this method will always return the original length, independent of the value of the last byte of the original data.
        ///     <br/>
        ///     If the actual data length is even and the original data does not end with a <c>0x00</c> byte, this method will return the original length.
        ///     <br/>
        ///     If the actual data length is even and the original data does end with a <c>0x00</c> byte, this method will return the original length <c>-1</c>.
        ///     </para>
        ///     <para>
        ///     If your data will never end in <c>0x00</c> you can use this extension method safely, otherwise it is highly recommended to only use client evaluation.
        ///     </para>
        /// </remarks>
        /// <param name="_">The DbFunctions instance.</param>
        /// <param name="byteArray">The `byte[]` array.</param>
        /// <returns>The length of <paramref name="byteArray"/>, or the length of <paramref name="byteArray"/> <c>-1</c> in some cases.</returns>
        public static int ByteArrayLength(
            this DbFunctions _,
            byte[] byteArray)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ByteArrayLength)));



        #region Sample standard deviation

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<byte> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<short> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<int> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<long> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<float> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<double> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        /// <summary>
        ///     Returns the sample standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDev</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample standard deviation.</returns>
        public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<decimal> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

        #endregion Sample standard deviation

        #region Population standard deviation

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<byte> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<short> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<int> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<long> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<float> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<double> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        /// <summary>
        ///     Returns the population standard deviation of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>StDevP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population standard deviation.</returns>
        public static double? StandardDeviationPopulation(this DbFunctions _, IEnumerable<decimal> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationPopulation)));

        #endregion Population standard deviation

        #region Sample variance

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<byte> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<short> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<int> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<long> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<float> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<double> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        /// <summary>
        ///     Returns the sample variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>Var</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed sample variance.</returns>
        public static double? VarianceSample(this DbFunctions _, IEnumerable<decimal> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VarianceSample)));

        #endregion Sample variance

        #region Population variance

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<byte> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<short> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<int> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<long> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<float> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<double> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        /// <summary>
        ///     Returns the population variance of all values in the specified expression.
        ///     Corresponds to Jet/MS Access <c>VarP</c>.
        /// </summary>
        /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
        /// <param name="values">The values.</param>
        /// <returns>The computed population variance.</returns>
        public static double? VariancePopulation(this DbFunctions _, IEnumerable<decimal> values)
            => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VariancePopulation)));

        #endregion Population variance
    }
}
