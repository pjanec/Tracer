using DuckDB.NET.Data;
using System.Reflection;
var connection = new DuckDBConnection("Data Source=:memory:");
connection.Open();
var cmd = connection.CreateCommand();
cmd.CommandText = "CREATE TABLE t (v BIGINT, ts TIMESTAMP_NS, s VARCHAR)";
cmd.ExecuteNonQuery();
var appender = connection.CreateAppender("t");
Console.WriteLine("Appender type: " + appender.GetType().FullName);
foreach (var m in appender.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine("  Method: " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");

var row = appender.CreateRow();
Console.WriteLine("\nRow type: " + row.GetType().FullName);
foreach (var m in row.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine("  Method: " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
