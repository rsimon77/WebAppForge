namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>
    /// Web/Db.cs of every generated site: the Dapper setup (type handlers for values a driver returns in another type than the
    /// model's) and the parameters of a WHERE clause built at run time.
    /// </summary>
    public static string DbRuntime(string ns) => ("""
        using System.Data;
        using System.Globalization;
        using Dapper;

        namespace @@NS@@.Web;

        /// <summary>Dapper setup and parameter helpers of the generated site.</summary>
        public static class Db
        {
            /// <summary>Registers the type handlers. Program.cs calls it once at startup.</summary>
            public static void Configure()
            {
                SqlMapper.AddTypeHandler(new TextHandler());
                SqlMapper.AddTypeHandler(new DateHandler());
                SqlMapper.AddTypeHandler(new IntegerHandler());
                SqlMapper.AddTypeHandler(new DecimalHandler());
                SqlMapper.AddTypeHandler(new BooleanHandler());
            }

            /// <summary>The parameters of a list built at run time (search, filters, grid tools, owner).</summary>
            public static DynamicParameters Args(IEnumerable<KeyValuePair<string, object>> parameters)
            {
                var args = new DynamicParameters();
                foreach (var (name, value) in parameters)
                {
                    args.Add(name, value is DBNull ? null : value);
                }

                return args;
            }

            /// <summary>
            /// Text fields read any value as invariant text: a SQL Server uniqueidentifier or time, a MySQL TIME or CHAR(36) that the
            /// driver returns as a Guid, or a number stored in a SQLite text column.
            /// </summary>
            private sealed class TextHandler : SqlMapper.TypeHandler<string>
            {
                public override string Parse(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

                public override void SetValue(IDbDataParameter parameter, string? value) => parameter.Value = (object?)value ?? DBNull.Value;
            }

            /// <summary>
            /// Number fields read any numeric value, and numeric text. SQLite keeps each value in the type it was stored with, so a
            /// DECIMAL column can hold an integer and an INTEGER column a real number. A handler for a value type also serves its
            /// nullable type.
            /// </summary>
            private sealed class IntegerHandler : SqlMapper.TypeHandler<long>
            {
                public override long Parse(object value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);

                public override void SetValue(IDbDataParameter parameter, long value) => parameter.Value = value;
            }

            private sealed class DecimalHandler : SqlMapper.TypeHandler<decimal>
            {
                public override decimal Parse(object value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

                public override void SetValue(IDbDataParameter parameter, decimal value) => parameter.Value = value;
            }

            /// <summary>Yes/no fields read 1 and 0 (any integer type), a bit or boolean, and the texts 1, 0, true and false.</summary>
            private sealed class BooleanHandler : SqlMapper.TypeHandler<bool>
            {
                public override bool Parse(object value) => value switch
                {
                    bool flag => flag,
                    string text => text.Trim() is "1" || text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                    _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
                };

                public override void SetValue(IDbDataParameter parameter, bool value) => parameter.Value = value;
            }

            /// <summary>Date fields also read a SQL Server datetimeoffset, as its local date and time.</summary>
            private sealed class DateHandler : SqlMapper.TypeHandler<DateTime>
            {
                public override DateTime Parse(object value) =>
                    value is DateTimeOffset offset ? offset.DateTime : Convert.ToDateTime(value, CultureInfo.InvariantCulture);

                public override void SetValue(IDbDataParameter parameter, DateTime value) => parameter.Value = value;
            }
        }
        """).Replace("@@NS@@", ns);
}
