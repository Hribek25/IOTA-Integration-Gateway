using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Data.HashFunction;
using System.Linq;
using System.Data.HashFunction.xxHash;

namespace IOTAGears.Services
{
    public interface IFsStorageManager
    {
        Logger<FsStorageManager> Logger { get;}
        IConfiguration Configuration { get; }
        IxxHash HashFunction { get; }
    }
    
    public class FsStorageManager : IFsStorageManager
    {        
        public Logger<FsStorageManager> Logger { get; }
        public IConfiguration Configuration { get; }
        public IxxHash HashFunction { get; }
        private string CacheBasePath { get; } = Program.CacheBasePath();
        private string CacheElementsBasePath { get; } = Program.CacheElementsBasePath();

        public FsStorageManager(ILogger<FsStorageManager> logger, IConfiguration conf, IxxHash hashprovider)
        {
            Configuration = conf;
            Logger = (Logger<FsStorageManager>)logger;
            HashFunction = hashprovider;            

            Logger.LogInformation("File System Storage initiated and ready... Using Cache Dir: {CacheBasePath}, Cache Elements Dir: {CacheElementsBasePath}", CacheBasePath, CacheElementsBasePath );
        }

        private string GetCacheSubDir(string subDir)
        {            
            var target = Path.Combine(CacheBasePath, subDir);
            if (Directory.Exists(target))
            {
                return target;
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception e)
                {
                    Logger.LogError("Can't create sub cache directory {target}. Error {e}", target, e.Message);
                    return null;
                }
                return target;
            }
        }
        private string GetElementCacheSubDir(string subDir)
        {
            var target = Path.Combine(CacheElementsBasePath, subDir);
            if (Directory.Exists(target))
            {
                return target;
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception e)
                {
                    Logger.LogError("Can't create sub cache directory {target}. Error {e}", target, e.Message);
                    return null;
                }
                return target;
            }
        }
        private string CacheEntryFingerPrint(string request, string contentType)
        {
            var cnttype = contentType.Split(";")[0].Replace(" ","", StringComparison.InvariantCulture); // this is workqround in case there is also encoding specified in content type
            var callerid = (request + cnttype).ToUpperInvariant();
            var hsh = this.HashFunction.ComputeHash(callerid).AsHexString();
            return hsh;
        }
        private string CacheElementEntryFingerPrint(string callerId)
        {            
            var callerid = callerId.ToUpperInvariant();
            var hsh = this.HashFunction.ComputeHash(callerid).AsHexString();
            return hsh;
        }

        public async Task<JsonResult> GetFSCacheEntryAsync(string request, string contentType, int LifeSpan)
        {
            var hashcallerid = CacheEntryFingerPrint(request, contentType);            
            var targetDir = GetCacheSubDir(hashcallerid.Substring(0,2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hashcallerid.Substring(2)); // target file incl full path 

            JsonResult cacheEntry = null; // default return value
            if (File.Exists(targetFile)) // there is cache entry
            {
                TimeSpan actualSpan = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetFile);
                if (actualSpan.TotalSeconds <= LifeSpan) // cache entry is still quite fresh, so leveraging it
                {
                    var jsdata = await File.ReadAllTextAsync(targetFile);
                    var tmp = JsonSerializer.DeserializeFromJson(jsdata);
                    cacheEntry = new JsonResult(tmp);
                }                 
            }            
            return cacheEntry;
        }
        public async Task AddFSCacheEntryAsync(string request, object result, string contentType)
        {
            var hashcallerid = CacheEntryFingerPrint(request, contentType);
            var targetDir = GetCacheSubDir(hashcallerid.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hashcallerid.Substring(2)); // target file incl full path 

            string json = null;
            if (result is ObjectResult)
            {
                json = JsonSerializer.SerializeToJson((result as ObjectResult).Value);
            }
            if (result is JsonResult)
            {
                json = JsonSerializer.SerializeToJson((result as JsonResult).Value);
            }

            if (!(json is null))
            {
                await File.WriteAllTextAsync(targetFile, json);
            }            
        }
        public async Task<object> GetFSPartialCacheEntryAsync(string call)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            object OutputCacheEntry = null;

            if (File.Exists(targetFile)) // there is cache entry
            {
                var jsdata = await File.ReadAllTextAsync(targetFile);
                OutputCacheEntry = JsonSerializer.DeserializeFromJson(jsdata);
            }

            Logger.LogInformation("Partial cache used (GET) for individual element. {OutputCacheEntry?.GetType()} was loaded for the caller: {call}.", OutputCacheEntry?.GetType(), call.Substring(0, 50));
            return OutputCacheEntry;
        }

        public async Task AddFSPartialCacheEntryAsync(string call, object result)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            var json = JsonSerializer.SerializeToJson(result);
            await File.WriteAllTextAsync(targetFile, json);            

            Logger.LogInformation("Partial cache used (ADD) for an individual element. {result.GetType()} object was saved for the caller {call}.", result.GetType(), call.Substring(0, 50));
        }
        
        public async Task<Dictionary<string, object>> GetFSPartialCacheEntriesAsync(string call)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            Dictionary<string, object> OutputCacheEntry = null;

            if (File.Exists(targetFile)) // there is cache entry
            {
                var jsdata = await File.ReadAllTextAsync(targetFile);
                OutputCacheEntry = (Dictionary<string, object>)JsonSerializer.DeserializeFromJson(jsdata);
            }
            
            Logger.LogInformation("Partial cache used (GET) for multiple elements. {OutputCacheEntry.Count} records were loaded for the caller {call}.", OutputCacheEntry.Count, call.Substring(0, 50));
            return OutputCacheEntry.Count == 0 ? null : OutputCacheEntry;
        }
        public async Task AddFSPartialCacheEntriesAsync(string call, IEnumerable<object> results, Func<object, string> identDelegate)
        {
            if (identDelegate == null)
            {
                throw new ArgumentNullException(paramName: nameof(identDelegate));
            }

            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            Dictionary<string, object> elements = (from i in results select i ).ToDictionary(a => identDelegate(a), b => b);
            
            var json = JsonSerializer.SerializeToJson(elements);
            await File.WriteAllTextAsync(targetFile, json);
            
            Logger.LogInformation("Partial cache used (ADD) for multiple elements. {elements.Count} records were saved for the caller {call}.", elements.Count, call.Substring(0, 50));
        }
        
    }
}
