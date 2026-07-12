using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Simple JSON parser for Unity (no external dependencies).
    /// Handles nested objects and arrays manually since Unity's JsonUtility doesn't support Dictionary.
    /// </summary>
    public class SimpleJson
    {
        private readonly Dictionary<string, object> _data;

        public SimpleJson() { _data = new Dictionary<string, object>(); }
        public SimpleJson(Dictionary<string, object> data) { _data = data ?? new Dictionary<string, object>(); }

        public static SimpleJson Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new SimpleJson();
            return new SimpleJson(ParseJsonObject(json.Trim()));
        }

        public string GetString(string key) => _data.TryGetValue(key, out var v) ? v?.ToString() : null;
        public int GetInt(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
        public int GetInt(string key, int defaultValue) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : defaultValue;
        public long GetLong(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;
        public long GetLong(string key, long defaultValue) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : defaultValue;
        public double GetDouble(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : 0;
        public float GetFloat(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToSingle(v) : 0f;
        public bool GetBool(string key) => _data.TryGetValue(key, out var v) && v != null && Convert.ToBoolean(v);
        public SimpleJson GetObject(string key) => _data.TryGetValue(key, out var v) && v is Dictionary<string, object> d ? new SimpleJson(d) : null;
        public List<SimpleJson> GetArray(string key)
        {
            if (_data.TryGetValue(key, out var v) && v is List<object> list)
                return list.Select(o => new SimpleJson(o as Dictionary<string, object>)).ToList();
            return null;
        }

        public List<string> GetStringArray(string key)
        {
            if (_data.TryGetValue(key, out var v) && v is List<object> list)
                return list.Select(o => o?.ToString() ?? "").ToList();
            return null;
        }


        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var type = obj.GetType();

            if (type == typeof(string))
                return $"\"{EscapeString((string)obj)}\"";

            if (type == typeof(bool))
                return (bool)obj ? "true" : "false";

            if (type.IsPrimitive || type == typeof(decimal))
                return obj.ToString();

            // Handle arrays
            if (type.IsArray)
            {
                var array = (Array)obj;
                var sb = new StringBuilder();
                sb.Append("[");
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(Serialize(array.GetValue(i)));
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Handle IEnumerable (lists, etc.) but not string or dictionary
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string) && !(obj is System.Collections.IDictionary))
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Serialize(item));
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Handle anonymous types and objects
            var objSb = new StringBuilder();
            objSb.Append("{");
            bool firstProp = true;

            foreach (var prop in type.GetProperties())
            {
                // Skip indexers and internal properties
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.Name == "SyncRoot" || prop.Name == "IsReadOnly" || prop.Name == "IsFixedSize" || prop.Name == "IsSynchronized") continue;

                try
                {
                    var value = prop.GetValue(obj);
                    var name = char.ToLower(prop.Name[0]) + prop.Name.Substring(1); // camelCase

                    if (!firstProp) objSb.Append(",");
                    firstProp = false;

                    objSb.Append($"\"{name}\":");

                    if (value == null)
                        objSb.Append("null");
                    else if (value is string s)
                        objSb.Append($"\"{EscapeString(s)}\"");
                    else if (value is bool b)
                        objSb.Append(b ? "true" : "false");
                    else if (value.GetType().IsPrimitive || value.GetType() == typeof(decimal))
                        objSb.Append(value.ToString());
                    else
                        objSb.Append(Serialize(value));
                }
                catch
                {
                    // Skip properties that throw exceptions
                }
            }

            objSb.Append("}");
            return objSb.ToString();
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static Dictionary<string, object> ParseJsonObject(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json) || !json.StartsWith("{")) return result;

            try
            {
                int depth = 0;
                bool inString = false;
                bool escaped = false;
                var currentKey = new StringBuilder();
                var currentValue = new StringBuilder();
                bool readingKey = true;
                bool keyReady = false;

                for (int i = 1; i < json.Length - 1; i++)
                {
                    char c = json[i];

                    if (escaped)
                    {
                        escaped = false;
                        // CRITICAL FIX: Properly unescape JSON escape sequences
                        char unescaped = c switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            '/' => '/',
                            _ => c
                        };
                        if (readingKey) currentKey.Append(unescaped);
                        else currentValue.Append(unescaped);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        // Don't append backslash yet - wait for next char to determine escape sequence
                        continue;
                    }

                    if (c == '"' && depth == 0)
                    {
                        inString = !inString;
                        if (!inString && readingKey) keyReady = true;
                        continue;
                    }

                    if (!inString)
                    {
                        if (c == '{' || c == '[')
                        {
                            depth++;
                            if (!readingKey) currentValue.Append(c);
                            continue;
                        }
                        if (c == '}' || c == ']')
                        {
                            depth--;
                            if (!readingKey) currentValue.Append(c);
                            continue;
                        }
                        if (depth == 0 && c == ':' && keyReady)
                        {
                            readingKey = false;
                            continue;
                        }
                        if (depth == 0 && c == ',')
                        {
                            if (currentKey.Length > 0)
                            {
                                var key = currentKey.ToString().Trim();
                                var val = currentValue.ToString().Trim();
                                result[key] = ParseValue(val);
                            }
                            currentKey.Clear();
                            currentValue.Clear();
                            readingKey = true;
                            keyReady = false;
                            continue;
                        }
                        if (depth == 0 && char.IsWhiteSpace(c)) continue;
                    }

                    if (readingKey && inString)
                        currentKey.Append(c);
                    else if (!readingKey)
                        currentValue.Append(c);
                }

                if (currentKey.Length > 0)
                {
                    var key = currentKey.ToString().Trim();
                    var val = currentValue.ToString().Trim();
                    result[key] = ParseValue(val);
                }
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[SimpleJson] Parse error: {ex.Message}");
            }

            return result;
        }

        private static object ParseValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim();
            if (value == "null") return null;
            if (value == "true") return true;
            if (value == "false") return false;
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                var s = value.Substring(1, value.Length - 2);
                return UnescapeString(s);
            }
            if (value.StartsWith("{")) return ParseJsonObject(value);
            if (value.StartsWith("[")) return ParseJsonArray(value);
            if (long.TryParse(value, out long l)) return l;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
            return value;
        }

        private static string UnescapeString(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static List<object> ParseJsonArray(string json)
        {
            var result = new List<object>();
            if (string.IsNullOrEmpty(json) || !json.StartsWith("[")) return result;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            var current = new StringBuilder();

            for (int i = 1; i < json.Length - 1; i++)
            {
                char c = json[i];

                if (escaped)
                {
                    escaped = false;
                    current.Append(c);
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    current.Append(c);
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    current.Append(c);
                    continue;
                }

                if (!inString)
                {
                    if (c == '{' || c == '[') depth++;
                    if (c == '}' || c == ']') depth--;
                    if (depth == 0 && c == ',')
                    {
                        var val = current.ToString().Trim();
                        if (!string.IsNullOrEmpty(val))
                            result.Add(ParseValue(val));
                        current.Clear();
                        continue;
                    }
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                var val = current.ToString().Trim();
                if (!string.IsNullOrEmpty(val))
                    result.Add(ParseValue(val));
            }

            return result;
        }
    }
}
