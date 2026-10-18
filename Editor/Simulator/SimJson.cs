using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>
    /// A small JSON reader and writer for the simulator. JsonUtility cannot read a map or an
    /// object of unknown shape, and the simulator has to read whatever a game sends, the way the
    /// platform's Python handlers do.
    ///
    /// Values are <see cref="Dictionary{TKey,TValue}"/> (insertion-ordered), <see cref="List{T}"/>,
    /// string, long (a number written without a fraction or exponent), double, bool or null —
    /// so <c>1</c> and <c>1.0</c> stay different, as they are to Python's <c>isinstance(x, int)</c>.
    /// </summary>
    internal static class SimJson
    {
        /// <summary>The parsed value, or throws <see cref="FormatException"/>.</summary>
        public static object Parse(string json)
        {
            var reader = new Reader(json ?? "");
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new FormatException("Unexpected text after JSON value at " + reader.Position);
            }
            return value;
        }

        /// <summary>
        /// The parsed value, or null for anything that is not valid JSON — what Flask's
        /// <c>request.get_json(silent=True)</c> gives a handler.
        /// </summary>
        public static object TryParse(string json)
        {
            try
            {
                return Parse(json);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>Compact JSON, as the browser's <c>JSON.stringify</c> writes it.</summary>
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, false);
            return sb.ToString();
        }

        /// <summary>
        /// JSON as Python's <c>json.dumps</c> writes it with default arguments: ", " and ": "
        /// separators, non-ASCII escaped, and floats always with a fraction. The save route stores a
        /// non-string <c>data</c> this way, and measures its 500,000-character limit on it.
        /// </summary>
        public static string SerializePython(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, true);
            return sb.ToString();
        }

        public static Dictionary<string, object> Obj(params object[] keysAndValues)
        {
            var result = new Dictionary<string, object>();
            for (var i = 0; i + 1 < keysAndValues.Length; i += 2)
            {
                result[(string)keysAndValues[i]] = keysAndValues[i + 1];
            }
            return result;
        }

        private static void Write(StringBuilder sb, object value, bool python)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string text:
                    WriteString(sb, text, python);
                    return;
                case bool flag:
                    sb.Append(flag ? "true" : "false");
                    return;
                case long or int or short or byte:
                    sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                    return;
                case double or float or decimal:
                    WriteNumber(sb, Convert.ToDouble(value), python);
                    return;
                case IDictionary<string, object> map:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first) sb.Append(python ? ", " : ",");
                        first = false;
                        WriteString(sb, pair.Key, python);
                        sb.Append(python ? ": " : ":");
                        Write(sb, pair.Value, python);
                    }
                    sb.Append('}');
                    return;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    var first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(python ? ", " : ",");
                        first = false;
                        Write(sb, item, python);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    WriteString(sb, value.ToString(), python);
                    return;
            }
        }

        private static void WriteNumber(StringBuilder sb, double number, bool python)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                // Python writes NaN/Infinity, which is not JSON; JSON.stringify writes null.
                sb.Append(python ? (double.IsNaN(number) ? "NaN" : number > 0 ? "Infinity" : "-Infinity") : "null");
                return;
            }
            if (number == Math.Floor(number) && Math.Abs(number) < 1e16)
            {
                sb.Append(((long)number).ToString(CultureInfo.InvariantCulture));
                if (python) sb.Append(".0");
                return;
            }
            sb.Append(number.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string text, bool python)
        {
            sb.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || (python && c > 0x7f))
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _index;

            public Reader(string text)
            {
                _text = text;
            }

            public bool AtEnd => _index >= _text.Length;
            public int Position => _index;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && (_text[_index] == ' ' || _text[_index] == '\t' || _text[_index] == '\n' || _text[_index] == '\r'))
                {
                    _index++;
                }
            }

            public object ReadValue()
            {
                if (AtEnd) throw Error("Unexpected end of JSON");
                var c = _text[_index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                _index++;
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}')
                {
                    _index++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"') throw Error("Expected a property name");
                    var key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':') throw Error("Expected ':'");
                    _index++;
                    SkipWhitespace();
                    // A repeated key keeps the last value, as Python's json module does.
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (_text[_index] == '}')
                    {
                        _index++;
                        return result;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _index++;
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']')
                {
                    _index++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (_text[_index] == ']')
                    {
                        _index++;
                        return result;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                _index++;
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    var c = _text[_index++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Control character in string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("Unterminated escape");
                    var escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw Error("Bad unicode escape");
                            if (!int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                throw Error("Bad unicode escape");
                            }
                            sb.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error("Bad escape '\\" + escape + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                var start = _index;
                if (_text[_index] == '-') _index++;
                var integral = true;
                while (!AtEnd)
                {
                    var c = _text[_index];
                    if (c >= '0' && c <= '9')
                    {
                        _index++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        integral = false;
                        _index++;
                    }
                    else
                    {
                        break;
                    }
                }
                var token = _text.Substring(start, _index - start);
                if (integral && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                {
                    return whole;
                }
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                {
                    return real;
                }
                throw Error("Bad number '" + token + "'");
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0)
                {
                    throw Error("Unexpected token");
                }
                _index += word.Length;
            }

            private FormatException Error(string message)
            {
                return new FormatException(message + " at " + _index);
            }
        }
    }
}
