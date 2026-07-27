using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIIDecryptSharp
{
    public class BSII_Serializer
    {
        public static byte[] Serialize(ref BSII_Data data)
        {
            //serialize the data:
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SiiNunit");
            sb.AppendLine("{");

            string indent = "";
            foreach (var block in data.DecodedBlocks)
            {
                if (String.IsNullOrEmpty(block.Name) || String.IsNullOrEmpty(block.ID?.Value))
                    continue;

                sb.AppendLine(block.Name + " : " + block.ID.Value + " {");
                indent = " ";

                foreach (var s1 in block.Segments)
                {
                    var segment = s1;
                    if (segment.Type != 0)
                    {
                        switch (segment.Type)
                        {
                            case (int)DataTypeIdFormat.ArrayOfByteBool:
                                sb.Append(SerializeByteBoolArray(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfEncodedString:
                                sb.Append(SerializeEncodedStringArray(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfIdA:
                            case (int)DataTypeIdFormat.ArrayOfIdC:
                            case (int)DataTypeIdFormat.ArrayOfIdE:
                                sb.Append(SerializeIDArray(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfInt32:
                                sb.Append(SerializeInt32Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfSingle:
                                sb.Append(SerializeSingleArray(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfUInt16:
                                sb.Append(SerializeUInt16Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfUInt32:
                                sb.Append(SerializeUInt32Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfUInt64:
                                sb.Append(SerializeUInt64Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfUTF8String:
                                sb.Append(SerializeUTF8StringArray(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfVectorOf3Int32:
                                sb.Append(SerializeInt32Vector3Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfVectorOf3Single:
                                sb.Append(SerializeSingleVector3Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfVectorOf4Single:
                                sb.Append(SerializeSingleVector4Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfVectorOf8Single:
                                if (data.Header.Version == 1)
                                {
                                    sb.Append(SerializeSingleVector7Array(ref segment, ref indent));
                                }
                                else
                                {
                                    sb.Append(SerializeSingleVector8Array(ref segment, ref indent));
                                }
                                break;
                            case (int)DataTypeIdFormat.ByteBool:
                                sb.Append(SerializeBool(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.EncodedString:
                                sb.Append(SerializeEncodedString(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.IdType3:
                            case (int)DataTypeIdFormat.IdType2:
                            case (int)DataTypeIdFormat.Id:
                                sb.Append(SerializeId(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.Int32:
                                sb.Append(SerializeInt32(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.Int64:
                                sb.Append(SerializeInt64(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.UInt32Type2:
                            case (int)DataTypeIdFormat.UInt32:
                                sb.Append(SerializeUInt32(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.UInt64:
                                sb.Append(SerializeUInt64(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.UInt16:
                                sb.Append(SerializeUInt16(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.OrdinalString:
                                sb.Append(SerializeOrdinalString(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.Single:
                                sb.Append(SerializeSingle(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.UTF8String:
                                sb.Append(SerializeUTF8String(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.VectorOf2Single:
                                sb.Append(SerializeSingleVector2(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.VectorOf3Int32:
                                sb.Append(SerializeInt32Vector3(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.VectorOf3Single:
                                sb.Append(SerializeSingleVector3(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.VectorOf4Single:
                                sb.Append(SerializeSingleVector4(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.VectorOf8Single:
                                if (data.Header.Version == 1)
                                {
                                    sb.Append(SerializeSingleVector7(ref segment, ref indent));
                                }
                                else
                                {
                                    sb.Append(SerializeSingleVector8(ref segment, ref indent));
                                }
                                break;
                            case (int)DataTypeIdFormat.ArrayOfInt64:
                                sb.Append(SerializeInt64Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfVectorOf2Single:
                                sb.Append(SerializeSingleVector2Array(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.Int16:
                                sb.Append(SerializeInt16(ref segment, ref indent));
                                break;
                            case (int)DataTypeIdFormat.ArrayOfInt16:
                                sb.Append(SerializeInt16Array(ref segment, ref indent));
                                break;
                            case 0:
                            default:
                                break;
                        }
                    }
                }
                sb.AppendLine("}").AppendLine("");
                

            }
            sb.Append("}");
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static string SerializeByteBoolArray(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as bool[];
            StringBuilder sb = new StringBuilder();
            sb.Append(indent);
            sb.AppendLine(data.Name + ": " + value.Length);
            for(int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString().ToLower());
            }
            return sb.ToString();
        }
        public static string SerializeEncodedStringArray(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as string[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for(int i = 0; i < value.Length; i ++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i]);   
            }
            return sb.ToString();
        }
        
        public static string SerializeIDArray(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as IDComplexType[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].Value);
            }
            return sb.ToString();
        }
                               
        public static string SerializeInt32Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as Int32[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }
        public static string SerializeSingleArray(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as Single[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                string text = "nil";

                if (value[i] - Math.Truncate(value[i]) != 0.00f || value[i] >= 1e7)
                {
                    text = "";
                    var bytes = BitConverter.GetBytes(value[i]);
                    foreach (byte b in bytes)
                    {
                        text = b.ToString("x2") + text;
                    }
                    text = "&" + text;
                }
                else
                {
                    text = ((int)value[i]).ToString("f0");
                }
                
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + text);
            }
            return sb.ToString();
        }
        public static string SerializeUInt16Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as UInt16[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }
        public static string SerializeUInt32Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as UInt32[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }
        public static string SerializeUInt64Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as UInt64[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }
        public static string SerializeUTF8StringArray(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as string[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (int.TryParse(value[i], out int int32))
                {
                    sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i]);
                }
                else
                {
                    if (string.IsNullOrEmpty(value[i]))
                    {
                        sb.AppendLine(indent + data.Name + "[" + i + "]: " + "\"\"");
                    }
                    else
                    {
                        if (IsLimitedAlphabet(ref value[i]))
                        {
                            sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i]);
                        }
                        else
                        {
                            sb.AppendLine(indent + data.Name + "[" + i + "]: " + "\"" + value[i] + "\"");
                        }
                    }
                }
            }
            return sb.ToString();
        }
        public static string SerializeInt32Vector3Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as Int32Vector3[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + ", " + SerializeSingle(value[i].B) + ", " + SerializeSingle(value[i].C) + ")");
            }
            return sb.ToString();
        }
        public static string SerializeSingleVector3Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as SingleVector3[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + ", " + SerializeSingle(value[i].B) + ", " + SerializeSingle(value[i].C) + ")");
            }
            return sb.ToString();
        }
        public static string SerializeSingleVector2Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as SingleVector2[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + ", " + SerializeSingle(value[i].B) + ")");
            }
            return sb.ToString();
        }
        public static string SerializeSingleVector4Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as SingleVector4[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + "; " + SerializeSingle(value[i].B) + ", " + SerializeSingle(value[i].C) + ", " + SerializeSingle(value[i].D) +")");
            }
            return sb.ToString();
        }
        public static string SerializeSingleVector8Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as SingleVector8[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + ", " + SerializeSingle(value[i].B) + ", " + SerializeSingle(value[i].C) + ") (" +
                    SerializeSingle(value[i].E) + "; " + SerializeSingle(value[i].F) + ", " + SerializeSingle(value[i].G) + ", " + SerializeSingle(value[i].H) + ")");
            }
            return sb.ToString();
        }
        public static string SerializeSingleVector7Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as SingleVector7[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(indent + data.Name + "[" + i + "]: (");
                sb.AppendLine(SerializeSingle(value[i].A) + ", " + SerializeSingle(value[i].B) + ", " + SerializeSingle(value[i].C) + ") (" + SerializeSingle(value[i].D) + "; " + SerializeSingle(value[i].E) + ", " + SerializeSingle(value[i].F) + ")");
            }
            return sb.ToString();
        }
        public static string SerializeBool(ref BSII_DataSegment data, ref string indent)
        {
            bool? value = data.Value as bool? ?? false;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.ToString().ToLower());
            return sb.ToString();
        }
        public static string SerializeEncodedString(ref BSII_DataSegment data, ref string indent)
        {
            string value = data.Value as string;
            StringBuilder sb = new StringBuilder();
            if(string.IsNullOrEmpty(value))
            {
                value = "\"\"";
            }
            sb.AppendLine(indent + data.Name + ": " + value);
            return sb.ToString();
        }
        public static string SerializeId(ref BSII_DataSegment data, ref string indent)
        {
            IDComplexType value = data.Value as IDComplexType;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Value);
            return sb.ToString();
        }
        public static string SerializeInt32(ref BSII_DataSegment data, ref string indent)
        {
            Int32? value = data.Value as Int32?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if(value != null) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeInt64(ref BSII_DataSegment data, ref string indent)
        {
            Int64? value = data.Value as Int64?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if (value != null) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeUInt32(ref BSII_DataSegment data, ref string indent)
        {
            UInt32? value = data.Value as UInt32?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if (value != null && value != 4294967295) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeUInt64(ref BSII_DataSegment data, ref string indent)
        {
            UInt64? value = data.Value as UInt64?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if (value != null) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeUInt16(ref BSII_DataSegment data, ref string indent)
        {
            UInt16? value = data.Value as UInt16?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if (value != null && value != 65535) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeOrdinalString(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as string;
            StringBuilder sb = new StringBuilder();
            sb.Append(indent + data.Name + ": ");
            sb.AppendLine(value);
            return sb.ToString();
        }

        public static string SerializeInt16Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as Int16[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }
        public static string SerializeInt16(ref BSII_DataSegment data, ref string indent)
        {
            Int16? value = data.Value as Int16?;
            StringBuilder sb = new StringBuilder();
            string text = "nil";
            if (value != null && value != 32767) text = value.ToString();
            sb.AppendLine(indent + data.Name + ": " + text);
            return sb.ToString();
        }
        public static string SerializeSingle(ref BSII_DataSegment data, ref string indent)
        {
            Single? value = data.Value as Single?;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + SerializeSingle(value));
            return sb.ToString();
        }
        public static string SerializeUTF8String(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as string;
            StringBuilder sb = new StringBuilder();
            sb.Append(indent + data.Name + ": ");
           
            if (int.TryParse(value, out int int32))
            {
                sb.AppendLine(value);
            }
            else
            {
                if (string.IsNullOrEmpty(value))
                {
                    sb.AppendLine("\"\"");
                }
                else
                {
                    if (IsLimitedAlphabet(ref value))
                    {
                        sb.AppendLine(value);
                    }
                    else
                    {
                        sb.AppendLine("\"" + value + "\"");
                    }
                }
            }
            
            return sb.ToString();
        }
        public static string SerializeSingleVector2(ref BSII_DataSegment data, ref string indent)
        {
            SingleVector2 vector = data.Value as SingleVector2;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": (" + SerializeSingle(vector.A) + ", " + SerializeSingle(vector.B) + ")");
            return sb.ToString();
        }
        public static string SerializeSingleVector3(ref BSII_DataSegment data, ref string indent)
        {
            SingleVector3 vector = data.Value as SingleVector3;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": (" + SerializeSingle(vector.A) + ", " + SerializeSingle(vector.B) + ", " + SerializeSingle(vector.C) +")");
            return sb.ToString();
        }
        public static string SerializeSingleVector4(ref BSII_DataSegment data, ref string indent)
        {
            SingleVector4 vector = data.Value as SingleVector4;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": (" + SerializeSingle(vector.A) + "; " + SerializeSingle(vector.B) + ", " + SerializeSingle(vector.C) + ", " + SerializeSingle(vector.D) + ")");
            return sb.ToString();
        }
        public static string SerializeInt32Vector3(ref BSII_DataSegment data, ref string indent)
        {
            Int32Vector3 vector = data.Value as Int32Vector3;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": (" + vector.A + ", " + vector.B + ", " + vector.C + ")");
            return sb.ToString();
        }
        public static string SerializeSingleVector8(ref BSII_DataSegment data, ref string indent)
        {
            StringBuilder sb = new StringBuilder();
            SingleVector8 vector = data.Value as SingleVector8;
            sb.AppendLine(indent + data.Name + ": (" + SerializeSingle(vector.A) + ", " + SerializeSingle(vector.B) + ", " + SerializeSingle(vector.C) + ") (" +
                SerializeSingle(vector.E) + "; " + SerializeSingle(vector.F) + ", " + SerializeSingle(vector.G) + ", " + SerializeSingle(vector.H) + ")");
            return sb.ToString();
        }
        public static string SerializeSingleVector7(ref BSII_DataSegment data, ref string indent)
        {
            StringBuilder sb = new StringBuilder();
            SingleVector7 vector = data.Value as SingleVector7;
            sb.AppendLine(indent + data.Name + ": (" + SerializeSingle(vector.A) + ", " + SerializeSingle(vector.B) + ", " + SerializeSingle(vector.C) + ") (" +
                SerializeSingle(vector.D) + "; " + SerializeSingle(vector.E) + ", " + SerializeSingle(vector.F) + ", " + SerializeSingle(vector.G) + ")");
            return sb.ToString();
        }
        public static string SerializeInt64Array(ref BSII_DataSegment data, ref string indent)
        {
            var value = data.Value as Int64[];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(indent + data.Name + ": " + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                sb.AppendLine(indent + data.Name + "[" + i + "]: " + value[i].ToString());
            }
            return sb.ToString();
        }

        private static bool IsLimitedAlphabet(ref string value)
        {
            for(int i = 0; i < value.Length; i++)
            {
                if (!LimitedAlphabet.Contains(value[i])) return false;
            }
            return true;
        }
        private static char[] LimitedAlphabet = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l',
            'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q',
            'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '_'
        };
        public static string SerializeSingle(Single? single)
        {
            string text = "nil";
            if (single.HasValue)
            {
                if (single.Value - Math.Truncate(single.Value) != 0.00f || single.Value >= 1e7)
                {
                    text = "";
                    var bytes = BitConverter.GetBytes(single.Value);
                    foreach (byte b in bytes)
                    {
                        text = b.ToString("x2") + text;
                    }
                    text = "&" + text;
                }
                else
                {
                    text = ((int)single.Value).ToString("f0");
                }
            }
            return text;
        }
    }
}
