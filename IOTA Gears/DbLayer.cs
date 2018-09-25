using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace IOTAGears
{
    public static class DbLayer
    {
        private static bool IsValidDBLayer(DbConnection connection)
        {
            string cmd;
            if (connection is SqliteConnection)
            {
                cmd = @"SELECT name FROM sqlite_master WHERE type='table'";
            }
            else
            {
                cmd = @"SHOW TABLES;";
            }            

            var c = connection.CreateCommand();
            c.CommandText = cmd;

            try
            {
                connection.Open();
                using (var reader = c.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        connection.Close();
                        return false;
                    }

                    var tables = new List<string>();
                    while (reader.Read())
                    {
                        tables.Add((string)reader[0]); // absolute indexing since it may differ based on the provider
                    }
                    connection.Close();

                    if (!tables.Contains("cache") || !tables.Contains("partial_cache") || !tables.Contains("task_pipeline"))
                    {                        
                        return false;
                    }
                };
            }
            catch (Exception)
            {
                connection.Close();
                return false;
            }
            connection.Close();
            return true;
        }

        private static bool InitDB(DbConnection connection, DbLayerProvider provider)
        {

            string initscript;
            if (provider==DbLayerProvider.Sqlite)
            {
                initscript =
    @"DROP TABLE IF EXISTS cache;
CREATE TABLE cache (  
 timestamp bigint  NOT NULL
, query text NOT NULL
, response_type text NOT NULL
, response text NOT NULL
);

DROP TABLE IF EXISTS partial_cache;
CREATE TABLE partial_cache (
timestamp bigint NOT NULL
, `call` TEXT NOT NULL
, ident TEXT NOT NULL
, EntityTimestamp bigint
, result TEXT NOT NULL);


DROP TABLE IF EXISTS task_pipeline;
CREATE TABLE task_pipeline (
  timestamp bigint NOT NULL
, task text NOT NULL
, input text NULL
, result text NULL
, performed int  NOT NULL
, performed_when bigint NULL
, ip text NULL
, guid text NULL);
";
            }
            else
            {
                initscript =
    @"DROP TABLE IF EXISTS cache;
CREATE TABLE cache (  
 timestamp BIGINT  NOT NULL
, query VARCHAR(255) NOT NULL
, response_type VARCHAR(255) NOT NULL
, response LONGTEXT NOT NULL
);

DROP TABLE IF EXISTS partial_cache;
CREATE TABLE partial_cache (
timestamp BIGINT NOT NULL
, `call` VARCHAR(255) NOT NULL
, ident VARCHAR(255) NOT NULL
, EntityTimestamp BIGINT
, result MEDIUMTEXT NOT NULL);


DROP TABLE IF EXISTS task_pipeline;
CREATE TABLE task_pipeline (
  timestamp BIGINT NOT NULL
, task VARCHAR(50) NOT NULL
, input TEXT NULL
, result VARCHAR(255) NULL
, performed INT NOT NULL
, performed_when BIGINT NULL
, ip VARCHAR(100) NULL
, guid VARCHAR(100) NULL);
";
            }
            
            var c = connection.CreateCommand();
            c.CommandText = initscript;
            var res = false;

            connection.Open();
            try
            {
                c.ExecuteNonQuery();
                res = true;
            }
            catch (Exception)
            {
                res = false;
            }
            finally
            {
                connection.Close();
            }

            Console.WriteLine($"DB CREATED from scratch... Location: {connection.DataSource}");
            return res;
        }

        public static bool IsDBLayerReady(string connectionstring = null, DbLayerProvider provider = DbLayerProvider.Sqlite)
        {
            var res = false;
            var SqliteDbBasePath = Program.SqliteDbLayerDataSource();
            var CacheFolderPath = Program.CacheBasePath();           

            var connStr = connectionstring;
            var initDB = false;

            // Preparing cache folder
            if (!System.IO.Directory.Exists(CacheFolderPath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(CacheFolderPath);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Cache directory can't be created! Error: {e.Message}");
                    return false;
                }
            }

            // Trying write access to that folder
            try
            {
                var TestFile = System.IO.Path.Combine(CacheFolderPath, "writable.test");
                System.IO.File.WriteAllText(TestFile, "test");                
            }
            catch (Exception e)
            {
                Console.WriteLine($"It seems cache directory is not writable! Error: {e.Message}");
                return false;
            }

            // Preparing cache folders

            if (provider == DbLayerProvider.Sqlite)
            {
                connStr = "Data Source=" + SqliteDbBasePath;
                if (!System.IO.File.Exists(SqliteDbBasePath)) // Sqlite DB file does not exist
                {
                    initDB = true;
                    Console.WriteLine($"Sqlite DB file not found! Location: {SqliteDbBasePath}");
                }
            }
            else // Other than Sqlite Provider
            {
                if (connStr is null)
                {
                    Console.WriteLine($"Connection string is missing for non-Sqlite DB! provider: {provider}");
                    return false; // Connection string is missing for non Sqlite DB
                }
            }

            DbConnection DbConn; // Common DbConnection interface
            if (provider==DbLayerProvider.Sqlite)
            {
                DbConn = new SqliteConnection(connStr);
            }
            else
            {
                DbConn = new MySqlConnection(connStr);
            }

            using (DbConn)
            {
                var validDB = false;

                if (initDB == false)
                {
                    validDB = IsValidDBLayer(DbConn);
                }

                if (initDB || !validDB ) // some basic checks whether the DB is ok in terms of structure
                {
                    if (InitDB(DbConn, provider)) // let's create new DB from scratch 
                    {
                        res = true;
                    }
                    else
                    {
                        res = false;
                    }
                }
                else
                {
                    res = true;
                }                
            }            
            return res;
        }
    }
}
