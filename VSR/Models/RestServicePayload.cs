using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VSR.Models;

public class QueryOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class RestServicePayload
{
    [JsonPropertyName("func")]
    public string Func { get; set; } = "ajaxExecuteQueryO";

    [JsonPropertyName("params")]
    public List<object?> Params { get; set; } = new();

    [JsonPropertyName("options")]
    public List<QueryOption> Options { get; set; } = new();

    [JsonPropertyName("fcode")]
    public string Fcode { get; set; } = "FNC002";

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;
}
