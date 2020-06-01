using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Common;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace Common
{
    internal class SerializationHelper
    {
        #region SerializeJson<T>(T obj)
        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJson<T>(T obj)
        {
            var options = new JsonSerializerOptions
            {
                IgnoreReadOnlyProperties = true,
                IgnoreNullValues = true,
                //ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                //DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                //Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
                //Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                //PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = true
            };
            //options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            var jsonString = System.Text.Json.JsonSerializer.Serialize(obj, options); 
            return jsonString;
        }
        #endregion
        #region SerializeJson(Type t, T obj)
        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJson(Type t, object obj)
        {
            if (t == null) { t = obj?.GetType(); }
            using (var stream = new MemoryStream())
            {
                string xml = null;
                var serializer = new DataContractJsonSerializer(t);
                serializer.WriteObject(stream, obj);
                stream.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(stream))
                {
                    xml = reader.ReadToEnd();
                    return xml;
                }
            }
        }
        #endregion
        #region DeserializeJson<T>(string xml)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static T DeserializeJson<T>(string xml)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    T obj = default(T);
                    writer.Write(xml);
                    writer.Flush();
                    stream.Seek(0, SeekOrigin.Begin);
                    obj = (T)serializer.ReadObject(stream);
                    return obj;
                }
            }
        }
        #endregion
        #region DeserializeJson(Type t, string json)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static object DeserializeJson(Type t, string json)
        {
            var serializer = new DataContractJsonSerializer(t);
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    object obj = null;
                    writer.Write(json);
                    writer.Flush();
                    stream.Seek(0, SeekOrigin.Begin);
                    obj = serializer.ReadObject(stream);
                    return obj;
                }
            }
        }
        #endregion

        #region SerializeJson<T>(T obj)
        public static string SerializeJsonObject(object obj)
        {
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ConstructorHandling = ConstructorHandling.Default,
                NullValueHandling = NullValueHandling.Ignore
            };
            return JsonConvert.SerializeObject(obj, jsonSerializerSettings);
        }
        public static object DeserializeJsonObject(string json)
        {
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ConstructorHandling = ConstructorHandling.Default,
                NullValueHandling = NullValueHandling.Ignore
            };

            return JsonConvert.DeserializeObject(json, jsonSerializerSettings);
        }

        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJsonObject<T>(T obj)
        {
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            };

            return JsonConvert.SerializeObject(obj, jsonSerializerSettings);
        }
        #endregion
        #region SerializeJsonObject(Type t, T obj)
        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJsonObject(Type t, object obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
        #endregion
        #region DeserializeJsonObject<T>(string json)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static T DeserializeJsonObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        #endregion
        #region DeserializeJsonObject(Type t, string json)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static object DeserializeJsonObject(Type t, string json)
        {
            return JsonConvert.DeserializeObject(json, t, default(JsonSerializerSettings));
        }
        #endregion

        #region SerializeJsonDataContract<T>(T obj)
        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJsonDataContract<T>(T obj)
        {
            using (var stream = new MemoryStream())
            {
                string xml = null;
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, obj);
                stream.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(stream))
                {
                    xml = reader.ReadToEnd();
                    return xml;
                }
            }
        }
        #endregion
        #region SerializeJsonDataContract(Type t, T obj)
        /// <summary>Serializza l'oggetto in una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da serializzare</typeparam>
        /// <param name='obj'>Oggetto da setializzare</param>
        /// <returns>String contenente l'xml con l'oggetto serializzato</returns>
        public static string SerializeJsonDataContract(Type t, object obj)
        {
            if (t == null) { t = obj?.GetType(); }
            using (var stream = new MemoryStream())
            {
                string xml = null;
                var serializer = new DataContractJsonSerializer(t);
                serializer.WriteObject(stream, obj);
                stream.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(stream))
                {
                    xml = reader.ReadToEnd();
                    return xml;
                }
            }
        }
        #endregion
        #region DeserializeJsonDataContract<T>(string xml)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static T DeserializeJsonDataContract<T>(string xml)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    T obj = default(T);
                    writer.Write(xml);
                    writer.Flush();
                    stream.Seek(0, SeekOrigin.Begin);
                    obj = (T)serializer.ReadObject(stream);
                    return obj;
                }
            }
        }
        #endregion
        #region DeserializeJsonDataContract(Type t, string json)
        /// <summary>Deserializza l'oggetto da una stringa xml (DataContractSerializer)</summary>
        /// <typeparam name='T'>Tipo dell'oggetto da deserializzare</typeparam>
        /// <param name='xml'>Xml da cui ricostruire l'oggetto</param>
        /// <returns>Oggetto del tipo specificato</returns>
        public static object DeserializeJsonDataContract(Type t, string json)
        {
            var serializer = new DataContractJsonSerializer(t);
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    object obj = null;
                    writer.Write(json);
                    writer.Flush();
                    stream.Seek(0, SeekOrigin.Begin);
                    obj = serializer.ReadObject(stream);
                    return obj;
                }
            }
        }
        #endregion

    }
}
