﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FaunaDB.Collections;
using FaunaDB.Errors;
using FaunaDB.Query;
using FaunaDB.Types;
using Newtonsoft.Json;

namespace FaunaDB.Client
{
    /// <summary>
    /// Directly communicates with FaunaDB via JSON.
    /// </summary>
    public class Client
    {
        readonly IClientIO clientIO;

        /// <param name="domain">Base URL for the FaunaDB server.</param>
        /// <param name="scheme">Scheme of the FaunaDB server. Should be "http" or "https".</param>
        /// <param name="port">Port of the FaunaDB server.</param>
        /// <param name="timeout">Timeout. Defaults to 1 minute.</param>
        /// <param name="secret">Auth token for the FaunaDB server.</param>
        /// <param name="clientIO">Optional IInnerClient. Used only for testing.</param>"> 
        public Client(
            string domain = "rest.faunadb.com",
            string scheme = "https",
            int? port = null,
            TimeSpan? timeout = null,
            string secret = null,
            IClientIO clientIO = null)
        {
            if (port == null)
                port = scheme == "https" ? 443 : 80;

            this.clientIO = clientIO ??
                new DefaultClientIO(new Uri(scheme + "://" + domain + ":" + port), timeout ?? TimeSpan.FromSeconds(60), secret);
        }

        /// <summary>
        /// Use the FaunaDB query API.
        /// </summary>
        /// <param name="expression">Expression generated by methods of <see cref="Query"/>.</param>
        public Task<Value> Query(Expr expression) =>
            Execute(HttpMethodKind.Post, "", expression);

        public Task<IReadOnlyList<Value>> Query(params Expr[] expressions) =>
            ExecuteBatch(HttpMethodKind.Post, "", expressions);

        /// <summary>
        /// Ping FaunaDB.
        /// See the <see href="https://faunadb.com/documentation/rest#other">docs</see>. 
        /// </summary>
        public async Task<string> Ping(string scope = null, int? timeout = null) =>
            (string)await Execute(HttpMethodKind.Get, "ping", query: ImmutableDictionary.Of("scope", scope, "timeout", timeout?.ToString()))
                .ConfigureAwait(false);

        async Task<Value> Execute(HttpMethodKind action, string path, Expr data = null, IReadOnlyDictionary<string, string> query = null)
        {
            var dataString = data == null ?  null : data.ToJson();
            var responseHttp = await clientIO.DoRequest(action, path, dataString, query);

            RaiseForStatusCode(responseHttp);

            var responseContent = (ObjectV)Json.FromJson(responseHttp.ResponseContent);
            return responseContent["resource"];
        }

        async Task<IReadOnlyList<Value>> ExecuteBatch(HttpMethodKind action, string path, Expr[] data = null, IReadOnlyDictionary<string, string> query = null)
        {
            var dataString = data == null ?  null : UnescapedArray.Of(data).ToJson();
            var responseHttp = await clientIO.DoRequest(action, path, dataString, query);

            RaiseForStatusCode(responseHttp);

            var responseContent = (ObjectV)Json.FromJson(responseHttp.ResponseContent);
            return responseContent["resource"].Collect(Field.Root);
        }

        internal struct ErrorsWrapper
        {
            public IReadOnlyList<QueryError> Errors;
        }

        internal static void RaiseForStatusCode(RequestResult resultRequest)
        {
            var statusCode = resultRequest.StatusCode;

            if (statusCode >= 200 && statusCode < 300)
                return;

            var wrapper = JsonConvert.DeserializeObject<ErrorsWrapper>(resultRequest.ResponseContent);

            var response = new QueryErrorResponse(statusCode, wrapper.Errors);

            switch (statusCode)
            {
                case 400:
                    throw new BadRequest(response);
                case 401:
                    throw new Unauthorized(response);
                case 403:
                    throw new PermissionDenied(response);
                case 404:
                    throw new NotFound(response);
                case 405:
                    throw new MethodNotAllowed(response);
                case 500:
                    throw new InternalError(response);
                case 503:
                    throw new UnavailableError(response);
                default:
                    throw new UnknowException(response);
            }
        }

    }
}
