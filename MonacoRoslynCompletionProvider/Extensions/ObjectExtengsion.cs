using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace System
{
    public static class ObjectExtengsion
    {
        public static string ToJson(this Object value, bool format = false)
        {
            string json = JsonConvert.SerializeObject(value, format ? Formatting.Indented : Formatting.None);
            return json;
        }

        public static void Dump(this Object value)
        {
            Console.WriteLine(value);
        }
    }
}
