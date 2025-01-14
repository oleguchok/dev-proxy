// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Mocks;

public class MockRequestConfiguration
{
    [JsonIgnore]
    public string MockFile { get; set; } = "mock-request.json";
    [JsonPropertyName("request")]
    public MockRequest? Request { get; set; }
}

public class MockRequestPlugin : BaseProxyPlugin
{
    protected MockRequestConfiguration _configuration = new();
    private MockRequestLoader? _loader = null;

    public override string Name => nameof(MockRequestPlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);
        _loader = new MockRequestLoader(_logger!, _configuration);

        pluginEvents.MockRequest += OnMockRequest;

        // make the mock file path relative to the configuration file
        _configuration.MockFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(_configuration.MockFile), Path.GetDirectoryName(context.Configuration?.ConfigFile ?? string.Empty) ?? string.Empty);

        // load the request from the configured mock file
        _loader.InitResponsesWatcher();
    }

    protected HttpRequestMessage GetRequestMessage()
    {
        Debug.Assert(_configuration.Request is not null, "The mock request is not configured");

        _logger?.LogDebug("Preparing mock {method} request to {url}", _configuration.Request.Method, _configuration.Request.Url);
        var requestMessage = new HttpRequestMessage
        {
            RequestUri = new Uri(_configuration.Request.Url),
            Method = new HttpMethod(_configuration.Request.Method)
        };

        var contentType = "";
        if (_configuration.Request.Headers is not null)
        {
            _logger?.LogDebug("Adding headers to the mock request");

            foreach (var header in _configuration.Request.Headers)
            {
                if (header.Name.ToLower() == "content-type")
                {
                    contentType = header.Value;
                    continue;
                }

                requestMessage.Headers.Add(header.Name, header.Value);
            }
        }

        if (_configuration.Request.Body is not null)
        {
            _logger?.LogDebug("Adding body to the mock request");

            if (_configuration.Request.Body is string)
            {
                requestMessage.Content = new StringContent(_configuration.Request.Body, Encoding.UTF8, contentType);
            }
            else
            {
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(_configuration.Request.Body), Encoding.UTF8, "application/json");
            }
        }

        return requestMessage;
    }

    protected virtual async Task OnMockRequest(object sender, EventArgs e)
    {
        if (_configuration.Request is null)
        {
            _logger?.LogDebug("No mock request is configured. Skipping.");
            return;
        }

        using (var httpClient = new HttpClient())
        {
            var requestMessage = GetRequestMessage();

            try
            {
                _logger?.LogRequest(["Sending mock request"], MessageType.Mocked, _configuration.Request.Method, _configuration.Request.Url);

                await httpClient.SendAsync(requestMessage);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "An error has occurred while sending the mock request to {url}", _configuration.Request.Url);
            }
        }
    }
}