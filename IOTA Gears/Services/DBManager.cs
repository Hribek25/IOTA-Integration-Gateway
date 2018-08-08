using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace IOTA_Gears.Services
{
    public interface IDBManager
    {
        
    }

    public class DBManager : IDBManager
    {
        public SqliteConnectionStringBuilder ConnectionStringBuilder { get; private set; }
        public SqliteConnection DBConnection { get; private set; }
        private ILogger<DBManager> Logger { get; set; }

        public DBManager(SqliteConnectionStringBuilder connection, ILogger<DBManager> logger)
        {
            ConnectionStringBuilder = connection;
            Logger = logger;

            var initDB = false;

            if (!System.IO.File.Exists(ConnectionStringBuilder.DataSource)) // DB does not exist
            {
                initDB = true;
                Logger.LogWarning("DB file not found!");
            }

            DBConnection = new SqliteConnection(ConnectionStringBuilder.ConnectionString);
            DBConnection.Open();

            if (initDB | TestValidDB()==false) // some basic checks whether the DB is ok in terms of structure
            {
                try
                {
                    InitDB(); // let's create new DB structure                
                }
                catch (Exception e)
                {
                    Logger.LogError("DB could not be created", e);
                    throw;
                }                
            }
            Logger.LogInformation("DB LAYER ready... Using file: {DBConnection.DataSource}", DBConnection.DataSource);
            //Console.WriteLine($"DB prepared... File: {DBConnection.DataSource}");
            DBConnection.Close();
        }

        private bool TestValidDB()
        {
            var cmd = @"SELECT name FROM sqlite_master WHERE type='table'";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
                        
            try
            {
                using (var reader = c.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        return false;
                    }

                    var tables = new List<string>();
                    while (reader.Read())
                    {
                        tables.Add((string)reader["name"]);                     
                    }

                    if (!tables.Contains("cache"))
                    {
                        return false;
                    }
                } ;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private void InitDB()
        {
            var initscript =
@"DROP TABLE IF EXISTS [cache];
CREATE TABLE [cache] (  
 [timestamp] bigint  NOT NULL
, [query] text NOT NULL
, [response_type] text NOT NULL
, [response] text NOT NULL
);";
            var c = DBConnection.CreateCommand();
            c.CommandText = initscript;
                       
            try
            {
                c.ExecuteNonQuery();
            }
            catch (Exception)
            {                
                throw;
            }
            Logger.LogInformation("DB CREATED from scratch...");            
        }
        
        public JsonResult GetCacheEntry(string request, string contentType, int LifeSpan)
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
            DBConnection.Open();
            
            JsonResult cacheEntry = null; // default return value
            using (var reader = c.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["response"];
                    cacheEntry = new JsonResult(
                        Newtonsoft.Json.JsonConvert.DeserializeObject(
                            jsdata,
                            new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
                            )
                        );
                }
            }
            DBConnection.Close();
            if (cacheEntry != null)
            {
                Logger.LogInformation("Cache entry was loaded from cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
            }
            else
            {
                Logger.LogInformation("No cache entry found in cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
            }
            
            return cacheEntry;
        }

        public void AddCacheEntry(string request, JsonResult result, string contentType)
        {
            var cmd = "DELETE FROM [cache] WHERE [query]=@query and [response_type] like @contenttype;" + Environment.NewLine; // delete old entries from cache for the given query type/content type
            cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                result.Value,
                Newtonsoft.Json.Formatting.None,
                new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
                );

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

            DBConnection.Open();
            c.ExecuteNonQuery();
            DBConnection.Close();
            Logger.LogInformation("New entry to CACHE table for Request: {request}", request);
                        
            return ;
        }

        //private byte[] ObjectToByteArray(Object obj)
        //{
        //    using (var ms = new MemoryStream())
        //    {
        //        BinaryFormatter b = new BinaryFormatter();
        //        b.Serialize(ms, obj);
        //        return ms.ToArray();
        //    }
        //}

        //private object ByteArrayToObject(byte[] source)
        //{
        //    using (var ms = new MemoryStream(source, false))
        //    {
        //        BinaryFormatter b = new BinaryFormatter();
        //        return b.Deserialize(ms);
        //    }
        //}      
        
    }
}
