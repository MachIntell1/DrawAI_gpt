using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MachIntellDrawAI.Infrastructure
{
    internal static class JsonContract
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(processDictionaryKeys: false, overrideSpecifiedNames: false)
            },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            DateParseHandling = DateParseHandling.DateTimeOffset
        };
    }
}
