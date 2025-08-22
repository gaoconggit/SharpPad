```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    public static async Task Main()
    {
        var jsonStr =
        """
            {"messages":[{"content":"现在时间是:2024-12-10 14:21:24\n你是一个插件助手，会各种插件的能力，讲话通俗易懂","role":"system"},{"content":"你是什么模型","role":"user"}],"temperature":0.6,"top_p":0.5,"n":1,"stream":true,"model":"gemini-exp-1206"}
          
        """;

        var obj = JsonConvert.DeserializeObject(jsonStr);
        Console.WriteLine("```json \n" + JsonConvert.SerializeObject(obj, Formatting.Indented));
    }
}
