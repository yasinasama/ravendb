﻿// -----------------------------------------------------------------------
//  <copyright file="RequestRouter.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Raven.Server.Documents;
using System.Threading;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.Primitives;
using Raven.Server.Utils;
using Raven.Server.Web;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Routing
{
    public class RequestRouter
    {
        private readonly Trie<RouteInformation> _trie;
        private readonly RavenServer _ravenServer;
        private readonly MetricsCountersManager _serverMetrics;

        public RequestRouter(Dictionary<string, RouteInformation> routes, RavenServer ravenServer)
        {
            _trie = Trie<RouteInformation>.Build(routes);
            _ravenServer = ravenServer;
            _serverMetrics = ravenServer.Metrics;

        }

        public RouteInformation GetRoute(string method, string path, out RouteMatch match)
        {
            var tryMatch = _trie.TryMatch(method, path);
            match = tryMatch.Match;
            return tryMatch.Value;
        }

        public async ValueTask<string> HandlePath(HttpContext context, string method, string path)
        {
            var tryMatch = _trie.TryMatch(method, path);
            if (tryMatch.Value == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

                using (var ctx = JsonOperationContext.ShortTermSingleUse())
                using (var writer = new BlittableJsonTextWriter(ctx, context.Response.Body))
                {
                    ctx.Write(writer,
                        new DynamicJsonValue
                        {
                            ["Type"] = "Error",
                            ["Error"] = $"There is no handler for path: {method} {path}{context.Request.QueryString}"
                        });
                }
                return null;
            }

            var reqCtx = new RequestHandlerContext
            {
                HttpContext = context,
                RavenServer = _ravenServer,
                RouteMatch = tryMatch.Match,
                AllowResponseCompression = _ravenServer.Configuration.Http.AllowResponseCompression
            };

            var tuple = tryMatch.Value.TryGetHandler(reqCtx);
            var handler = tuple.Item1 ?? await tuple.Item2;

            reqCtx.Database?.Metrics?.RequestsMeter.Mark();
            _serverMetrics.RequestsMeter.Mark();

            Interlocked.Increment(ref _serverMetrics.ConcurrentRequestsCount);
            if (handler == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                using (var ctx = JsonOperationContext.ShortTermSingleUse())
                using (var writer = new BlittableJsonTextWriter(ctx, context.Response.Body))
                {
                    ctx.Write(writer,
                        new DynamicJsonValue
                        {
                            ["Type"] = "Error",
                            ["Message"] = $"There is no handler for {context.Request.Method} {context.Request.Path}"
                        });
                }
                return null;
            }

            if (_ravenServer.Configuration.Security.AuthenticationEnabled)
            {
                var authResult = TryAuthorize(tryMatch.Value, context, reqCtx.Database);
                if (authResult == false)
                    return reqCtx.Database?.Name;
            }

            if (reqCtx.Database != null)
            {
                using (reqCtx.Database.DatabaseInUse(tryMatch.Value.SkipUsagesCount))
                    await handler(reqCtx);
            }
            else
            {
                await handler(reqCtx);
            }

            Interlocked.Decrement(ref _serverMetrics.ConcurrentRequestsCount);

            return reqCtx.Database?.Name;
        }

        private bool TryAuthorize(RouteInformation route, HttpContext context, DocumentDatabase database)
        {
            var feature = context.Features.Get<IHttpAuthenticationFeature>() as RavenServer.AuthenticateConnection;
            
            switch (route.AuthorizationStatus)
            {
                case AuthorizationStatus.UnauthenticatedClients:
                    var userWantsToAccessStudioMainPage = context.Request.Path == "/studio/index.html";
                    if (userWantsToAccessStudioMainPage)
                    {
                        switch (feature?.Status)
                        {
                            case null:
                            case RavenServer.AuthenticationStatus.NoCertificateProvided:
                            case RavenServer.AuthenticationStatus.Expired:
                            case RavenServer.AuthenticationStatus.NotYetValid:
                            case RavenServer.AuthenticationStatus.None:
                            case RavenServer.AuthenticationStatus.UnfamiliarCertificate:
                                UnlikelyFailAuthorization(context, database?.Name, feature);
                                return false;
                        }
                    }
                    
                    return true;
                case AuthorizationStatus.ClusterAdmin:
                case AuthorizationStatus.Operator:
                case AuthorizationStatus.ValidUser:
                case AuthorizationStatus.DatabaseAdmin:

                    switch (feature?.Status)
                    {
                        case null:
                        case RavenServer.AuthenticationStatus.NoCertificateProvided:
                        case RavenServer.AuthenticationStatus.Expired:
                        case RavenServer.AuthenticationStatus.NotYetValid:
                        case RavenServer.AuthenticationStatus.None:
                        case RavenServer.AuthenticationStatus.UnfamiliarCertificate:
                            UnlikelyFailAuthorization(context, database?.Name, feature);
                            return false;
                        case RavenServer.AuthenticationStatus.Allowed:
                            if (route.AuthorizationStatus == AuthorizationStatus.Operator || route.AuthorizationStatus == AuthorizationStatus.ClusterAdmin)
                                goto case RavenServer.AuthenticationStatus.None;

                            if (database == null)
                                return true;

                            if (feature.CanAccess(database.Name, route.AuthorizationStatus == AuthorizationStatus.DatabaseAdmin))
                                return true;

                            goto case RavenServer.AuthenticationStatus.None;
                        case RavenServer.AuthenticationStatus.Operator:
                            if (route.AuthorizationStatus == AuthorizationStatus.ClusterAdmin)
                                goto case RavenServer.AuthenticationStatus.None;
                            return true;
                        case RavenServer.AuthenticationStatus.ClusterAdmin:
                            return true;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                default:
                    ThrowUnknownAuthStatus(route);
                    return false; // never hit
            }
        }

        private static void ThrowUnknownAuthStatus(RouteInformation route)
        {
            throw new ArgumentOutOfRangeException("Unknown route auth status: " + route.AuthorizationStatus);
        }

        public void UnlikelyFailAuthorization(HttpContext context, string database, RavenServer.AuthenticateConnection feature)
        {
            string message;
            if (feature == null || feature.Status == RavenServer.AuthenticationStatus.None || feature.Status == RavenServer.AuthenticationStatus.NoCertificateProvided)
            {
                message = "This server requires client certificate for authentication, but none was provided by the client.";
            }
            else
            {
                var name = feature.Certificate.FriendlyName;
                if (string.IsNullOrWhiteSpace(name))
                    name = feature.Certificate.Subject;
                if (string.IsNullOrWhiteSpace(name))
                    name = feature.Certificate.ToString(false);

                if (feature.Status == RavenServer.AuthenticationStatus.UnfamiliarCertificate)
                {
                    message = "Supplied client certificate '" + name + "' is unknown to the server. In order to register your certificate please contact your system administrator.";
                }
                else if (feature.Status == RavenServer.AuthenticationStatus.Allowed)
                {
                    message = "Could not authorize access to " + (database ?? "the server") + " using provided client certificate '" + name + "'.";
                }
                else if (feature.Status == RavenServer.AuthenticationStatus.Operator)
                {
                    message = "Insufficient security clearance to access " + (database ?? "the server") + " using provided client certificate '" + name + "'.";
                }
                else if (feature.Status == RavenServer.AuthenticationStatus.Expired)
                {
                    message = "Supplied client certificate '" + name + "' has expired on " + feature.Certificate.NotAfter.ToString("D") + ". Please contact your system administrator in order to obtain a new one.";
                }
                else if (feature.Status == RavenServer.AuthenticationStatus.NotYetValid)
                {
                    message = "Supplied client certificate '" + name + "'cannot be used before " + feature.Certificate.NotBefore.ToString("D");
                }
                else
                {
                    message = "Access to the server was denied.";
                }
            }
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            using (var ctx = JsonOperationContext.ShortTermSingleUse())
            using (var writer = new BlittableJsonTextWriter(ctx, context.Response.Body))
            {
                DrainRequest(ctx, context);

                if (RavenServerStartup.IsHtmlAcceptable(context))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                    context.Response.Headers["Location"] = "/studio-auth-error.html?err=" + Uri.EscapeDataString(message);
                    return;
                }

                ctx.Write(writer,
                    new DynamicJsonValue
                    {
                        ["Type"] = "InvalidAuth",
                        ["Message"] = message
                    });
            }
        }

        private static void DrainRequest(JsonOperationContext ctx, HttpContext context)
        {
            if (context.Response.Headers.TryGetValue("Connection", out StringValues value) && value == "close")
                return; // don't need to drain it, the connection will close 

            using (ctx.GetManagedBuffer(out JsonOperationContext.ManagedPinnedBuffer buffer))
            {
                var requestBody = context.Request.Body;
                while (true)
                {
                    var read = requestBody.Read(buffer.Buffer.Array, buffer.Buffer.Offset, buffer.Buffer.Count);
                    if (read == 0)
                        break;
                }
            }
        }
    }
}
