```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text;

namespace login
{
    public class Program
    {
        public async static Task Main()
        {
            // postJson
            using var client = new HttpClient();
            var url = "";
            var reqJson = """
                    {
                      
                    }
                """;

            var response = await client.PostAsync(url, new StringContent(reqJson, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            body.Dump();
        }
    }
}
```
