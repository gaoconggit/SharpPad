```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

class Program
{
    public static async Task Main()
    {
        string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        // 定义一个字段排序顺序字典 
        var fieldOrder = new Dictionary<string, int>()
        {
            { "exp", 0 },
            { "iss", 1 },
            { "aud", 2 },
            { "client_id", 3 },
            { "sub", 4 },
            { "auth_time", 5 },
            { "idp", 6 },
            { "userid", 7 },
            { "tenantid", 8 },
            { "tenantname", 9 },
            { "scope", 10 },
        };

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwtToken = tokenHandler.ReadJwtToken(token);

        Console.WriteLine("```json Header:");
        JsonConvert.SerializeObject(jwtToken.Header, Newtonsoft.Json.Formatting.Indented).Dump();
        Console.WriteLine("```");

        Console.WriteLine("```json Payload:");
        // 将JWT令牌的负载解析为字典类型
        var payloadDict = new Dictionary<string, object>();
        foreach (var claim in jwtToken.Payload.Claims)
        {
            if (payloadDict.ContainsKey(claim.Type))
            {
                // 如果键已经存在，尝试将其值解析为数组并追加新值，否则执行添加操作
                if (payloadDict[claim.Type] is object[] valueArray)
                {
                    payloadDict[claim.Type] = valueArray.Append(claim.Value).ToArray();
                }
                else
                {
                    payloadDict[claim.Type] = new object[] { payloadDict[claim.Type], claim.Value };
                }
            }
            else
            {
                payloadDict.Add(claim.Type, claim.Value);
            }
        }

        // 将字典按照预定义的顺序排序
        var sortedDict = payloadDict.OrderBy(x => fieldOrder.ContainsKey(x.Key) ? fieldOrder[x.Key] : int.MaxValue)
                                     .ToDictionary(x => x.Key, x => x.Value);

        // 输出JWT令牌的负载部分
        var json = JsonConvert.SerializeObject(sortedDict, Newtonsoft.Json.Formatting.Indented);
        Console.WriteLine(json);

    }
}
```
