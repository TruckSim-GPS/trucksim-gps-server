using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIIDecryptSharp
{
    public class StreamUtils
    {
        public static bool TryReadUInt32(ref byte[] bytes, ref int offset, out UInt32 result)
        {
            try
            {
                result = BitConverter.ToUInt32(bytes, offset);
                offset += sizeof(UInt32);
                return true;
            }
            catch
            {
                result = 0;
                
            }
            return false;
        }

    }
}
