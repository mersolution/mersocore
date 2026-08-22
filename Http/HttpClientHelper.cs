using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Http
{
    /// <summary>
    /// HTTP Client helper for REST API calls
    /// </summary>
    public class HttpClientHelper : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientConfig _config;
        private bool _disposed = false;

        /// <summary>
        /// Create HTTP client with default settings
        /// </summary>
        public HttpClientHelper()
        {
            _config = new HttpClientConfig();
            _httpClient = CreateHttpClient();
        }

        /// <summary>
        /// Create HTTP client with configuration
        /// </summary>
        public HttpClientHelper(HttpClientConfig config)
        {
            _config = config ?? new HttpClientConfig();
            _httpClient = CreateHttpClient();
        }

        /// <summary>
        /// Create HTTP client with base URL
        /// </summary>
        public HttpClientHelper(string baseUrl)
        {
            _config = new HttpClientConfig { BaseUrl = baseUrl };
            _httpClient = CreateHttpClient();
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (_config.IgnoreSslErrors)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
            };

            if (!string.IsNullOrEmpty(_config.BaseUrl))
            {
                client.BaseAddress = new Uri(_config.BaseUrl);
            }

            // Default headers
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrEmpty(_config.BearerToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.BearerToken);
            }

            if (!string.IsNullOrEmpty(_config.BasicAuthUsername))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.BasicAuthUsername}:{_config.BasicAuthPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            // Custom headers
            if (_config.DefaultHeaders != null)
            {
                foreach (var header in _config.DefaultHeaders)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return client;
        }

        #region GET Methods

        /// <summary>
        /// Send GET request and return string response
        /// </summary>
        public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Send GET request and return HttpResponse
        /// </summary>
        public async Task<HttpResponse> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Get, url, null, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send GET request with headers
        /// </summary>
        public async Task<HttpResponse> GetAsync(string url, Dictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Get, url, null, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send GET request and return byte array
        /// </summary>
        public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        #endregion

        #region POST Methods

        /// <summary>
        /// Send POST request with JSON body
        /// </summary>
        public async Task<HttpResponse> PostJsonAsync(string url, string jsonBody, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await SendAsync(HttpMethod.Post, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send POST request with form data
        /// </summary>
        public async Task<HttpResponse> PostFormAsync(string url, Dictionary<string, string> formData, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(formData);
            return await SendAsync(HttpMethod.Post, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send POST request with custom content
        /// </summary>
        public async Task<HttpResponse> PostAsync(string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Post, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send POST request with JSON body and headers
        /// </summary>
        public async Task<HttpResponse> PostJsonAsync(string url, string jsonBody, Dictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await SendAsync(HttpMethod.Post, url, content, headers, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region PUT Methods

        /// <summary>
        /// Send PUT request with JSON body
        /// </summary>
        public async Task<HttpResponse> PutJsonAsync(string url, string jsonBody, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await SendAsync(HttpMethod.Put, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send PUT request with custom content
        /// </summary>
        public async Task<HttpResponse> PutAsync(string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Put, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region DELETE Methods

        /// <summary>
        /// Send DELETE request
        /// </summary>
        public async Task<HttpResponse> DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Delete, url, null, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send DELETE request with headers
        /// </summary>
        public async Task<HttpResponse> DeleteAsync(string url, Dictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Delete, url, null, headers, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region PATCH Methods

        /// <summary>
        /// Send PATCH request with JSON body
        /// </summary>
        public async Task<HttpResponse> PatchJsonAsync(string url, string jsonBody, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var method = new HttpMethod("PATCH");
            return await SendAsync(method, url, content, null, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Core Send Method

        /// <summary>
        /// Send HTTP request with full control
        /// </summary>
        public async Task<HttpResponse> SendAsync(
            HttpMethod method,
            string url,
            HttpContent content = null,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(method, url);

            if (content != null)
            {
                request.Content = content;
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            return new HttpResponse
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Content = await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                Headers = GetResponseHeaders(response)
            };
        }

        private Dictionary<string, string> GetResponseHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>();
            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            return headers;
        }

        #endregion

        #region Retry Methods

        /// <summary>
        /// Send request with retry policy
        /// </summary>
        public async Task<HttpResponse> SendWithRetryAsync(
            HttpMethod method,
            string url,
            HttpContent content = null,
            int maxRetries = 3,
            int delayMilliseconds = 1000,
            CancellationToken cancellationToken = default)
        {
            Exception lastException = null;

            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    var response = await SendAsync(method, url, content, null, cancellationToken).ConfigureAwait(false);

                    // Retry on server errors (5xx)
                    if (response.StatusCode >= 500 && i < maxRetries)
                    {
                        await Task.Delay(delayMilliseconds * (i + 1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return response;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (i < maxRetries)
                    {
                        await Task.Delay(delayMilliseconds * (i + 1), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout
                    lastException = ex;
                    if (i < maxRetries)
                    {
                        await Task.Delay(delayMilliseconds * (i + 1), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new HttpRequestException($"Request failed after {maxRetries} retries", lastException);
        }

        /// <summary>
        /// GET with retry
        /// </summary>
        public async Task<HttpResponse> GetWithRetryAsync(string url, int maxRetries = 3, CancellationToken cancellationToken = default)
        {
            return await SendWithRetryAsync(HttpMethod.Get, url, null, maxRetries, 1000, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// POST JSON with retry
        /// </summary>
        public async Task<HttpResponse> PostJsonWithRetryAsync(string url, string jsonBody, int maxRetries = 3, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await SendWithRetryAsync(HttpMethod.Post, url, content, maxRetries, 1000, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Set Bearer token for authentication
        /// </summary>
        public void SetBearerToken(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// Add custom header
        /// </summary>
        public void AddHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }

        /// <summary>
        /// Remove header
        /// </summary>
        public void RemoveHeader(string name)
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
        }

        /// <summary>
        /// Build URL with query parameters
        /// </summary>
        public static string BuildUrl(string baseUrl, Dictionary<string, string> queryParams)
        {
            if (queryParams == null || queryParams.Count == 0)
                return baseUrl;

            var sb = new StringBuilder(baseUrl);
            sb.Append(baseUrl.Contains("?") ? "&" : "?");

            bool first = true;
            foreach (var param in queryParams)
            {
                if (!first) sb.Append("&");
                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(param.Value ?? ""));
                first = false;
            }

            return sb.ToString();
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// HTTP Client configuration
    /// </summary>
    public class HttpClientConfig
    {
        /// <summary>
        /// Base URL for all requests
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Request timeout in seconds (default: 30)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Bearer token for authentication
        /// </summary>
        public string BearerToken { get; set; }

        /// <summary>
        /// Basic auth username
        /// </summary>
        public string BasicAuthUsername { get; set; }

        /// <summary>
        /// Basic auth password
        /// </summary>
        public string BasicAuthPassword { get; set; }

        /// <summary>
        /// Ignore SSL certificate errors (use with caution)
        /// </summary>
        public bool IgnoreSslErrors { get; set; } = false;

        /// <summary>
        /// Default headers to include in all requests
        /// </summary>
        public Dictionary<string, string> DefaultHeaders { get; set; }
    }

    /// <summary>
    /// HTTP Response wrapper
    /// </summary>
    public class HttpResponse
    {
        /// <summary>
        /// HTTP status code
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Whether the request was successful (2xx)
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Status reason phrase
        /// </summary>
        public string ReasonPhrase { get; set; }

        /// <summary>
        /// Response body as string
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Response headers
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Throw exception if not successful
        /// </summary>
        public HttpResponse EnsureSuccess()
        {
            if (!IsSuccess)
            {
                throw new HttpRequestException($"HTTP {StatusCode}: {ReasonPhrase}");
            }
            return this;
        }
    }
}
