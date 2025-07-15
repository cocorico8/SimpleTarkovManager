using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleTarkovManager.Services
{
    public class DebuggingHttpHandler : DelegatingHandler
    {
        public DebuggingHttpHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine("--- HTTP Request ---");
            Console.WriteLine($"Request: {request.Method} {request.RequestUri}");

            // Log Headers
            Console.WriteLine("Headers:");
            foreach (var header in request.Headers)
            {
                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                }
            }
            
            // Log Request Body if it exists
            if (request.Content != null)
            {
                var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Body: {requestBody}");
            }
            
            // Send the request and get the response
            var response = await base.SendAsync(request, cancellationToken);
            
            Console.WriteLine("\n--- HTTP Response ---");
            Console.WriteLine($"Status Code: {(int)response.StatusCode} {response.ReasonPhrase}");

            // Log Response Headers
            Console.WriteLine("Headers:");
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }
            foreach (var header in response.Content.Headers)
            {
                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }
            
            // IMPORTANT: We don't read the response body here, because it's a stream that can only be read once.
            // Our existing service code will handle reading and logging the decompressed body.
            Console.WriteLine("-------------------------------------------------");

            return response;
        }
    }
}