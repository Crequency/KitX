using System.Text.Json;

namespace KitX_Dashboard.Converters
{
    public class UpdateHashNamePolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name switch
        {
            "Item1" => "MD5",
            "Item2" => "SHA1",
            "Item3" => "Size",
            _ => name,
        };
    }
}
