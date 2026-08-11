using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MateEngine.AIVoiceMod
{
    internal static class FishMessagePack
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits
        {
            [FieldOffset(0)] public long Bits;
            [FieldOffset(0)] public double Value;
        }

        public static byte[] Encode(object value)
        {
            using (var stream = new MemoryStream()) { Write(stream, value); return stream.ToArray(); }
        }

        public static IDictionary<string, object> DecodeMap(byte[] bytes)
        {
            int offset = 0;
            var value = Read(bytes, ref offset) as IDictionary<string, object>;
            if (value == null) throw new InvalidDataException("Fish frame is not a MessagePack map.");
            return value;
        }

        private static void Write(Stream stream, object value)
        {
            if (value == null) { stream.WriteByte(0xc0); return; }
            if (value is bool) { stream.WriteByte((bool)value ? (byte)0xc3 : (byte)0xc2); return; }
            if (value is string) { WriteString(stream, (string)value); return; }
            if (value is byte[]) { WriteBinary(stream, (byte[])value); return; }
            if (value is int) { WriteInteger(stream, (int)value); return; }
            if (value is long) { WriteInteger(stream, (long)value); return; }
            if (value is float) { WriteDouble(stream, (float)value); return; }
            if (value is double) { WriteDouble(stream, (double)value); return; }
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                WriteMapHeader(stream, dictionary.Count);
                foreach (var item in dictionary) { WriteString(stream, item.Key); Write(stream, item.Value); }
                return;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var list = new List<object>(); foreach (var item in enumerable) list.Add(item);
                WriteArrayHeader(stream, list.Count); foreach (var item in list) Write(stream, item); return;
            }
            throw new NotSupportedException("Unsupported MessagePack value: " + value.GetType().FullName);
        }

        private static object Read(byte[] data, ref int offset)
        {
            if (offset >= data.Length) throw new EndOfStreamException();
            byte code = data[offset++];
            if (code <= 0x7f) return (long)code;
            if (code >= 0xe0) return (long)(sbyte)code;
            if ((code & 0xe0) == 0xa0) return ReadString(data, ref offset, code & 0x1f);
            if ((code & 0xf0) == 0x80) return ReadMap(data, ref offset, code & 0x0f);
            if ((code & 0xf0) == 0x90) return ReadArray(data, ref offset, code & 0x0f);
            switch (code)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xc4: return ReadBytes(data, ref offset, ReadU8(data, ref offset));
                case 0xc5: return ReadBytes(data, ref offset, ReadU16(data, ref offset));
                case 0xc6: return ReadBytes(data, ref offset, checked((int)ReadU32(data, ref offset)));
                case 0xcb: return ReadDouble(data, ref offset);
                case 0xcc: return (long)ReadU8(data, ref offset);
                case 0xcd: return (long)ReadU16(data, ref offset);
                case 0xce: return (long)ReadU32(data, ref offset);
                case 0xd0: return (long)(sbyte)ReadU8(data, ref offset);
                case 0xd1: return (long)(short)ReadU16(data, ref offset);
                case 0xd2: return (long)(int)ReadU32(data, ref offset);
                case 0xd9: return ReadString(data, ref offset, ReadU8(data, ref offset));
                case 0xda: return ReadString(data, ref offset, ReadU16(data, ref offset));
                case 0xdb: return ReadString(data, ref offset, checked((int)ReadU32(data, ref offset)));
                case 0xdc: return ReadArray(data, ref offset, ReadU16(data, ref offset));
                case 0xdd: return ReadArray(data, ref offset, checked((int)ReadU32(data, ref offset)));
                case 0xde: return ReadMap(data, ref offset, ReadU16(data, ref offset));
                case 0xdf: return ReadMap(data, ref offset, checked((int)ReadU32(data, ref offset)));
                default: throw new InvalidDataException("Unsupported MessagePack code 0x" + code.ToString("x2"));
            }
        }

        private static IDictionary<string, object> ReadMap(byte[] data, ref int offset, int count)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++) map[(string)Read(data, ref offset)] = Read(data, ref offset);
            return map;
        }
        private static object[] ReadArray(byte[] data, ref int offset, int count) { var array = new object[count]; for (int i = 0; i < count; i++) array[i] = Read(data, ref offset); return array; }
        private static string ReadString(byte[] data, ref int offset, int count) { var value = Encoding.UTF8.GetString(data, offset, count); offset += count; return value; }
        private static byte[] ReadBytes(byte[] data, ref int offset, int count) { var value = new byte[count]; Buffer.BlockCopy(data, offset, value, 0, count); offset += count; return value; }
        private static int ReadU8(byte[] data, ref int offset) { return data[offset++]; }
        private static int ReadU16(byte[] data, ref int offset) { return (data[offset++] << 8) | data[offset++]; }
        private static uint ReadU32(byte[] data, ref int offset) { return ((uint)data[offset++] << 24) | ((uint)data[offset++] << 16) | ((uint)data[offset++] << 8) | data[offset++]; }
        private static double ReadDouble(byte[] data, ref int offset)
        {
            ulong bits = ((ulong)data[offset++] << 56) | ((ulong)data[offset++] << 48) | ((ulong)data[offset++] << 40) | ((ulong)data[offset++] << 32) |
                         ((ulong)data[offset++] << 24) | ((ulong)data[offset++] << 16) | ((ulong)data[offset++] << 8) | data[offset++];
            return new DoubleBits { Bits = unchecked((long)bits) }.Value;
        }

        private static void WriteString(Stream stream, string value) { var bytes = Encoding.UTF8.GetBytes(value ?? ""); if (bytes.Length < 32) stream.WriteByte((byte)(0xa0 | bytes.Length)); else if (bytes.Length <= 255) { stream.WriteByte(0xd9); stream.WriteByte((byte)bytes.Length); } else { stream.WriteByte(0xda); WriteU16(stream, bytes.Length); } stream.Write(bytes, 0, bytes.Length); }
        private static void WriteBinary(Stream stream, byte[] value) { if (value.Length <= 255) { stream.WriteByte(0xc4); stream.WriteByte((byte)value.Length); } else { stream.WriteByte(0xc5); WriteU16(stream, value.Length); } stream.Write(value, 0, value.Length); }
        private static void WriteMapHeader(Stream stream, int count) { if (count < 16) stream.WriteByte((byte)(0x80 | count)); else { stream.WriteByte(0xde); WriteU16(stream, count); } }
        private static void WriteArrayHeader(Stream stream, int count) { if (count < 16) stream.WriteByte((byte)(0x90 | count)); else { stream.WriteByte(0xdc); WriteU16(stream, count); } }
        private static void WriteInteger(Stream stream, long value) { if (value >= 0 && value <= 127) stream.WriteByte((byte)value); else { stream.WriteByte(0xd2); WriteU32(stream, unchecked((uint)value)); } }
        private static void WriteDouble(Stream stream, double value)
        {
            stream.WriteByte(0xcb);
            ulong bits = unchecked((ulong)new DoubleBits { Value = value }.Bits);
            stream.WriteByte((byte)(bits >> 56)); stream.WriteByte((byte)(bits >> 48)); stream.WriteByte((byte)(bits >> 40)); stream.WriteByte((byte)(bits >> 32));
            stream.WriteByte((byte)(bits >> 24)); stream.WriteByte((byte)(bits >> 16)); stream.WriteByte((byte)(bits >> 8)); stream.WriteByte((byte)bits);
        }
        private static void WriteU16(Stream stream, int value) { stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
        private static void WriteU32(Stream stream, uint value) { stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
    }
}
