﻿using Newtonsoft.Json;

namespace AlertHub.Api.Models.FCM;

public class ResponseModel
{
    [JsonProperty("isSuccess")]
    public bool IsSuccess { get; set; }
    [JsonProperty("message")]
    public string Message { get; set; }
}