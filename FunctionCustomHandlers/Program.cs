using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Newtonsoft.Json;
using HttpVersion = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpVersion;

namespace FunctionCustomHandlers
{
    class Program
    {
        private static TcpListener _listener;

        private static readonly string FunctionsHttpWorkerPort =
            Environment.GetEnvironmentVariable("FUNCTIONS_HTTPWORKER_PORT") ?? "9080";


        static async Task Main(string[] args)
        {
            _listener = new TcpListener(IPAddress.Loopback, int.Parse(FunctionsHttpWorkerPort));
            _listener.Start();
            Console.WriteLine($"server listening on {FunctionsHttpWorkerPort}");

            try
            {
                var connection = await _listener.AcceptTcpClientAsync();
                while (true)
                {
                    ThreadPool.QueueUserWorkItem(async _ =>
                    {
                        try
                        {
                            await new Handler().Run(connection);
                        }
                        finally
                        {
                            connection.Close();
                            connection.Dispose();
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Something went wrong {e.Message}");
            }
        }
    }


    internal class Handler : IHttpRequestLineHandler, IHttpHeadersHandler
    {
        private string _path;


        private static readonly Dictionary<string, Func<string, string>> ResponseProcessors =
            new Dictionary<string, Func<string, string>>
            {
                [nameof(QueueTrigger)] = QueueTrigger,
                [nameof(CosmosDbTrigger)] = CosmosDbTrigger,
                [nameof(HttpTrigger)] = HttpTrigger
            };


        public async Task Run(TcpClient connection)
        {
            await using var ns = connection.GetStream();
            await using var sw = new StreamWriter(ns, Encoding.Default);

            var bytes = new byte[1000 * 1000];
            await ns.ReadAsync(bytes, 0, 1000 * 1000);

            var buffer = new ReadOnlySequence<byte>(bytes);

            var parser = new HttpParser<Handler>();
            parser.ParseRequestLine(this, buffer, out var consumed, out _);

            buffer = buffer.Slice(consumed);
            parser.ParseHeaders(this, buffer, out consumed, out _, out _);

            buffer = buffer.Slice(consumed);

            var bodyString = GetBodyString(buffer);


            if (ResponseProcessors.TryGetValue(_path, out var processor))
            {
                var responseBody = processor(bodyString);

                await sw.WriteAsync($"HTTP/1.1 200 Ok {Environment.NewLine}"
                                    + $"Content-Length: {responseBody.Length}{Environment.NewLine}"
                                    + $"Content-Type: application/json{Environment.NewLine}"
                                    + Environment.NewLine
                                    + responseBody
                                    + Environment.NewLine + Environment.NewLine);
            }
            else
            {
                await sw.WriteAsync($"HTTP/1.1 400 Bad request {Environment.NewLine}");
            }

            await sw.FlushAsync();
        }

        private static string GetBodyString(ReadOnlySequence<byte> buffer)
        {
            var bodyString = Encoding.Default.GetString(buffer.ToSpan());

            if (!string.IsNullOrEmpty(bodyString))
            {
                var index = bodyString.IndexOf("{", StringComparison.Ordinal);

                if (index != -1)
                {
                    bodyString = bodyString.Substring(index);
                }

                index = bodyString.LastIndexOf("}", StringComparison.Ordinal);
                if (index != -1)
                {
                    bodyString = bodyString.Substring(0, index + 1);
                }
            }

            return bodyString;
        }

        private static string CosmosDbTrigger(string input)
        {
            try
            {
                var body = JsonConvert.DeserializeObject<InvocationRequest>(input);
                var response = new InvocationResponse();

                var message = new { messageFromCosmos = body.Data["items"] };
                response.Outputs["output"] =
                    Convert.ToBase64String(Encoding.Default.GetBytes(JsonConvert.SerializeObject(message)));

                return JsonConvert.SerializeObject(response);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string QueueTrigger(string input)
        {
            try
            {
                var req = JsonConvert.DeserializeObject<InvocationRequest>(input);

                var response = new InvocationResponse
                {
                    Outputs =
                    {
                        ["output"] = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                            JsonConvert.SerializeObject(new { messageFromQueue = req.Data["items"].ToString() })))
                    }
                };

                return JsonConvert.SerializeObject(response);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string HttpTrigger(string input)
        {
            if (!string.IsNullOrEmpty(input))
            {
                try
                {
                    var body = JsonConvert.DeserializeObject<Dictionary<string, object>>(input);

                    var response = new
                    {
                        YouWrote = body
                    };

                    return JsonConvert.SerializeObject(response);
                }
                catch
                {
                }
            }

            return string.Empty;
        }


        public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path,
            Span<byte> query, Span<byte> customMethod,
            bool pathEncoded)
        {
            _path = Encoding.Default.GetString(path).Substring(1);
        }

        public void OnHeader(Span<byte> name, Span<byte> value)
        {
            //intentionally ignore
        }
    }
}