using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tangle.Net.Entity;

namespace IOTA_Gears
{
    #region CustomizedJsonConverters
    // based on https://stackoverflow.com/a/23017892

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
            if (jo["Checksum"].Contains("Value") && jo["Checksum"]["Value"].Type == JTokenType.String)
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
    class TangleFragmentConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(Tangle.Net.Entity.Fragment));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the JSON for the Result into a JObject
            JObject jo = JObject.Load(reader);

            // Read the properties which will be used as constructor parameters
            string value = (string)jo["Value"];

            // Construct the Result object using the non-default constructor
            Fragment result = new Fragment(value);

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
    class TangleTagConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(Tangle.Net.Entity.Tag));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the JSON for the Result into a JObject
            JObject jo = JObject.Load(reader);

            // Read the properties which will be used as constructor parameters
            string value = (string)jo["Value"];

            // Construct the Result object using the non-default constructor
            Tag result = new Tag(value);

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


    //class TangleTransactionConverter : JsonConverter
    //{
    //    public override bool CanConvert(Type objectType)
    //    {
    //        return (objectType == typeof(Tangle.Net.Entity.Transaction));
    //    }

    //    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    //    {
    //        // Load the JSON for the Result into a JObject

    //        if (reader.TokenType == JsonToken.String)
    //        {
    //            // Read the properties which will be used as constructor parameters
    //            var value = (string)reader.Value;

    //            // Construct the Result object using the non-default constructor
    //            var result = Transaction.FromTrytes(new TransactionTrytes(value));

    //            // (If anything else needs to be populated on the result object, do that here)
    //            // Return the result
    //            return result;
    //        }

    //        return null;            
    //    }

    //    public override bool CanWrite
    //    {
    //        get { return true; }
    //    }

    //    public override bool CanRead
    //    {
    //        get { return true; }
    //    }

    //    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    //    {
    //        var o = (Transaction)value;

    //        // Transactions are serialized as Trytes
    //        var jt = new JValue(o.ToTrytes().Value);
    //        jt.WriteTo(writer);
    //    }
    //}

    #endregion

    public class DBSerializer
    {
        public static string SerializeToJson(object input)
        {
            var settings = new JsonSerializerSettings()
            {
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                TypeNameHandling = TypeNameHandling.All
            };
                       

            string res;
            res = JsonConvert.SerializeObject(
                input,
                Formatting.None,
                settings
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
            settings.Converters.Add(new TangleFragmentConverter());
            settings.Converters.Add(new TangleTagConverter());
            
            var res =  JsonConvert.DeserializeObject( input, settings );

            return res;
        }
    }
}
