using DuckDB.NET.Data;
var connection = new DuckDBConnection("Data Source=:memory:");
connection.Open();
connection.CreateCommand().CommandText = "CREATE TABLE t (v BIGINT)";
var cmd = connection.CreateCommand();
cmd.CommandText = "CREATE TABLE t (v BIGINT)";
cmd.ExecuteNonQuery();
var appender = connection.CreateAppender("t");
Console.WriteLine(appender.GetType().FullName);
foreach (var m in appender.GetType().GetMethods()) {
    Console.WriteLine(m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
}
