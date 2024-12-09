using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System
{
    public static class ObjectExtengsion
    {
        public static void Dump(this Object value)
        {
            Console.WriteLine(value);
        }
    }
}
