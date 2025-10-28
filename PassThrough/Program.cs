using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PassThrough;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;
// using Yarp.ReverseProxy.Forwarder;
// using Yarp.ReverseProxy.Transforms;

try
{

    await RootCertificates.Install();

#pragma warning disable CA2252 // This API requires opting into preview features
    if (QuicListener.IsSupported)
    {
        Console.Out.WriteLine("Quic is available");
    } else
    {
        Console.Out.WriteLine("Quic is not available");
    }
#pragma warning restore CA2252 // This API requires opting into preview features

    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";


    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(kestrel =>
    {

        kestrel.ListenAnyIP(int.Parse(port), portOptions => {
            portOptions.Protocols = HttpProtocols.Http1;
        });
    });

    // builder.Services.AddHttpForwarder();

    builder.Services.AddCors((o) => o.AddDefaultPolicy((p) => p
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
    ));

    var app = builder.Build();

    app.UseCors();
    // Configure our own HttpMessageInvoker for outbound calls for proxy operations
    var options = new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        // ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);

                var sslStream = new SslStream(new NetworkStream(socket, ownsSocket: true));

                // When using HTTP/2, you must also keep in mind to set options like ApplicationProtocols
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = context.DnsEndPoint.Host,

                }, cancellationToken);

                return sslStream;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
    options.SslOptions.RemoteCertificateValidationCallback = (a, b, c, d) => true;
    var httpClient = new HttpClient(options);

    // Setup our own request transform class
    // var requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };

    app.UseRouting();

    // When using IHttpForwarder for direct forwarding you are responsible for routing, destination discovery, load balancing, affinity, etc..
    // For an alternate example that includes those features see BasicYarpSample.
    app.Map("/api/emails/d/{ei}/{emailID}/{destinationHost}/{**catchAll}", async (HttpContext httpContext) =>
    {
        var request = httpContext.Request;

        var destinationHost = request.RouteValues.GetValueOrDefault("destinationHost")!.ToString();
        var all = request.RouteValues.GetValueOrDefault("catchAll", "")!;

        var queryString = request.QueryString;

        var url = RequestUtilities.MakeDestinationAddress(
                    "https://" + destinationHost,
                    $"/{all}",
                    queryString);

        // Console.WriteLine($"Get: {url}");

        var response = httpContext.Response;

        var clientRequest = new HttpRequestMessage(HttpMethod.Get, url);
        foreach(var hk in request.Headers)
        {
            if (hk.Key.EndsWith(":") || hk.Key.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            clientRequest.Headers.TryAddWithoutValidation(hk.Key, hk.Value.AsReadOnly());
        }

        using var rs = await httpClient.SendAsync(clientRequest, httpContext.RequestAborted);
        response.StatusCode = (int)rs.StatusCode;
        if (!rs.IsSuccessStatusCode)
        {
            // send error as it is but log
            Console.WriteLine($"Status {rs.StatusCode} https://{destinationHost}/${all}");
        }

        foreach(var key in rs.Headers) {
            foreach(var v in key.Value)
            {
                response.Headers.TryAdd(key.Key, v);
            }
        }

        foreach (var key in rs.Content.Headers)
        {
            foreach (var v in key.Value)
            {
                response.Headers.TryAdd(key.Key, v);
            }
        }

        response.Headers.CacheControl = "public, max-age=2592000, immutable";

        using var s = await rs.Content.ReadAsStreamAsync();
        await s.CopyToAsync(response.Body);

        //var error = await forwarder.SendAsync(httpContext, "https://" + destinationHost, httpClient, requestOptions,
        //    (context, proxyRequest) =>
        //    {
        //        // Customize the query string:
        //        var queryContext = new QueryTransformContext(context.Request);

        //        // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
        //        proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(
        //            "https://" + destinationHost,
        //            $"/{all}",
        //            queryContext.QueryString);
        //        proxyRequest.Headers.Host = destinationHost;
        //        Console.WriteLine($"New-Request: {proxyRequest.RequestUri}");
        //        return default;
        //    });

        // Check if the proxy operation was successful
        //if (error != ForwarderError.None)
        //{
        //    var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
        //    if (errorFeature != null)
        //    {
        //        var exception = errorFeature.Exception;
        //        if (exception != null)
        //        {
        //            Console.WriteLine(exception.ToString());
        //        }
        //    }
        //}
    });

    app.Run();



} catch (Exception ex)
{
    Console.WriteLine(ex);
    throw new Exception("Closed", ex);
}
// }, null);
