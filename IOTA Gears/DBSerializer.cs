using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tangle.Net.Entity;

namespace IOTA_Gears
{
    #region JsonConverters

    class TangleAddressConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(Tangle.Net.Entity.Address));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the JSON for the Result into a JObject
            JObject jo = JObject.Load(reader);

            // Read the properties which will be used as constructor parameters
            string value = (string)jo["Value"];
            string checksum = "";
            if (jo["Checksum"]["Value"].Type == JTokenType.String)
            {
                checksum = (string)jo["Checksum"]["Value"];
            }

            // Construct the Result object using the non-default constructor
            Address result = new Address(value + checksum)
            {
                // (If anything else needs to be populated on the result object, do that here)
                Balance = (long)jo["Balance"],
                KeyIndex = (int)jo["KeyIndex"],
                SpentFrom = (bool)jo["SpentFrom"]
            };

            // Return the result
            return result;
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
    class TangleTryteStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(Tangle.Net.Entity.TryteString));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the JSON for the Result into a JObject
            JObject jo = JObject.Load(reader);

            // Read the properties which will be used as constructor parameters
            string value = (string)jo["Value"];

            // Construct the Result object using the non-default constructor
            TryteString result = new TryteString(value);

            // (If anything else needs to be populated on the result object, do that here)

            // Return the result
            return result;
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
    class TangleHashConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(Tangle.Net.Entity.Hash));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the JSON for the Result into a JObject
            JObject jo = JObject.Load(reader);

            // Read the properties which will be used as constructor parameters
            string value = (string)jo["Value"];

            // Construct the Result object using the non-default constructor
            Hash result = new Hash(value);

            // (If anything else needs to be populated on the result object, do that here)

            // Return the result
            return result;
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

    #endregion


    public class DBSerializer
    {
        public static string SerializeToJson(object input)
        {
            string res;
            res = JsonConvert.SerializeObject(
                input,
                Formatting.None,
                new JsonSerializerSettings() {
                    PreserveReferencesHandling = PreserveReferencesHandling.All,
                    TypeNameHandling = TypeNameHandling.All,
                    ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
                }
            );

            return res;
        }

        public static object DeserializeFromJson(string input)
        {
            var settings = new JsonSerializerSettings()
            {
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                TypeNameHandling = TypeNameHandling.All
            };

            settings.Converters.Add(new TangleAddressConverter());
            settings.Converters.Add(new TangleTryteStringConverter());
            settings.Converters.Add(new TangleHashConverter());
            
            var res =  JsonConvert.DeserializeObject( input, settings );

            return res;
        }
    }
}
