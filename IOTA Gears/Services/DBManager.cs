using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Threading.Tasks;

namespace IOTA_Gears.Services
{
    public interface IDBManager
    {
        
    }

    public class DBManager : IDBManager
    {
        public SqliteConnection DBConnection { get; private set; }
        private ILogger<DBManager> Logger { get; set; }

        public DBManager(SqliteConnection connection, ILogger<DBManager> logger)
        {
            Logger = logger;
            DBConnection = connection;                        
            Logger.LogInformation("DB LAYER ready... Using file: {DBConnection.DataSource}", DBConnection.DataSource);            
        }

        public async Task<JsonResult> GetCacheEntryAsync(string request, string contentType, int LifeSpan)
        {
            SqliteCommand c = _GetCacheEntrySQL(request, contentType, LifeSpan);
            DBConnection.Open();

            JsonResult cacheEntry = null; // default return value
            using (var reader = await c.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["response"];
                    cacheEntry = new JsonResult(DBSerializer.DeserializeFromJson(jsdata));
                }
            }
            DBConnection.Close();
            return cacheEntry;
        }
        public async Task AddCacheEntryAsync(string request, JsonResult result, string contentType)
        {
            SqliteCommand c = _AddCacheEntrySQL(request, result, contentType);

            DBConnection.Open();
            await c.ExecuteNonQueryAsync();
            DBConnection.Close();
            
        }
        
        public async Task AddPartialCacheEntriesAsync(string call, object input, IEnumerable<object> results)
        {
            var inputcmd = _AddPartialCacheInputEntrySQL(call, input);

            DBConnection.Open();
            
            using (var tr = DBConnection.BeginTransaction())
            {
                inputcmd.Transaction = tr;
                await inputcmd.ExecuteNonQueryAsync();

                foreach (var i in _AddPartialCacheOutputEntriesSQL(call, results))
                {
                    i.Transaction = tr;
                    await i.ExecuteNonQueryAsync();
                }
                tr.Commit();
            }            
            DBConnection.Close();
        }
        public async Task AddPartialCacheEntryAsync(string call, object input, object result)
        {
            var inputcmd = _AddPartialCacheInputEntrySQL(call, input);
            var outputcmd = _AddPartialCacheOutputEntrySQL(call, result);

            DBConnection.Open();
            using (var tr = DBConnection.BeginTransaction())
            {
                inputcmd.Transaction = tr;
                outputcmd.Transaction = tr;
                await inputcmd.ExecuteNonQueryAsync();
                await outputcmd.ExecuteNonQueryAsync();
                tr.Commit();
            }           
            
            DBConnection.Close();
        }

        public async Task<Tuple<object, IEnumerable<object>>> GetPartialCacheEntriesAsync(string call)
        {
            var inputcmd = _GetPartialCacheInputEntrySQL(call);
            var outputcmd = _GetPartialCacheOutputEntrySQL(call);

            DBConnection.Open();

            object InputCacheEntry = null;
            using (var reader = await inputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["input"];
                    InputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                }
            }

            
            var result = new List<object>();

            using (var reader = await outputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        var jsdata = (string)reader["result"];
                        var OutputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                        result.Add(OutputCacheEntry);
                    }
                }
            }

            DBConnection.Close();

            return new Tuple<object, IEnumerable<object>>(InputCacheEntry, result);
        }
        public async Task<Tuple<object, object>> GetPartialCacheEntryAsync(string call)
        {
            var inputcmd = _GetPartialCacheInputEntrySQL(call);
            var outputcmd = _GetPartialCacheOutputEntrySQL(call);

            DBConnection.Open();

            object InputCacheEntry = null;
            using (var reader = await inputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["input"];
                    InputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                }
            }

            object OutputCacheEntry = null;
            using (var reader = await outputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["result"];
                    OutputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                }
            }
            
            DBConnection.Close();

            return new Tuple<object, object>(InputCacheEntry, OutputCacheEntry);
        }
        
        #region SQLHelpers
        private SqliteCommand _AddCacheEntrySQL(string request, JsonResult result, string contentType)
        {
            var cmd = "DELETE FROM [cache] WHERE [query]=@query and [response_type] like @response_type;" + Environment.NewLine; // delete old entries from cache for the given query type/content type
            cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json = DBSerializer.SerializeToJson(result.Value);

            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@query",request),
                    //new SqliteParameter("@span",LifeSpan),
                    new SqliteParameter("@response_type",contentType),
                    new SqliteParameter("@response", json)
                    //new SqliteParameter("@data", SqliteType.Blob,binObj.Length ) { Value=binObj}
                }
            );
            return c;
        }
        private SqliteCommand _GetCacheEntrySQL(string request, string contentType, int LifeSpan)
        {
            var cmd = "SELECT * FROM [cache] WHERE (cast(strftime('%s','now') as bigint)-cast([timestamp] as bigint))<=@span and [query]=@query and [response_type] like @contenttype";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@span",LifeSpan),
                    new SqliteParameter("@contenttype",contentType + "%"),
                    new SqliteParameter("@query",request)
                }
            );
            return c;
        }

        private SqliteCommand _AddPartialCacheOutputEntrySQL(string call, object result)
        {
            var cmd = "INSERT INTO [partial_cache_out] ([timestamp], [call], [result]) VALUES (strftime('%s','now'), @call, @result)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result); 
            
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call),
                    new SqliteParameter("@result",json_result)                    
                }
            );
            return c;
        }
        private IEnumerable<SqliteCommand> _AddPartialCacheOutputEntriesSQL(string call, IEnumerable<object> results)
        {
            var commands = new List<SqliteCommand>();
            foreach (var i in results)
            {
                commands.Add(
                    _AddPartialCacheOutputEntrySQL(
                        call: call,
                        result: i)
                    );
            }
            return commands;
        }

        private SqliteCommand _AddPartialCacheInputEntrySQL(string call, object input)
        {
            var cmd = "DELETE FROM [partial_cache_in] WHERE [call]=@call;" + System.Environment.NewLine; // Deleting every attempt to write to cache - it provides me a timestamp where the given call was made
            cmd += "INSERT INTO [partial_cache_in] ([timestamp], [call], [input]) VALUES (strftime('%s','now'), @call, @input)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_input = DBSerializer.SerializeToJson(input);

            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call),
                    new SqliteParameter("@input",json_input)
                }
            );
            return c;
        }

        private SqliteCommand _GetPartialCacheOutputEntrySQL(string call)
        {
            var cmd = "SELECT * FROM [partial_cache_out] WHERE [call]=@call ORDER BY datetime([timestamp]) DESC";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call)
                }
            );
            return c;
        }
        private SqliteCommand _GetPartialCacheInputEntrySQL(string call)
        {
            var cmd = "SELECT * FROM [partial_cache_in] WHERE [call]=@call";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call)
                }
            );
            return c;
        }
        #endregion

    }
}
