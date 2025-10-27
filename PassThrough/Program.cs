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
using Yarp.ReverseProxy.Transforms;

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

    builder.Services.AddHttpForwarder();

    var app = builder.Build();

    // Configure our own HttpMessageInvoker for outbound calls for proxy operations
    var httpClient = new HttpMessageInvoker(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    });

    // Setup our own request transform class
    var requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };

    app.UseRouting();

    // When using IHttpForwarder for direct forwarding you are responsible for routing, destination discovery, load balancing, affinity, etc..
    // For an alternate example that includes those features see BasicYarpSample.
    app.Map("/{**catch-all}", async (HttpContext httpContext, IHttpForwarder forwarder) =>
    {
        var error = await forwarder.SendAsync(httpContext, "http://" + httpContext.Request.Headers.Host, httpClient, requestOptions,
            (context, proxyRequest) =>
            {
                // Customize the query string:
                var queryContext = new QueryTransformContext(context.Request);
                var ip = context.Connection.RemoteIpAddress;
                if (ip != null)
                {
                    proxyRequest.Headers.TryAddWithoutValidation("x-forwarded-for", ip.ToString());
                }

                // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
                proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress("http://" + proxyRequest.Headers.Host, context.Request.Path, queryContext.QueryString);
                proxyRequest.Version = HttpVersion.Version11;
                return default;
            });

        // Check if the proxy operation was successful
        if (error != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            if (errorFeature != null)
            {
                var exception = errorFeature.Exception;
                if (exception != null)
                {
                    Console.WriteLine(exception.ToString());
                }
            }
        }
    });

    app.Run();



} catch (Exception ex)
{
    Console.WriteLine(ex);
    throw new Exception("Closed", ex);
}
// }, null);
